using System;
using System.Collections.Generic;
using System.Linq;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    /// <summary>Bots use the whole road: different lanes per racer, smooth lane changes, passing slower karts.</summary>
    public class BotDriverTests
    {
        private InMemoryConfigSource _content;

        [SetUp]
        public void SetUp() => _content = TestContent.Create();

        [Test]
        public void SmallNearbySeedsDoNotShareTheirFirstDraw()
        {
            var firsts = Enumerable.Range(1, 64).Select(i => new DeterministicRandom((ulong)i * 31).NextFloat()).ToArray();
            Assert.That(firsts.Max() - firsts.Min(), Is.GreaterThan(0.5f), "first draws must cover the range, not sit at zero");
            Assert.That(firsts.Average(), Is.InRange(0.3f, 0.7f));
            Assert.That(new DeterministicRandom(7).NextFloat(), Is.EqualTo(new DeterministicRandom(7).NextFloat()), "still deterministic");
        }

        [Test]
        public void EightBotsPreferDifferentLanes()
        {
            var geo = new TrackGeometry(_content.GetTrack("oval"));
            var lanes = Enumerable.Range(0, 8).Select(i => new BotDriver(geo, 1000UL + (ulong)i * 31UL, 0.6f, _content.Game.bots).LaneOffset).ToArray();
            float spread = _content.Game.bots.laneSpreadFraction * geo.Definition.roadHalfWidth;
            Assert.That(lanes.All(l => Math.Abs(l) <= spread + 0.001f), "lanes stay inside the configured spread");
            Assert.That(lanes.Max() - lanes.Min(), Is.GreaterThan(spread), "the grid uses both sides of the road: " + string.Join(", ", lanes.Select(l => l.ToString("0.0"))));
        }

        [Test]
        public void TheFieldSpreadsAcrossTheRoadDuringARace()
        {
            var geo = new TrackGeometry(_content.GetTrack("oval"));
            var samples = new List<float>();
            TestContent.RunBotRace(_content, TestContent.Setup(laps: 1, items: false, lastma: false, road: false), 8, perTick: sim =>
            {
                if (!sim.StateMachine.IsRacing || sim.RaceTime < 5f) return;
                var lat = sim.Participants.Where(p => p.IsActiveRacer).Select(p => geo.Project(p.State.Position).LateralOffset).ToArray();
                if (lat.Length < 4) return;
                float mean = lat.Average();
                samples.Add((float)Math.Sqrt(lat.Select(l => (l - mean) * (l - mean)).Average()));
            });
            Assert.That(samples.Count, Is.GreaterThan(100));
            Assert.That(samples.Average(), Is.GreaterThan(1.2f), "lateral spread (standard deviation in metres) across the field while racing");
        }

        [Test]
        public void ABotSteersAroundASlowerKartDirectlyAhead()
        {
            var sim = new RaceSimulation(TestContent.Setup(items: false, lastma: false, road: false), _content, null);
            sim.AddParticipant("a", "A", "danfo", "tunde", isBot: true);
            sim.AddParticipant("b", "B", "keke", "tunde", isBot: true);
            var geo = sim.Track;
            geo.Sample(60f, out Vec3 behind, out Vec3 dir);
            geo.Sample(68f, out Vec3 ahead, out _);
            var a = sim.Find("a"); var b = sim.Find("b");
            a.State.Position = behind; a.State.Heading = dir.ToYaw(); a.State.Speed = 20f;
            b.State.Position = ahead; b.State.Heading = dir.ToYaw(); b.State.Speed = 15f;
            var cfg = new BotConfig { laneSpreadFraction = 0f, laneChangeMinSeconds = 999f, laneChangeMaxSeconds = 1000f };
            var alone = new BotDriver(geo, 5, 0.6f, cfg).Think(a, null, null, 1f / 30f);
            var withTraffic = new BotDriver(geo, 5, 0.6f, cfg).Think(a, null, sim.Participants, 1f / 30f);
            Assert.That(Math.Abs(alone.Steer), Is.LessThan(0.05f), "on the centreline with no one ahead the bot drives straight");
            Assert.That(Math.Abs(withTraffic.Steer), Is.GreaterThan(0.1f), "with a slower kart ahead it pulls out to pass");

            b.State.Speed = 30f;
            var fasterAhead = new BotDriver(geo, 5, 0.6f, cfg).Think(a, null, sim.Participants, 1f / 30f);
            Assert.That(Math.Abs(fasterAhead.Steer), Is.LessThan(0.05f), "no swerve for a kart pulling away");
        }

        [Test]
        public void LaneChangesAreSmoothAndTimed()
        {
            var geo = new TrackGeometry(_content.GetTrack("oval"));
            var cfg = new BotConfig { laneSpreadFraction = 0.7f, laneChangeMinSeconds = 1f, laneChangeMaxSeconds = 1f, laneBlendMetresPerSecond = 2f };
            var sim = new RaceSimulation(TestContent.Setup(items: false, lastma: false, road: false), _content, null);
            sim.AddParticipant("a", "A", "danfo", "tunde", isBot: true);
            var a = sim.Find("a");
            var bot = new BotDriver(geo, 11, 0.6f, cfg);
            float dt = 1f / 30f, last = bot.LaneOffset;
            var distinct = new HashSet<int>();
            for (int i = 0; i < 30 * 12; i++)
            {
                bot.Think(a, null, null, dt);
                Assert.That(Math.Abs(bot.LaneOffset - last), Is.LessThanOrEqualTo(cfg.laneBlendMetresPerSecond * dt + 1e-4f), "never jumps sideways");
                last = bot.LaneOffset;
                distinct.Add((int)Math.Round(bot.LaneOffset * 2f));
            }
            Assert.That(distinct.Count, Is.GreaterThan(3), "the preferred lane moves over twelve seconds");
        }
    }
}
