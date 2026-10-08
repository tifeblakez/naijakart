using System;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class RaceStateMachineTests
    {
        [Test]
        public void HappyPathIsLegal()
        {
            var sm = new RaceStateMachine();
            foreach (var s in new[] { RaceState.Lobby, RaceState.Loading, RaceState.Countdown, RaceState.Racing, RaceState.FinalLap, RaceState.Finish, RaceState.Results, RaceState.Lobby })
            {
                Assert.That(sm.TryTransition(s), Is.True, "to " + s);
            }
        }

        [Test]
        public void IllegalTransitionsAreRejected()
        {
            var sm = new RaceStateMachine();
            Assert.That(sm.TryTransition(RaceState.Racing), Is.False);
            Assert.Throws<InvalidOperationException>(() => sm.Transition(RaceState.Results));
            Assert.That(sm.Current, Is.EqualTo(RaceState.Waiting));
        }

        [Test]
        public void ExceptionalStatesRecoverToWaiting()
        {
            var sm = new RaceStateMachine(RaceState.Racing);
            Assert.That(sm.TryTransition(RaceState.ServerError));
            Assert.That(sm.IsTerminal);
            Assert.That(sm.TryTransition(RaceState.Waiting));
        }
    }

    public class RaceSimulationTests
    {
        private InMemoryConfigSource _content;

        [SetUp]
        public void SetUp()
        {
            _content = TestContent.Create();
        }

        [Test]
        public void EightBotsCompleteARaceWithAuthoritativeOrder()
        {
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 2), 8);
            Assert.That(sim.State, Is.EqualTo(RaceState.Results));
            var r = sim.Results;
            Assert.That(r.Entries.Count, Is.EqualTo(8));
            var seen = new HashSet<int>();
            float lastTime = 0f;
            foreach (var e in r.Entries)
            {
                Assert.That(seen.Add(e.FinishPosition), "unique finish positions");
                if (e.Finished)
                {
                    Assert.That(e.TotalTime, Is.GreaterThanOrEqualTo(lastTime), "finishers ordered by time");
                    lastTime = e.TotalTime;
                    Assert.That(e.LapsCompleted, Is.EqualTo(2));
                    Assert.That(e.BestLap, Is.GreaterThan(10f));
                }
            }
            Assert.That(r.Entries[0].Finished, Is.True);
            Assert.That(r.Entries[0].FinishPosition, Is.EqualTo(1));
            Assert.That(r.RaceDuration, Is.GreaterThan(20f).And.LessThan(300f));
        }

        [Test]
        public void FinalLapStateIsEnteredAndRecordsPositions()
        {
            bool sawFinalLap = false;
            var events = new List<RaceEvent>();
            bool finalLapEvent = false;
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 2, items: false, lastma: false, road: false), 4, perTick: s =>
            {
                if (s.State == RaceState.FinalLap) sawFinalLap = true;
                events.Clear();
                s.DrainEvents(events);
                foreach (var e in events) if (e.Type == RaceEventType.FinalLapStarted) finalLapEvent = true;
            });
            Assert.That(sawFinalLap, Is.True);
            Assert.That(finalLapEvent, Is.True);
            foreach (var e in sim.Results.Entries) Assert.That(e.Stats.PositionAtFinalLapStart, Is.InRange(1, 4));
        }

        [Test]
        public void NoJoiningAfterCountdownStarts()
        {
            var sim = new RaceSimulation(TestContent.Setup(), _content, null);
            Assert.That(sim.AddParticipant("a", "A", "danfo", null), Is.True);
            Assert.That(sim.AddParticipant("a", "A", "danfo", null), Is.False, "duplicate id");
            sim.BeginCountdown();
            Assert.That(sim.State, Is.EqualTo(RaceState.Countdown));
            Assert.That(sim.AddParticipant("b", "B", "danfo", null), Is.False);
        }

        [Test]
        public void MaxPlayersIsEnforced()
        {
            var sim = new RaceSimulation(TestContent.Setup(), _content, null);
            for (int i = 0; i < 8; i++) Assert.That(sim.AddParticipant("p" + i, null, "danfo", null), Is.True);
            Assert.That(sim.AddParticipant("p8", null, "danfo", null), Is.False);
        }

        [Test]
        public void CountdownEmitsTicksThenStartsRacing()
        {
            var sim = new RaceSimulation(TestContent.Setup(), _content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.BeginCountdown();
            var events = new List<RaceEvent>();
            int ticks = 0;
            while (sim.State == RaceState.Countdown && ticks++ < 1000) sim.Step();
            sim.DrainEvents(events);
            int countdownTicks = 0; bool started = false;
            foreach (var e in events) { if (e.Type == RaceEventType.CountdownTick) countdownTicks++; if (e.Type == RaceEventType.RaceStarted) started = true; }
            Assert.That(countdownTicks, Is.EqualTo(_content.Game.raceRules.countdownSeconds));
            Assert.That(started);
            Assert.That(sim.State, Is.EqualTo(RaceState.Racing));
            Assert.That(sim.RaceTime, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void InputsAreRateLimitedAndOrdered()
        {
            var sim = new RaceSimulation(TestContent.Setup(), _content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.BeginCountdown();
            while (sim.State == RaceState.Countdown) sim.Step();
            sim.SubmitInput("a", new PlayerInputFrame { Sequence = 5, Steer = 0.5f });
            Assert.That(sim.Find("a").LatestInput.Steer, Is.EqualTo(0.5f));
            sim.SubmitInput("a", new PlayerInputFrame { Sequence = 3, Steer = -1f });
            Assert.That(sim.Find("a").LatestInput.Steer, Is.EqualTo(0.5f), "out-of-order frame ignored");
            for (int i = 0; i < 500; i++) sim.SubmitInput("a", new PlayerInputFrame { Sequence = 10 + i, Steer = 0.1f });
            Assert.That(sim.Find("a").LastInputSequence, Is.LessThan(10 + _content.Game.antiCheat.maxInputFramesPerSecond + 1), "flood dropped");
        }

        [Test]
        public void DisconnectedPlayerBecomesDnfAfterWindowAndResultsStayValid()
        {
            _content.Game.raceRules.reconnectWindowSeconds = 2f;
            var events = new List<RaceEvent>();
            bool dnf = false;
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false), 3, perTick: s =>
            {
                if (s.State == RaceState.Racing && s.RaceTime > 3f && s.RaceTime < 3.1f) s.MarkDisconnected("p2");
                events.Clear();
                s.DrainEvents(events);
                foreach (var e in events) if (e.Type == RaceEventType.PlayerDnf && e.PlayerId == "p2") dnf = true;
            });
            Assert.That(dnf, Is.True);
            var e2 = sim.Results.For("p2");
            Assert.That(e2.Status, Is.EqualTo(ParticipantStatus.DidNotFinish));
            Assert.That(e2.Finished, Is.False);
            Assert.That(e2.FinishPosition, Is.EqualTo(3), "DNF is ordered last");
            Assert.That(sim.Results.Entries[0].Finished, Is.True);
        }

        [Test]
        public void ReconnectWithinWindowKeepsRacing()
        {
            _content.Game.raceRules.reconnectWindowSeconds = 10f;
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false), 2, perTick: s =>
            {
                if (s.State == RaceState.Racing && s.RaceTime > 2f && s.RaceTime < 2.05f) s.MarkDisconnected("p1");
                if (s.State == RaceState.Racing && s.RaceTime > 4f && s.RaceTime < 4.05f) s.MarkReconnected("p1");
            });
            Assert.That(sim.Results.For("p1").Finished, Is.True);
        }

        [Test]
        public void RaceTimesOutWhenNobodyFinishes()
        {
            _content.Game.raceRules.maxRaceDurationSeconds = 5f;
            var sim = new RaceSimulation(TestContent.Setup(laps: 3), _content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.BeginCountdown();
            for (int i = 0; i < 30 * 12 && sim.State != RaceState.Results; i++) sim.Step();
            Assert.That(sim.State, Is.EqualTo(RaceState.Results));
            Assert.That(sim.Results.TimedOut, Is.True);
            Assert.That(sim.Results.For("a").Status, Is.EqualTo(ParticipantStatus.DidNotFinish));
        }

        [Test]
        public void FinishGraceForcesStragglersToDnf()
        {
            _content.Game.raceRules.finishGraceSeconds = 1f;
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false), 2, perTick: s =>
            {
                // Immobilise p1 so it can never finish.
                var p = s.Find("p1");
                if (s.State == RaceState.Racing) p.State.IsImmobilised = true;
            });
            Assert.That(sim.Results.For("p0").Finished, Is.True);
            Assert.That(sim.Results.For("p1").Status, Is.EqualTo(ParticipantStatus.DidNotFinish));
            Assert.That(sim.Results.RaceDuration, Is.LessThan(sim.Results.For("p0").TotalTime + 1.2f));
        }

        [Test]
        public void RematchRequiresEveryConnectedHumanVote()
        {
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false), 2);
            // Bots are flagged; mark them human for this check by voting as the API would.
            foreach (var p in sim.Participants) p.IsBot = false;
            sim.VoteRematch("p0");
            Assert.That(sim.RematchRequested, Is.False);
            sim.VoteRematch("p1");
            Assert.That(sim.RematchRequested, Is.True);
        }

        [Test]
        public void SnapshotReflectsState()
        {
            var sim = new RaceSimulation(TestContent.Setup(), _content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.AddParticipant("b", "B", "keke", null);
            sim.BeginCountdown();
            while (sim.State == RaceState.Countdown) sim.Step();
            TestContent.Drive(sim, "a", 60);
            var snap = sim.BuildSnapshot();
            Assert.That(snap.State, Is.EqualTo(RaceState.Racing));
            Assert.That(snap.Participants.Length, Is.EqualTo(2));
            Assert.That(snap.Participants[0].PlayerId, Is.EqualTo("a"));
            Assert.That(snap.Participants[0].Speed, Is.GreaterThan(5f));
            Assert.That(snap.Participants[0].RacePosition, Is.EqualTo(1));
            Assert.That(snap.Participants[1].RacePosition, Is.EqualTo(2));
            Assert.That(snap.ItemBoxes.Length, Is.EqualTo(_content.GetTrack("oval").itemBoxes.Length));
        }

        [Test]
        public void SameSeedAndInputsProduceIdenticalResults()
        {
            var a = TestContent.RunBotRace(TestContent.Create(), TestContent.Setup(seed: 9), 6);
            var b = TestContent.RunBotRace(TestContent.Create(), TestContent.Setup(seed: 9), 6);
            for (int i = 0; i < a.Results.Entries.Count; i++)
            {
                Assert.That(b.Results.Entries[i].PlayerId, Is.EqualTo(a.Results.Entries[i].PlayerId));
                Assert.That(b.Results.Entries[i].TotalTime, Is.EqualTo(a.Results.Entries[i].TotalTime));
            }
        }

        [Test]
        public void ClientCannotDeclareItselfFinished()
        {
            // The public API exposes no way to set laps, positions or finish state: only inputs/intents.
            var api = typeof(RaceSimulation).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var m in api)
            {
                Assert.That(m.Name, Does.Not.Contain("SetPosition").And.Not.Contain("SetLap").And.Not.Contain("Finish").IgnoreCase.Or.EqualTo("get_Results"),
                    "suspicious public API: " + m.Name);
            }
            // And a participant removed mid-race is a DNF, never a finisher.
            var sim = TestContent.RunBotRace(_content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false), 2, perTick: s =>
            {
                if (s.State == RaceState.Racing && s.RaceTime > 1f && s.RaceTime < 1.05f) s.RemoveParticipant("p1");
            });
            Assert.That(sim.Results.For("p1").Finished, Is.False);
        }

        [Test]
        public void CancelProducesCancelledResults()
        {
            var sim = new RaceSimulation(TestContent.Setup(), _content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.BeginCountdown();
            sim.Cancel("test");
            Assert.That(sim.State, Is.EqualTo(RaceState.RaceCancelled));
            Assert.That(sim.Results.Cancelled, Is.True);
        }
    }
}
