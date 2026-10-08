using System;
using System.Collections.Generic;
using System.Globalization;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;

namespace NaijaKart.Core.Challenges
{
    public interface IChallengeProgressStore
    {
        List<ChallengeProgress> Get(string playerId);
        void Save(string playerId, List<ChallengeProgress> progress);
    }

    public sealed class InMemoryChallengeProgressStore : IChallengeProgressStore
    {
        private readonly Dictionary<string, List<ChallengeProgress>> _data = new Dictionary<string, List<ChallengeProgress>>();
        public List<ChallengeProgress> Get(string playerId) => _data.TryGetValue(playerId, out var l) ? l : (_data[playerId] = new List<ChallengeProgress>());
        public void Save(string playerId, List<ChallengeProgress> progress) => _data[playerId] = progress;
    }

    public sealed class ChallengeCompletion
    {
        public string PlayerId;
        public ChallengeDefinition Challenge;
        public long Coins;
        public int Xp;
        public string Title;
    }

    /// <summary>
    /// Applies a finished race to every enabled rule for each human racer, completes rules that reach
    /// their target, and pays rewards through the ledger with idempotency keys so a replayed settlement
    /// never pays twice. Period boundaries are computed in UTC from the race's settlement time.
    /// </summary>
    public sealed class ChallengeEvaluator
    {
        private readonly ChallengeDefinition[] _rules;
        private readonly IChallengeProgressStore _store;
        private readonly CoinLedger _ledger;
        private readonly Func<DateTime> _clock;

        public ChallengeEvaluator(ChallengeLibrary library, IChallengeProgressStore store, CoinLedger ledger, Func<DateTime> utcClock = null)
        {
            _rules = library?.challenges ?? Array.Empty<ChallengeDefinition>();
            _store = store;
            _ledger = ledger;
            _clock = utcClock ?? (() => DateTime.UtcNow);
        }

        public static string PeriodKey(ChallengeCadence cadence, DateTime utc)
        {
            switch (cadence)
            {
                case ChallengeCadence.Daily: return utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case ChallengeCadence.Weekly:
                    int week = ISOWeek.GetWeekOfYear(utc);
                    return ISOWeek.GetYear(utc).ToString(CultureInfo.InvariantCulture) + "-W" + week.ToString("00", CultureInfo.InvariantCulture);
                default: return "";
            }
        }

        /// <summary>Evaluates a race for all human entries. Returns completions (for UI toasts).</summary>
        public List<ChallengeCompletion> Apply(RaceResults results, Func<string, IReadOnlyCollection<string>> friendsOf = null,
            Func<string, HashSet<string>> tracksWonBefore = null)
        {
            var completions = new List<ChallengeCompletion>();
            if (results == null || results.Cancelled) return completions;
            DateTime now = _clock();
            foreach (var entry in results.Entries)
            {
                if (entry.IsBot) continue;
                var progress = _store.Get(entry.PlayerId);
                foreach (var rule in _rules)
                {
                    if (rule.disabled) continue;
                    int delta = MetricDelta(rule.metric, entry, results, friendsOf, tracksWonBefore);
                    if (delta <= 0) continue;
                    string period = PeriodKey(rule.cadence, now);
                    var p = Find(progress, rule.id, period);
                    if (p.completed) continue;
                    p.value += delta;
                    if (p.value >= rule.target)
                    {
                        p.value = rule.target;
                        p.completed = true;
                        Pay(entry.PlayerId, rule, period, p);
                        completions.Add(new ChallengeCompletion { PlayerId = entry.PlayerId, Challenge = rule, Coins = rule.rewardCoins, Xp = rule.rewardXp, Title = rule.rewardTitle });
                    }
                }
                _store.Save(entry.PlayerId, progress);
            }
            return completions;
        }

        public IReadOnlyList<ChallengeProgress> CurrentProgress(string playerId)
        {
            DateTime now = _clock();
            var all = _store.Get(playerId);
            var list = new List<ChallengeProgress>();
            foreach (var rule in _rules)
            {
                if (rule.disabled) continue;
                list.Add(Find(all, rule.id, PeriodKey(rule.cadence, now)));
            }
            return list;
        }

        public ChallengeDefinition Rule(string id)
        {
            foreach (var r in _rules) if (r.id == id) return r;
            return null;
        }

        private static ChallengeProgress Find(List<ChallengeProgress> list, string id, string period)
        {
            foreach (var p in list) if (p.challengeId == id && p.periodKey == period) return p;
            var np = new ChallengeProgress { challengeId = id, periodKey = period };
            list.Add(np);
            return np;
        }

        private void Pay(string playerId, ChallengeDefinition rule, string period, ChallengeProgress p)
        {
            if (p.rewardClaimed || _ledger == null) return;
            if (rule.rewardCoins > 0)
            {
                _ledger.EnsureAccount(playerId);
                _ledger.Credit(playerId, rule.rewardCoins, "challenge:" + rule.id, $"challenge:{playerId}:{rule.id}:{period}", out _);
            }
            p.rewardClaimed = true;
        }

        private static int MetricDelta(ChallengeMetric metric, RaceResultEntry e, RaceResults results,
            Func<string, IReadOnlyCollection<string>> friendsOf, Func<string, HashSet<string>> tracksWonBefore)
        {
            var s = e.Stats ?? new ParticipantStats();
            bool won = e.Finished && e.FinishPosition == 1;
            switch (metric)
            {
                case ChallengeMetric.RacesCompleted: return e.Finished ? 1 : 0;
                case ChallengeMetric.Wins: return won ? 1 : 0;
                case ChallengeMetric.TopThreeFinishes: return e.Finished && e.FinishPosition <= 3 ? 1 : 0;
                case ChallengeMetric.Drifts: return s.Drifts;
                case ChallengeMetric.PurpleDrifts: return s.PurpleDrifts;
                case ChallengeMetric.Boosts: return s.Boosts;
                case ChallengeMetric.Overtakes: return s.Overtakes;
                case ChallengeMetric.ItemsUsed: return s.ItemsUsed;
                case ChallengeMetric.ItemHitsLanded: return s.ItemHitsLanded;
                case ChallengeMetric.LastmaEscapes: return s.LastmaEscapes;
                case ChallengeMetric.BailGiven: return s.BailGiven;
                case ChallengeMetric.ShortcutsTaken: return s.ShortcutsTaken;
                case ChallengeMetric.WinsWithoutItems: return won && s.ItemsUsed == 0 ? 1 : 0;
                case ChallengeMetric.WinsFromLastOnFinalLap:
                {
                    int racers = 0;
                    foreach (var x in results.Entries) racers++;
                    return won && racers >= 2 && s.PositionAtFinalLapStart >= racers ? 1 : 0;
                }
                case ChallengeMetric.FriendsBeaten:
                {
                    if (friendsOf == null || !e.Finished) return 0;
                    var friends = friendsOf(e.PlayerId);
                    if (friends == null) return 0;
                    int n = 0;
                    foreach (var other in results.Entries)
                    {
                        if (other.IsBot || other.PlayerId == e.PlayerId || other.FinishPosition <= e.FinishPosition) continue;
                        foreach (var f in friends) if (f == other.PlayerId) { n++; break; }
                    }
                    return n;
                }
                case ChallengeMetric.DistinctTracksWon:
                {
                    if (!won) return 0;
                    var before = tracksWonBefore?.Invoke(e.PlayerId);
                    return before != null && before.Contains(results.TrackId) ? 0 : 1;
                }
                case ChallengeMetric.TournamentWins: return won && results.Mode == RaceMode.Tournament ? 1 : 0;
                default: return 0;
            }
        }
    }
}
