using NaijaKart.Core.Config;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Input
{
    /// <summary>
    /// Converts a phone roll angle (degrees) into a steer value in [-1,1]. Pure function of config and
    /// player settings so it can be unit-tested and tuned without touching the Unity input code.
    /// Supports calibration offset, dead zone, sensitivity, inversion and response curve (PRD §17, §72).
    /// </summary>
    public sealed class TiltSteeringMapper
    {
        private readonly TiltConfig _config;
        private float _smoothedAngle;
        private bool _hasSample;

        public float CalibrationOffsetDegrees { get; private set; }
        public float Sensitivity { get; private set; }
        public bool Inverted { get; set; }

        public TiltSteeringMapper(TiltConfig config)
        {
            _config = config;
            Sensitivity = config.defaultSensitivity;
        }

        public void SetSensitivity(float sensitivity) =>
            Sensitivity = MathUtil.Clamp(sensitivity, _config.minSensitivity, _config.maxSensitivity);

        /// <summary>Treat the current roll as "centre". Called from the calibration UI.</summary>
        public void Calibrate(float currentRollDegrees) => CalibrationOffsetDegrees = currentRollDegrees;

        public void ResetSmoothing()
        {
            _hasSample = false;
            _smoothedAngle = 0f;
        }

        /// <summary>Maps a raw roll reading into steer, applying smoothing over dt seconds.</summary>
        public float Map(float rawRollDegrees, float dt)
        {
            if (!_hasSample)
            {
                _smoothedAngle = rawRollDegrees;
                _hasSample = true;
            }
            else
            {
                _smoothedAngle = MathUtil.Damp(_smoothedAngle, rawRollDegrees, _config.smoothingLambda, dt);
            }
            return MapInstant(_smoothedAngle);
        }

        /// <summary>Stateless mapping without smoothing. Useful for tests and calibration previews.</summary>
        public float MapInstant(float rollDegrees)
        {
            float angle = rollDegrees - CalibrationOffsetDegrees;
            float magnitude = System.Math.Abs(angle);
            if (magnitude <= _config.deadZoneDegrees) return 0f;

            float usable = _config.fullSteerAngleDegrees - _config.deadZoneDegrees;
            float t = MathUtil.Clamp01((magnitude - _config.deadZoneDegrees) / usable * Sensitivity);
            float curved = (float)System.Math.Pow(t, _config.responseExponent);
            float steer = curved * MathUtil.Sign(angle);
            return Inverted ? -steer : steer;
        }
    }
}
