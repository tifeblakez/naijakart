using NaijaKart.Core.AntiCheat;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Track;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class TrackGeometryTests
    {
        [Test]
        public void ProjectionAndSamplingAgree()
        {
            var geo = new TrackGeometry(TrackFactory.Oval("o", 100f, 30f, 6f));
            Assert.That(geo.LapLength, Is.GreaterThan(300f));
            for (float d = 0f; d < geo.LapLength; d += 37f)
            {
                geo.Sample(d, out Vec3 p, out _);
                var proj = geo.Project(p);
                Assert.That(proj.DistanceAlong, Is.EqualTo(d).Within(0.5f));
                Assert.That(proj.LateralOffset, Is.EqualTo(0f).Within(0.05f));
            }
        }

        [Test]
        public void SurfaceSampleDetectsRoadEdge()
        {
            var geo = new TrackGeometry(TrackFactory.Oval("o", 100f, 30f, 6f));
            geo.Sample(20f, out Vec3 p, out Vec3 dir);
            Vec3 right = Vec3.Cross(Vec3.Up, dir);
            Assert.That(geo.SampleSurface(p + right * 3f, 1f, 1f).OnRoad, Is.True);
            var off = geo.SampleSurface(p + right * 10f, 1f, 1f);
            Assert.That(off.OnRoad, Is.False);
            Assert.That(off.DistanceBeyondRoadEdge, Is.EqualTo(4f).Within(0.05f));
        }

        [Test]
        public void GridSlotsAreBehindStartLineFacingForward()
        {
            var geo = new TrackGeometry(TrackFactory.Oval("o", 100f, 30f, 6f));
            geo.GridSlot(0, 4f, 2.5f, out Vec3 p0, out float h0);
            geo.GridSlot(7, 4f, 2.5f, out Vec3 p7, out float h7);
            Assert.That(geo.Project(p0).DistanceAlong, Is.GreaterThan(geo.Project(p7).DistanceAlong), "slot 0 is nearest the line");
            Assert.That(h0, Is.EqualTo(0f).Within(0.2f));
            Assert.That(h7, Is.EqualTo(0f).Within(0.2f));
            Assert.That(Vec3.FlatDistance(p0, p7), Is.GreaterThan(10f));
        }
    }

    public class CheckpointTrackerTests
    {
        private TrackDefinition _track;
        private TrackGeometry _geo;

        [SetUp]
        public void SetUp()
        {
            _track = TrackFactory.Oval("o", 100f, 30f, 6f, checkpointCount: 4);
            _geo = new TrackGeometry(_track);
        }

        private CheckpointTracker Tracker(int laps = 2, float minLap = 0f) =>
            new CheckpointTracker(_track, _geo, laps, minLap, _track.checkpoints[0].gates[0].position, 0f);

        private static Vec3 Gate(TrackDefinition t, int i, int g = 0) => t.checkpoints[i].gates[g].position;

        [Test]
        public void GatesMustBeCrossedInOrder()
        {
            var tr = Tracker();
            Assert.That(tr.Update(Gate(_track, 2), 1f, out _), Is.EqualTo(CheckpointResult.None), "skipping gate 1 is not credited");
            Assert.That(tr.NextCheckpoint, Is.EqualTo(1));
            Assert.That(tr.Update(Gate(_track, 1), 2f, out _), Is.EqualTo(CheckpointResult.GatePassed));
            Assert.That(tr.Update(Gate(_track, 0), 3f, out _), Is.EqualTo(CheckpointResult.None), "finish line before all gates is ignored");
            Assert.That(tr.Update(Gate(_track, 2), 4f, out _), Is.EqualTo(CheckpointResult.GatePassed));
            Assert.That(tr.Update(Gate(_track, 3), 5f, out _), Is.EqualTo(CheckpointResult.GatePassed));
            Assert.That(tr.Update(Gate(_track, 0), 6f, out _), Is.EqualTo(CheckpointResult.LapCompleted));
            Assert.That(tr.LapsCompleted, Is.EqualTo(1));
            Assert.That(tr.IsOnFinalLap, Is.True);
        }

        [Test]
        public void FinishesAfterConfiguredLaps()
        {
            var tr = Tracker(laps: 1);
            float t = 1f;
            for (int i = 1; i < 4; i++) tr.Update(Gate(_track, i), t++, out _);
            Assert.That(tr.Update(Gate(_track, 0), t, out _), Is.EqualTo(CheckpointResult.RaceFinished));
            Assert.That(tr.Finished);
            Assert.That(tr.Update(Gate(_track, 1), t + 1, out _), Is.EqualTo(CheckpointResult.None));
        }

        [Test]
        public void ImpossiblyFastLapIsRejected()
        {
            var tr = Tracker(laps: 2, minLap: 20f);
            tr.StartLapTimer(0f);
            for (int i = 1; i < 4; i++) tr.Update(Gate(_track, i), i, out _);
            Assert.That(tr.Update(Gate(_track, 0), 5f, out _), Is.EqualTo(CheckpointResult.LapRejected));
            Assert.That(tr.LapsCompleted, Is.EqualTo(0));
            Assert.That(tr.NextCheckpoint, Is.EqualTo(1), "must redo the lap");
        }

        [Test]
        public void ShortcutGateCountsAndIsFlagged()
        {
            TrackFactory.AddShortcutGate(_track, 2, new Vec3(0, 0, 50), 5f);
            var tr = Tracker();
            tr.Update(Gate(_track, 1), 1f, out _);
            Assert.That(tr.Update(new Vec3(0, 0, 50), 2f, out bool shortcut), Is.EqualTo(CheckpointResult.GatePassed));
            Assert.That(shortcut, Is.True);
            Assert.That(tr.ShortcutsTaken, Is.EqualTo(1));
            Assert.That(tr.NextCheckpoint, Is.EqualTo(3));
        }

        [Test]
        public void ProgressIsMonotonicAlongTheLap()
        {
            var tr = Tracker();
            float last = -1f;
            for (float d = 0f; d < _geo.LapLength * 0.98f; d += 5f)
            {
                _geo.Sample(d, out Vec3 p, out _);
                tr.Update(p, d, out _);
                float progress = tr.Progress(p);
                Assert.That(progress, Is.GreaterThanOrEqualTo(last - 1e-4f), $"progress fell at d={d}");
                last = progress;
            }
        }

        [Test]
        public void LapValidatorUsesReferenceTime()
        {
            var cfg = new AntiCheatConfig { absoluteMinLapSeconds = 20f, minLapFractionOfReference = 0.5f };
            Assert.That(LapValidator.MinimumLapSeconds(new TrackDefinition { referenceLapSeconds = 70f }, cfg), Is.EqualTo(35f));
            Assert.That(LapValidator.MinimumLapSeconds(new TrackDefinition { referenceLapSeconds = 30f }, cfg), Is.EqualTo(20f));
        }
    }
}
