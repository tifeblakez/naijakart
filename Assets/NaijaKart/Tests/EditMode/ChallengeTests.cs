using System;
using System.Collections.Generic;
using NaijaKart.Core.Challenges;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class ChallengeEvaluatorTests
    {
        private static ChallengeLibrary Library() => new ChallengeLibrary
        {
            challenges = new[]
            {
                new ChallengeDefinition { id = "d_races", cadence = ChallengeCadence.Daily, metric = ChallengeMetric.RacesCompleted, target = 2, rewardCoins = 100, rewardXp = 10 },
                new ChallengeDefinition { id = "w_wins", cadence = ChallengeCadence.Weekly, metric = ChallengeMetric.Wins, target = 1, rewardCoins = 500 },
                new ChallengeDefinition { id = "a_nodull", cadence = ChallengeCadence.Achievement, metric = ChallengeMetric.WinsWithoutItems, target = 1, rewardTitle = "No Dull" },
                new ChallengeDefinition { id = "w_friends", cadence = ChallengeCadence.Weekly, metric = ChallengeMetric.FriendsBeaten, target = 2 },
                new ChallengeDefinition { id = "a_omo", cadence = ChallengeCadence.Achievement, metric = ChallengeMetric.WinsFromLastOnFinalLap, target = 1 },
                new ChallengeDefinition { id = "off", cadence = ChallengeCadence.Daily, metric = ChallengeMetric.RacesCompleted, target = 1, disabled = true },
            }
        };

        private static RaceResults Results(string winner, string loser, int winnerItems = 0, int winnerFinalLapPos = 1)
        {
            var r = new RaceResults { RaceId = Guid.NewGuid().ToString("N"), TrackId = "oval", Mode = RaceMode.QuickRace };
            r.Entries.Add(new RaceResultEntry { PlayerId = winner, FinishPosition = 1, Finished = true, Stats = new ParticipantStats { ItemsUsed = winnerItems, PositionAtFinalLapStart = winnerFinalLapPos } });
            r.Entries.Add(new RaceResultEntry { PlayerId = loser, FinishPosition = 2, Finished = true, Stats = new ParticipantStats() });
            return r;
        }

        [Test]
        public void AccumulatesCompletesAndPaysOnce()
        {
            var ledger = new CoinLedger(new InMemoryCoinStore(), new EconomyConfig { startingBalance = 0 });
            var store = new InMemoryChallengeProgressStore();
            DateTime now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
            var ev = new ChallengeEvaluator(Library(), store, ledger, () => now);

            var c1 = ev.Apply(Results("a", "b"));
            var ids = new List<string>(); foreach (var c in c1) ids.Add(c.Challenge.id + ":" + c.PlayerId);
            Assert.That(ids, Does.Contain("w_wins:a").And.Contain("a_nodull:a"));
            Assert.That(ids, Does.Not.Contain("d_races:a"), "needs 2 races");
            Assert.That(ids, Does.Not.Contain("off:a"), "disabled rules never fire");
            Assert.That(ledger.GetBalance("a"), Is.EqualTo(500));

            var c2 = ev.Apply(Results("b", "a", winnerItems: 3));
            ids.Clear(); foreach (var c in c2) ids.Add(c.Challenge.id + ":" + c.PlayerId);
            Assert.That(ids, Does.Contain("d_races:a").And.Contain("d_races:b").And.Contain("w_wins:b"));
            Assert.That(ids, Does.Not.Contain("a_nodull:b"), "used items");
            Assert.That(ledger.GetBalance("a"), Is.EqualTo(600));

            // Already complete: no double pay.
            ev.Apply(Results("a", "b"));
            Assert.That(ledger.GetBalance("a"), Is.EqualTo(600));
        }

        [Test]
        public void DailyResetsNextDayWeeklyDoesNot()
        {
            var ledger = new CoinLedger(new InMemoryCoinStore(), new EconomyConfig { startingBalance = 0 });
            DateTime now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc); // Wednesday
            var ev = new ChallengeEvaluator(Library(), new InMemoryChallengeProgressStore(), ledger, () => now);
            ev.Apply(Results("a", "b"));
            Assert.That(Find(ev, "a", "d_races").value, Is.EqualTo(1));
            now = now.AddDays(1);
            Assert.That(Find(ev, "a", "d_races").value, Is.EqualTo(0), "new day, new progress");
            Assert.That(Find(ev, "a", "w_wins").completed, Is.True, "same ISO week");
            now = now.AddDays(7);
            Assert.That(Find(ev, "a", "w_wins").completed, Is.False, "next week");
            Assert.That(Find(ev, "a", "a_nodull").completed, Is.True, "achievements are permanent");
        }

        [Test]
        public void FriendsBeatenAndFinalLapComeback()
        {
            var ev = new ChallengeEvaluator(Library(), new InMemoryChallengeProgressStore(), null, () => DateTime.UtcNow);
            var friends = new Dictionary<string, IReadOnlyCollection<string>> { { "a", new[] { "b" } } };
            var r = Results("a", "b", winnerFinalLapPos: 2);
            var c = ev.Apply(r, id => friends.TryGetValue(id, out var f) ? f : null);
            Assert.That(Find(ev, "a", "w_friends").value, Is.EqualTo(1));
            Assert.That(Find(ev, "b", "w_friends").value, Is.EqualTo(0), "b has no friends list");
            bool omo = false; foreach (var x in c) if (x.Challenge.id == "a_omo" && x.PlayerId == "a") omo = true;
            Assert.That(omo, Is.True, "won from last on final lap in a 2-player race");
        }

        [Test]
        public void PeriodKeysAreStable()
        {
            var monday = new DateTime(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc);
            Assert.That(ChallengeEvaluator.PeriodKey(ChallengeCadence.Daily, monday), Is.EqualTo("2026-01-05"));
            Assert.That(ChallengeEvaluator.PeriodKey(ChallengeCadence.Weekly, monday), Is.EqualTo("2026-W02"));
            Assert.That(ChallengeEvaluator.PeriodKey(ChallengeCadence.Weekly, monday.AddDays(6)), Is.EqualTo("2026-W02"));
            Assert.That(ChallengeEvaluator.PeriodKey(ChallengeCadence.Weekly, monday.AddDays(7)), Is.EqualTo("2026-W03"));
            Assert.That(ChallengeEvaluator.PeriodKey(ChallengeCadence.Achievement, monday), Is.EqualTo(""));
        }

        private static ChallengeProgress Find(ChallengeEvaluator ev, string player, string id)
        {
            foreach (var p in ev.CurrentProgress(player)) if (p.challengeId == id) return p;
            return null;
        }
    }

    public class LiveEventConfigTests
    {
        [Test]
        public void DefaultCalendarCoversTheWeekAndValidates()
        {
            var cfg = new LiveEventsConfig();
            for (int d = 0; d < 7; d++) Assert.That(cfg.RuleFor(d), Is.Not.Null, "weekday " + d);
            Assert.That(cfg.RuleFor(1).itemsEnabled, Is.False, "Monday is No Item Race");
            Assert.That(cfg.RuleFor(0).lastmaEnabled, Is.False, "Sunday is Chill Race");
            cfg.enabled = false;
            Assert.That(cfg.RuleFor(1), Is.Null);
            var content = TestContent.Create();
            content.Game.liveEvents.weekdayRules[0].weekday = 9;
            Assert.That(ConfigValidator.Validate(content), Has.Some.Contains("weekday out of range"));
        }

        [Test]
        public void LiveEventMultipliersReachTheSimulation()
        {
            var content = TestContent.Create();
            content.Game.lastma.firstTriggerMinSeconds = 10f;
            var slowSetup = TestContent.Setup(laps: 1, items: false, road: false, lastma: true);
            var fastSetup = TestContent.Setup(laps: 1, items: false, road: false, lastma: true);
            fastSetup.LastmaIntervalMultiplier = 0.1f;
            int CountWarnings(RaceSetup setup)
            {
                int n = 0; var events = new List<RaceEvent>();
                TestContent.RunBotRace(content, setup, 4, perTick: s => { events.Clear(); s.DrainEvents(events); foreach (var e in events) if (e.Type == RaceEventType.LastmaWarning) n++; });
                return n;
            }
            Assert.That(CountWarnings(fastSetup), Is.GreaterThan(CountWarnings(slowSetup)));
        }
    }
}
