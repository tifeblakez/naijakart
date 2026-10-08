using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class TiltSteeringMapperTests
    {
        private static TiltSteeringMapper Mapper() => new TiltSteeringMapper(new TiltConfig { fullSteerAngleDegrees = 15f, deadZoneDegrees = 1.5f, responseExponent = 1f, defaultSensitivity = 1f });

        [Test]
        public void DeadZoneProducesNoSteer()
        {
            var m = Mapper();
            Assert.That(m.MapInstant(1.0f), Is.EqualTo(0f));
            Assert.That(m.MapInstant(-1.4f), Is.EqualTo(0f));
        }

        [Test]
        public void FullAngleMapsToFullSteerWithSign()
        {
            var m = Mapper();
            Assert.That(m.MapInstant(15f), Is.EqualTo(1f).Within(1e-4));
            Assert.That(m.MapInstant(-15f), Is.EqualTo(-1f).Within(1e-4));
            Assert.That(m.MapInstant(40f), Is.EqualTo(1f).Within(1e-4), "clamped beyond full angle");
        }

        [Test]
        public void HalfwayIsProportionalAfterDeadZone()
        {
            var m = Mapper();
            float half = m.MapInstant(1.5f + 13.5f * 0.5f);
            Assert.That(half, Is.EqualTo(0.5f).Within(1e-3));
        }

        [Test]
        public void CalibrationShiftsCentre()
        {
            var m = Mapper();
            m.Calibrate(10f);
            Assert.That(m.MapInstant(10f), Is.EqualTo(0f));
            Assert.That(m.MapInstant(25f), Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void InversionAndSensitivity()
        {
            var m = Mapper();
            m.Inverted = true;
            Assert.That(m.MapInstant(15f), Is.EqualTo(-1f).Within(1e-4));
            m.Inverted = false;
            m.SetSensitivity(2f);
            Assert.That(m.MapInstant(1.5f + 13.5f * 0.5f), Is.EqualTo(1f).Within(1e-3), "double sensitivity saturates at half angle");
            m.SetSensitivity(99f);
            Assert.That(m.Sensitivity, Is.EqualTo(2f), "clamped to config max");
        }

        [Test]
        public void SmoothingConvergesToTarget()
        {
            var m = Mapper();
            float v = 0f;
            for (int i = 0; i < 120; i++) v = m.Map(15f, 1f / 60f);
            Assert.That(v, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void InputFrameSanitisesGarbage()
        {
            var f = new PlayerInputFrame { Steer = float.NaN }.Sanitised();
            Assert.That(f.Steer, Is.EqualTo(0f));
            f = new PlayerInputFrame { Steer = 9f }.Sanitised();
            Assert.That(f.Steer, Is.EqualTo(1f));
        }
    }
}
