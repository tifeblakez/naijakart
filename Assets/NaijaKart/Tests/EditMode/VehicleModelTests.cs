using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Math;
using NaijaKart.Core.Vehicle;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class ArcadeVehicleModelTests
    {
        private GameConfig _cfg;
        private ArcadeVehicleModel _model;
        private VehicleStats _stats;
        private const float Dt = 1f / 30f;

        [SetUp]
        public void SetUp()
        {
            _cfg = new GameConfig();
            _model = new ArcadeVehicleModel(_cfg);
            _stats = VehicleStats.From(TestContent.Create().Vehicles.vehicles[0], null, _cfg);
        }

        private VehicleStepEvents Run(ref VehicleState s, PlayerInputFrame input, int ticks, SurfaceSample? surface = null)
        {
            VehicleStepEvents all = VehicleStepEvents.None;
            for (int i = 0; i < ticks; i++) all |= _model.Step(ref s, input, _stats, surface ?? SurfaceSample.Road, Dt);
            return all;
        }

        [Test]
        public void AutoAcceleratesToTopSpeedAndMovesForward()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 30 * 6);
            Assert.That(s.Speed, Is.EqualTo(_stats.TopSpeed).Within(0.01f));
            Assert.That(s.Position.Z, Is.GreaterThan(50f));
            Assert.That(s.Position.X, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void SteeringTurnsHeading()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 60);
            Run(ref s, new PlayerInputFrame { Steer = 1f }, 30);
            Assert.That(s.Heading, Is.GreaterThan(0.3f));
            Assert.That(s.Position.X, Is.GreaterThan(0f), "turning right moves toward +X");
        }

        [Test]
        public void SteeringAuthorityIsLowAtStandstill()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            _model.Step(ref s, new PlayerInputFrame { Steer = 1f }, _stats, SurfaceSample.Road, Dt);
            Assert.That(s.Heading, Is.LessThan(0.01f));
        }

        [Test]
        public void DriftRequiresSpeedAndSteerAndChargesThroughLevels()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            var ev = _model.Step(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, _stats, SurfaceSample.Road, Dt);
            Assert.That(s.IsDrifting, Is.False, "too slow to drift");
            Run(ref s, PlayerInputFrame.Neutral, 90);
            ev = Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, 1);
            Assert.That(s.IsDrifting, Is.True);
            Assert.That(ev.HasFlag(VehicleStepEvents.DriftStarted));
            Assert.That(s.DriftDirection, Is.EqualTo(1));

            Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, (int)(_cfg.drift.levelThresholds[0] / Dt) + 2);
            Assert.That(s.DriftLevel, Is.EqualTo(DriftLevel.Blue));
            Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, (int)(_cfg.drift.levelThresholds[2] / Dt) + 2);
            Assert.That(s.DriftLevel, Is.EqualTo(DriftLevel.Purple));
        }

        [Test]
        public void ReleasingChargedDriftBoosts()
        {
            _cfg.drift.releaseMode = DriftReleaseMode.Immediate;
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 90);
            Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, (int)(_cfg.drift.levelThresholds[1] / Dt) + 5);
            Assert.That(s.DriftLevel, Is.EqualTo(DriftLevel.Orange));
            var ev = Run(ref s, new PlayerInputFrame { Steer = 0f, Drift = false }, 1);
            Assert.That(ev.HasFlag(VehicleStepEvents.DriftReleasedWithBoost));
            Assert.That(ev.HasFlag(VehicleStepEvents.BoostStarted));
            Assert.That(s.IsBoosting);
            Assert.That(s.BoostMultiplier, Is.EqualTo(_cfg.drift.levelBoostMultiplier[1]).Within(1e-4));
            Run(ref s, PlayerInputFrame.Neutral, 15);
            Assert.That(s.Speed, Is.GreaterThan(_stats.TopSpeed * 1.05f), "boost exceeds normal top speed");
        }

        [Test]
        public void StoreChargeModeBanksChargesAndBoostButtonSpendsThem()
        {
            _cfg.drift.releaseMode = DriftReleaseMode.StoreCharge;
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 90);
            Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, (int)(_cfg.drift.levelThresholds[2] / Dt) + 5);
            var ev = Run(ref s, PlayerInputFrame.Neutral, 1);
            Assert.That(ev.HasFlag(VehicleStepEvents.ChargeStored));
            Assert.That(s.IsBoosting, Is.False, "nothing fires on release");
            Assert.That(s.BoostCharges, Is.EqualTo(_cfg.drift.levelStoredCharges[2]));
            ev = Run(ref s, new PlayerInputFrame { Boost = true }, 1);
            Assert.That(ev.HasFlag(VehicleStepEvents.ChargeSpent));
            Assert.That(s.IsBoosting);
            Assert.That(s.BoostCharges, Is.EqualTo(_cfg.drift.levelStoredCharges[2] - 1));
            Run(ref s, new PlayerInputFrame { Boost = true }, 5);
            Assert.That(s.BoostCharges, Is.EqualTo(_cfg.drift.levelStoredCharges[2] - 1), "holding the button spends one charge only");
            for (int i = 0; i < 10; i++) { Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, (int)(_cfg.drift.levelThresholds[2] / Dt) + 5); Run(ref s, PlayerInputFrame.Neutral, 1); }
            Assert.That(s.BoostCharges, Is.EqualTo(_cfg.boost.maxStoredCharges), "capped");
        }

        [Test]
        public void ReleasingUnchargedDriftGivesNoBoost()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 90);
            Run(ref s, new PlayerInputFrame { Steer = 1f, Drift = true }, 3);
            var ev = Run(ref s, PlayerInputFrame.Neutral, 1);
            Assert.That(ev.HasFlag(VehicleStepEvents.DriftCancelled));
            Assert.That(s.IsBoosting, Is.False);
        }

        [Test]
        public void BoostIsCappedByConfig()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            _model.ApplyBoost(ref s, 5f, 99f);
            Assert.That(s.BoostMultiplier, Is.EqualTo(_cfg.boost.maxMultiplier));
        }

        [Test]
        public void StunKillsSpeedAndSteering()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 90);
            float heading = s.Heading;
            _model.ApplyStun(ref s, 1f, 0.2f);
            Assert.That(s.Speed, Is.LessThan(_stats.TopSpeed * 0.25f));
            Run(ref s, new PlayerInputFrame { Steer = 1f }, 10);
            Assert.That(s.Heading, Is.EqualTo(heading).Within(1e-4f), "no steering while stunned");
            Assert.That(s.IsStunned);
            Run(ref s, PlayerInputFrame.Neutral, 30);
            Assert.That(s.IsStunned, Is.False);
        }

        [Test]
        public void OffroadIsSlowerAndTriggersRecoveryAfterDelay()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            var offroad = new SurfaceSample { OnRoad = false, DistanceBeyondRoadEdge = 20f, GripMultiplier = 1f, SpeedMultiplier = 1f };
            var ev = Run(ref s, PlayerInputFrame.Neutral, 90, offroad);
            Assert.That(ev.HasFlag(VehicleStepEvents.WentOffroad));
            Assert.That(s.Speed, Is.LessThan(_stats.TopSpeed * _cfg.driving.offroadSpeedMultiplier + 0.1f));
            Assert.That(_model.NeedsRecovery(s), Is.True);
            _model.Recover(ref s, new Vec3(5, 0, 5), 1f, _stats);
            Assert.That(s.Position, Is.EqualTo(new Vec3(5, 0, 5)));
            Assert.That(s.Heading, Is.EqualTo(1f));
            Assert.That(_model.NeedsRecovery(s), Is.False);
        }

        [Test]
        public void ImmobilisedVehicleStops()
        {
            var s = VehicleState.AtRest(Vec3.Zero, 0f);
            Run(ref s, PlayerInputFrame.Neutral, 60);
            s.IsImmobilised = true;
            Run(ref s, new PlayerInputFrame { Steer = 1f }, 60);
            Assert.That(s.Speed, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void IsDeterministic()
        {
            var a = VehicleState.AtRest(Vec3.Zero, 0f);
            var b = VehicleState.AtRest(Vec3.Zero, 0f);
            var input = new PlayerInputFrame { Steer = 0.4f, Drift = true };
            Run(ref a, input, 200);
            Run(ref b, input, 200);
            Assert.That(a.Position, Is.EqualTo(b.Position));
            Assert.That(a.Heading, Is.EqualTo(b.Heading));
            Assert.That(a.Speed, Is.EqualTo(b.Speed));
        }

        [Test]
        public void CharacterStrengthModifiesStats()
        {
            var content = TestContent.Create();
            var plain = VehicleStats.From(content.Vehicles.vehicles[0], null, _cfg);
            var tunde = VehicleStats.From(content.Vehicles.vehicles[0], content.Characters.characters[0], _cfg);
            Assert.That(tunde.Acceleration, Is.GreaterThan(plain.Acceleration));
            Assert.That(tunde.YawRate, Is.LessThan(plain.YawRate));
        }
    }
}
