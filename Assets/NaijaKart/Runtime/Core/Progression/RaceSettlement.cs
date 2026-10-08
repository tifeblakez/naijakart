using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Net;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Social;

namespace NaijaKart.Core.Progression
{
    public interface IProfileStore
    {
        PlayerProfile Get(string playerId);
        void Save(PlayerProfile profile);
        /// <summary>Snapshot of all profiles (leaderboards). Small-scale only; a database adapter pages/sorts server-side.</summary>
        IEnumerable<PlayerProfile> All();
    }

    public interface IRivalryStore
    {
        Rivalry GetOrCreate(string a, string b);
        void Save(Rivalry rivalry);
        IEnumerable<Rivalry> For(string playerId);
    }

    public sealed class InMemoryProfileStore : IProfileStore
    {
        private readonly Dictionary<string, PlayerProfile> _profiles = new Dictionary<string, PlayerProfile>();
        private readonly ProgressionConfig _cfg;

        public InMemoryProfileStore(ProgressionConfig cfg)
        {
            _cfg = cfg;
        }

        public PlayerProfile Get(string playerId)
        {
            if (!_profiles.TryGetValue(playerId, out var p))
            {
                p = new PlayerProfile { PlayerId = playerId, DisplayName = playerId, Rating = _cfg.ratingStart };
                _profiles[playerId] = p;
            }
            return p;
        }

        public void Save(PlayerProfile profile) => _profiles[profile.PlayerId] = profile;
        public IEnumerable<PlayerProfile> All() => new List<PlayerProfile>(_profiles.Values);
    }

    public sealed class InMemoryRivalryStore : IRivalryStore
    {
        private readonly Dictionary<string, Rivalry> _rivalries = new Dictionary<string, Rivalry>();

        public Rivalry GetOrCreate(string a, string b)
        {
            string key = Rivalry.KeyFor(a, b);
            if (!_rivalries.TryGetValue(key, out var r))
            {
                r = Rivalry.Create(a, b);
                _rivalries[key] = r;
            }
            return r;
        }

        public void Save(Rivalry rivalry) => _rivalries[Rivalry.KeyFor(rivalry.PlayerA, rivalry.PlayerB)] = rivalry;

        public IEnumerable<Rivalry> For(string playerId)
        {
            var list = new List<Rivalry>();
            foreach (var r in _rivalries.Values) if (r.PlayerA == playerId || r.PlayerB == playerId) list.Add(r);
            return list;
        }
    }

    /// <summary>
    /// Turns authoritative RaceResults into persisted progression: XP, Coins (via the ledger with
    /// idempotency keys derived from the race id so a crash/retry can never double-pay), rating and
    /// rank changes, streaks and rivalry records (PRD §39, §40, §56, §68).
    /// </summary>
    public sealed class RaceSettlementService
    {
        private readonly GameConfig _cfg;
        private readonly CoinLedger _ledger;
        private readonly IProfileStore _profiles;
        private readonly IRivalryStore _rivalries;
        private readonly RankLadder _ladder;
        private readonly HashSet<string> _settledRaces = new HashSet<string>();

        public RaceSettlementService(GameConfig cfg, CoinLedger ledger, IProfileStore profiles, IRivalryStore rivalries)
        {
            _cfg = cfg;
            _ledger = ledger;
            _profiles = profiles;
            _rivalries = rivalries;
            _ladder = new RankLadder(cfg.progression);
        }

        public bool HasSettled(string raceId) => _settledRaces.Contains(raceId);

        public List<SettledRewardDto> Settle(RaceResults results, float xpMultiplier = 1f)
        {
            var rewards = new List<SettledRewardDto>();
            if (results == null || results.Cancelled || _settledRaces.Contains(results.RaceId)) return rewards;
            _settledRaces.Add(results.RaceId);

            bool ranked = results.Mode == RaceMode.Ranked || results.Mode == RaceMode.Tournament;
            var ratingEntries = new List<RatingCalculator.Entry>();
            foreach (var e in results.Entries)
            {
                if (e.IsBot) continue;
                var profile = _profiles.Get(e.PlayerId);
                ratingEntries.Add(new RatingCalculator.Entry { PlayerId = e.PlayerId, Rating = profile.Rating, FinishPosition = e.FinishPosition });
            }
            var deltas = ranked ? RatingCalculator.ComputeDeltas(ratingEntries, _cfg.progression) : new Dictionary<string, int>();

            foreach (var e in results.Entries)
            {
                if (e.IsBot) continue;
                var profile = _profiles.Get(e.PlayerId);
                var reward = RewardCalculator.Compute(e.FinishPosition, e.Finished, e.LapsCompleted, e.Stats, _cfg);
                if (xpMultiplier > 0f && xpMultiplier != 1f) reward.Xp = (int)System.Math.Round(reward.Xp * xpMultiplier);
                int levelBefore = XpCurve.LevelForXp(profile.TotalXp, _cfg.progression);
                string rankBefore = _ladder.TierFor(profile.Rating).id;

                profile.TotalXp += reward.Xp;
                profile.Races++;
                if (e.FinishPosition == 1 && e.Finished)
                {
                    profile.Wins++;
                    profile.CurrentWinStreak++;
                    if (profile.CurrentWinStreak > profile.BestWinStreak) profile.BestWinStreak = profile.CurrentWinStreak;
                }
                else
                {
                    profile.CurrentWinStreak = 0;
                }
                if (e.Finished && e.FinishPosition <= 3) profile.Podiums++;
                if (e.Stats != null)
                {
                    profile.LastmaEscapes += e.Stats.LastmaEscapes;
                    if (e.Status == Race.ParticipantStatus.Eliminated) profile.LastmaArrests++;
                }
                int delta = deltas.TryGetValue(e.PlayerId, out int d) ? d : 0;
                profile.Rating = System.Math.Max(0, profile.Rating + delta);
                _profiles.Save(profile);

                _ledger.EnsureAccount(e.PlayerId);
                if (reward.Coins > 0)
                {
                    _ledger.Credit(e.PlayerId, reward.Coins, "race_reward:" + results.Mode, results.RaceId + ":reward:" + e.PlayerId, out _);
                }

                rewards.Add(new SettledRewardDto
                {
                    PlayerId = e.PlayerId,
                    Xp = reward.Xp,
                    Coins = reward.Coins,
                    RatingDelta = delta,
                    NewRating = profile.Rating,
                    RankIdBefore = rankBefore,
                    RankIdAfter = _ladder.TierFor(profile.Rating).id,
                    LevelBefore = levelBefore,
                    LevelAfter = XpCurve.LevelForXp(profile.TotalXp, _cfg.progression),
                    CoinBalance = _ledger.GetBalance(e.PlayerId)
                });
            }

            UpdateRivalries(results);
            return rewards;
        }

        private void UpdateRivalries(RaceResults results)
        {
            var humans = new List<RaceResultEntry>();
            foreach (var e in results.Entries) if (!e.IsBot) humans.Add(e);
            for (int i = 0; i < humans.Count; i++)
            {
                for (int j = i + 1; j < humans.Count; j++)
                {
                    var a = humans[i];
                    var b = humans[j];
                    var r = _rivalries.GetOrCreate(a.PlayerId, b.PlayerId);
                    string winner = a.FinishPosition < b.FinishPosition ? a.PlayerId : b.PlayerId;
                    float lapA = r.PlayerA == a.PlayerId ? a.BestLap : b.BestLap;
                    float lapB = r.PlayerA == a.PlayerId ? b.BestLap : a.BestLap;
                    r.Record(winner, lapA, lapB);
                    _rivalries.Save(r);
                }
            }
        }
    }
}
