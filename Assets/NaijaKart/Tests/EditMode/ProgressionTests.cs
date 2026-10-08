using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Progression;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Social;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class ProgressionTests
    {
        private readonly ProgressionConfig _cfg = new ProgressionConfig();

        [Test]
        public void XpCurveIsMonotonicAndRoundTrips()
        {
            long prev = 0;
            for (int level = 1; level < _cfg.maxLevel; level++)
            {
                long need = XpCurve.XpToNextLevel(level, _cfg);
                Assert.That(need, Is.GreaterThan(prev));
                prev = need;
                long total = XpCurve.TotalXpForLevel(level, _cfg);
                Assert.That(XpCurve.LevelForXp(total, _cfg), Is.EqualTo(level));
                Assert.That(XpCurve.LevelForXp(total - 1, _cfg), Is.EqualTo(level - 1 < 1 ? 1 : level - 1));
            }
            Assert.That(XpCurve.LevelForXp(long.MaxValue / 4, _cfg), Is.EqualTo(_cfg.maxLevel));
            Assert.That(XpCurve.LevelProgress(0, _cfg), Is.EqualTo(0f));
        }

        [Test]
        public void RankLadderComesFromConfig()
        {
            var ladder = new RankLadder(_cfg);
            Assert.That(ladder.TierFor(0).id, Is.EqualTo("newbie"));
            Assert.That(ladder.TierFor(1000).id, Is.EqualTo("newbie"));
            Assert.That(ladder.TierFor(1620).id, Is.EqualTo("chairman"));
            Assert.That(ladder.TierFor(5000).id, Is.EqualTo("untouchable"));
            var custom = new ProgressionConfig { ranks = new[] { new RankTierConfig("a", "A", 0), new RankTierConfig("b", "B", 10) } };
            Assert.That(new RankLadder(custom).TierFor(10).id, Is.EqualTo("b"));
        }

        [Test]
        public void RatingRewardsWinnersAndIsRoughlyZeroSum()
        {
            var entries = new List<RatingCalculator.Entry>();
            for (int i = 0; i < 8; i++) entries.Add(new RatingCalculator.Entry { PlayerId = "p" + i, Rating = 1000, FinishPosition = i + 1 });
            var deltas = RatingCalculator.ComputeDeltas(entries, _cfg);
            Assert.That(deltas["p0"], Is.GreaterThan(0));
            Assert.That(deltas["p7"], Is.LessThan(0));
            Assert.That(deltas["p0"], Is.EqualTo(-deltas["p7"]));
            int sum = 0;
            foreach (var d in deltas.Values) sum += d;
            Assert.That(System.Math.Abs(sum), Is.LessThanOrEqualTo(8));
            // Beating a much stronger field pays more.
            entries[0] = new RatingCalculator.Entry { PlayerId = "p0", Rating = 700, FinishPosition = 1 };
            Assert.That(RatingCalculator.ComputeDeltas(entries, _cfg)["p0"], Is.GreaterThan(deltas["p0"]));
        }

        [Test]
        public void RewardsComeFromTables()
        {
            var cfg = new GameConfig();
            var stats = new ParticipantStats { LastmaEscapes = 2, Overtakes = 5 };
            var r = RewardCalculator.Compute(1, true, 3, stats, cfg);
            Assert.That(r.Xp, Is.EqualTo(cfg.progression.xpByPosition[0] + 3 * cfg.progression.xpPerLapCompleted + 2 * cfg.progression.xpLastmaEscape + 5 * cfg.progression.xpPerOvertake));
            Assert.That(r.Coins, Is.EqualTo(cfg.economy.coinsByPosition[0] + 3 * cfg.economy.coinsPerLapCompleted + 2 * cfg.economy.coinsLastmaEscapeBonus));
            var dnf = RewardCalculator.Compute(0, false, 1, null, cfg);
            Assert.That(dnf.Xp, Is.EqualTo(cfg.progression.xpDnf + cfg.progression.xpPerLapCompleted));
            Assert.That(RewardCalculator.Compute(20, true, 0, null, cfg).Coins, Is.EqualTo(cfg.economy.coinsByPosition[7]), "beyond table uses last value");
        }

        [Test]
        public void SettlementIsIdempotentAndUpdatesEverything()
        {
            var content = TestContent.Create();
            var sim = TestContent.RunBotRace(content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false, mode: RaceMode.Ranked), 3);
            foreach (var e in sim.Results.Entries) e.IsBot = false;
            var ledger = new CoinLedger(new InMemoryCoinStore(), content.Game.economy);
            var profiles = new InMemoryProfileStore(content.Game.progression);
            var rivalries = new InMemoryRivalryStore();
            var settlement = new RaceSettlementService(content.Game, ledger, profiles, rivalries);

            var rewards = settlement.Settle(sim.Results);
            Assert.That(rewards.Count, Is.EqualTo(3));
            string winner = sim.Results.Entries[0].PlayerId;
            var w = profiles.Get(winner);
            Assert.That(w.Wins, Is.EqualTo(1));
            Assert.That(w.Races, Is.EqualTo(1));
            Assert.That(w.CurrentWinStreak, Is.EqualTo(1));
            Assert.That(w.TotalXp, Is.GreaterThan(0));
            Assert.That(w.Rating, Is.GreaterThan(content.Game.progression.ratingStart));
            Assert.That(ledger.GetBalance(winner), Is.EqualTo(content.Game.economy.startingBalance + rewards[0].Coins));
            Assert.That(rewards[0].RankIdBefore, Is.EqualTo("newbie"));
            var loser = sim.Results.Entries[2].PlayerId;
            Assert.That(profiles.Get(loser).Rating, Is.LessThan(content.Game.progression.ratingStart));

            var again = settlement.Settle(sim.Results);
            Assert.That(again.Count, Is.EqualTo(0), "second settlement of the same race is a no-op");
            Assert.That(ledger.GetBalance(winner), Is.EqualTo(content.Game.economy.startingBalance + rewards[0].Coins));
            Assert.That(profiles.Get(winner).Races, Is.EqualTo(1));

            var riv = rivalries.GetOrCreate(winner, loser);
            Assert.That(riv.TotalRaces, Is.EqualTo(1));
            Assert.That(riv.WinsFor(winner), Is.EqualTo(1));
            Assert.That(riv.LastWinner, Is.EqualTo(winner));
        }

        [Test]
        public void QuickRaceDoesNotMoveRating()
        {
            var content = TestContent.Create();
            var sim = TestContent.RunBotRace(content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false, mode: RaceMode.QuickRace), 2);
            foreach (var e in sim.Results.Entries) e.IsBot = false;
            var profiles = new InMemoryProfileStore(content.Game.progression);
            var settlement = new RaceSettlementService(content.Game, new CoinLedger(new InMemoryCoinStore(), content.Game.economy), profiles, new InMemoryRivalryStore());
            var rewards = settlement.Settle(sim.Results);
            Assert.That(rewards[0].RatingDelta, Is.EqualTo(0));
            Assert.That(rewards[0].Coins, Is.GreaterThan(0));
        }
    }

    public class RivalryTests
    {
        [Test]
        public void RecordsWinsStreaksAndFastestLaps()
        {
            var r = Rivalry.Create("tife", "chuks");
            Assert.That(r.PlayerA, Is.EqualTo("chuks"), "canonical ordering");
            r.Record("tife", 80f, 72f);
            r.Record("tife", 79f, 75f);
            r.Record("chuks", 70f, 74f);
            Assert.That(r.TotalRaces, Is.EqualTo(3));
            Assert.That(r.WinsFor("tife"), Is.EqualTo(2));
            Assert.That(r.WinsFor("chuks"), Is.EqualTo(1));
            Assert.That(r.StreakFor("chuks"), Is.EqualTo(1));
            Assert.That(r.StreakFor("tife"), Is.EqualTo(0));
            Assert.That(r.FastestLapA, Is.EqualTo(70f));
            Assert.That(r.FastestLapB, Is.EqualTo(72f));
            Assert.That(Rivalry.KeyFor("tife", "chuks"), Is.EqualTo(Rivalry.KeyFor("chuks", "tife")));
        }

        [Test]
        public void HighlightsAreBuiltFromTelemetry()
        {
            var stats = new ParticipantStats { LastmaEscapes = 2, Overtakes = 18, WorstPosition = 7, PurpleDrifts = 1, ItemsUsed = 0 };
            var h = RaceHighlights.Build(stats, 1, 5);
            var ids = new List<string>();
            foreach (var x in h) ids.Add(x.Id);
            Assert.That(ids, Does.Contain("lastma_escape").And.Contain("overtakes").And.Contain("comeback").And.Contain("final_lap_comeback").And.Contain("perfect_drift").And.Contain("no_dull"));
        }
    }
}
