using System.Collections.Generic;
using NaijaKart.Core.AntiCheat;
using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Math;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;
using NaijaKart.Core.Vehicle;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class LoopbackTransportTests
    {
        [Test]
        public void DeliversInOrderAfterLatency()
        {
            var hub = new LoopbackTransportHub();
            hub.Conditions.LatencySeconds = 0.1f;
            var received = new List<int>();
            hub.Server.MessageReceived += (id, m) => received.Add(m.Laps);
            var client = hub.CreateClient("c1");
            client.Connect();
            for (int i = 0; i < 5; i++) client.Send(new ClientEnvelope { Kind = ClientMessageKind.Input, Laps = i });
            hub.Server.PumpIncoming();
            Assert.That(received, Is.Empty, "nothing before latency elapses");
            hub.Advance(0.11f);
            hub.Server.PumpIncoming();
            Assert.That(received, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        }

        [Test]
        public void PacketLossDropsSomeMessages()
        {
            var hub = new LoopbackTransportHub(seed: 11);
            hub.Conditions.PacketLoss = 0.5f;
            int received = 0;
            hub.Server.MessageReceived += (id, m) => received++;
            var client = hub.CreateClient("c1");
            client.Connect();
            for (int i = 0; i < 200; i++) client.Send(new ClientEnvelope { Kind = ClientMessageKind.Input });
            hub.Server.PumpIncoming();
            Assert.That(received, Is.GreaterThan(50).And.LessThan(150));
        }

        [Test]
        public void DisconnectReachesServerAndServerCanKick()
        {
            var hub = new LoopbackTransportHub();
            var events = new List<string>();
            hub.Server.ClientConnected += id => events.Add("conn:" + id);
            hub.Server.ClientDisconnected += id => events.Add("disc:" + id);
            var client = hub.CreateClient("c1");
            string clientReason = null;
            client.Disconnected += r => clientReason = r;
            client.Connect();
            hub.Server.PumpIncoming();
            client.Disconnect();
            hub.Server.PumpIncoming();
            Assert.That(events, Is.EqualTo(new[] { "conn:c1", "disc:c1" }));
            Assert.That(clientReason, Is.EqualTo("client"));

            var c2 = hub.CreateClient("c2");
            c2.Connect();
            hub.Server.PumpIncoming();
            hub.Server.Disconnect("c2", "kicked");
            c2.PumpIncoming();
            Assert.That(c2.IsConnected, Is.False);
            Assert.That(clientReason, Is.EqualTo("client"));
        }
    }

    public class ClientPredictorTests
    {
        [Test]
        public void PredictionMatchesServerExactlyWithoutLatency()
        {
            var content = TestContent.Create();
            var sim = new RaceSimulation(TestContent.Setup(items: false, lastma: false, road: false), content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.BeginCountdown();
            while (sim.State == RaceState.Countdown) sim.Step();
            var p = sim.Find("a");
            var predictor = new ClientPredictor(content.Game, sim.Track, p.Stats, p.State);
            var bot = new BotDriver(sim.Track, 5, 0.8f);
            for (int i = 0; i < 600; i++)
            {
                var think = bot.Think(p, sim.FixedDeltaTime);
                var input = predictor.Apply(new PlayerInputFrame { Steer = think.Steer, Drift = think.Drift });
                sim.SubmitInput("a", input);
                sim.Step();
                var snap = sim.BuildSnapshot();
                predictor.Reconcile(snap.Participants[0]);
                Assert.That(predictor.LastCorrectionMagnitude, Is.LessThan(1e-3f), "tick " + i);
                Assert.That(predictor.PendingInputs, Is.EqualTo(0));
            }
        }

        [Test]
        public void PredictionConvergesUnderLatency()
        {
            var content = TestContent.Create();
            var sim = new RaceSimulation(TestContent.Setup(items: false, lastma: false, road: false), content, null);
            sim.AddParticipant("a", "A", "danfo", null);
            sim.BeginCountdown();
            while (sim.State == RaceState.Countdown) sim.Step();
            var p = sim.Find("a");
            var predictor = new ClientPredictor(content.Game, sim.Track, p.Stats, p.State);
            var inFlight = new Queue<PlayerInputFrame>();
            var snaps = new Queue<ParticipantSnapshot>();
            const int delayTicks = 4;
            float maxCorrection = 0f;
            var bot = new BotDriver(sim.Track, 5, 0.8f);
            for (int i = 0; i < 600; i++)
            {
                // The client predicts from its own predicted state, as a real client would.
                var ghost = new RaceParticipant { PlayerId = "a", State = predictor.Predicted, Stats = p.Stats, Checkpoints = p.Checkpoints };
                var think = bot.Think(ghost, sim.FixedDeltaTime);
                inFlight.Enqueue(predictor.Apply(new PlayerInputFrame { Steer = think.Steer, Drift = think.Drift }));
                if (inFlight.Count > delayTicks) sim.SubmitInput("a", inFlight.Dequeue());
                sim.Step();
                snaps.Enqueue(sim.BuildSnapshot().Participants[0]);
                if (snaps.Count > delayTicks)
                {
                    predictor.Reconcile(snaps.Dequeue());
                    if (i > 20) maxCorrection = System.Math.Max(maxCorrection, predictor.LastCorrectionMagnitude);
                }
            }
            Assert.That(maxCorrection, Is.LessThan(0.5f), "corrections stay small under 4 ticks of latency");
            Assert.That(predictor.PendingInputs, Is.EqualTo(delayTicks * 2));
        }
    }

    public class AntiCheatTests
    {
        [Test]
        public void MovementValidatorFlagsTeleports()
        {
            var cfg = new AntiCheatConfig { maxSpeedTolerance = 1.1f, teleportSlackMeters = 1f };
            Assert.That(MovementValidator.IsPlausibleMove(Vec3.Zero, new Vec3(0, 0, 1.5f), 30f, 1f / 30f, cfg), Is.True);
            Assert.That(MovementValidator.IsPlausibleMove(Vec3.Zero, new Vec3(0, 0, 10f), 30f, 1f / 30f, cfg), Is.False);
            Assert.That(MovementValidator.IsPlausibleSpeed(45f, 30f, 1.6f, cfg), Is.True);
            Assert.That(MovementValidator.IsPlausibleSpeed(60f, 30f, 1.6f, cfg), Is.False);
        }

        [Test]
        public void RateLimiterDropsFloodsAndReplays()
        {
            var rl = new InputRateLimiter(10);
            for (int i = 1; i <= 10; i++) Assert.That(rl.Accept(i, 0.05f * i), Is.True);
            Assert.That(rl.Accept(11, 0.95f), Is.False, "11th in the same second");
            Assert.That(rl.Accept(11, 1.2f), Is.True);
            Assert.That(rl.Accept(11, 1.3f), Is.False, "replayed sequence");
            Assert.That(rl.Accept(5, 1.4f), Is.False, "old sequence");
        }
    }

    public class ConfigValidatorTests
    {
        [Test]
        public void DefaultTestContentIsValid()
        {
            Assert.That(ConfigValidator.Validate(TestContent.Create()), Is.Empty);
        }

        [Test]
        public void BrokenConfigIsReported()
        {
            var c = TestContent.Create();
            c.Game.drift.levelThresholds = new[] { 2f, 1f, 3f };
            c.Game.progression.ranks[3].minRating = 0;
            c.Items.items[1].id = c.Items.items[0].id;
            var errors = ConfigValidator.Validate(c);
            Assert.That(errors, Has.Some.Contains("drift.levelThresholds"));
            Assert.That(errors, Has.Some.Contains("progression.ranks"));
            Assert.That(errors, Has.Some.Contains("duplicate item id"));
        }
    }
}
