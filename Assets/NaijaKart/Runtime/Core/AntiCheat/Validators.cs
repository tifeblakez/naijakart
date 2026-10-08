using NaijaKart.Core.Config;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.AntiCheat
{
    /// <summary>
    /// Validation helpers used by the server (PRD §91). The design is server-authoritative so the main
    /// defence is structural: clients send inputs only. These checks cover the remaining surface:
    /// input flood/garbage, lap plausibility, and (for any future soft-trust path such as validating a
    /// client's predicted position before accepting it for cosmetic sync) movement plausibility.
    /// </summary>
    public static class MovementValidator
    {
        public static bool IsPlausibleMove(Vec3 from, Vec3 to, float topSpeed, float dt, AntiCheatConfig cfg)
        {
            float budget = topSpeed * cfg.maxSpeedTolerance * dt + cfg.teleportSlackMeters;
            return Vec3.FlatDistance(from, to) <= budget;
        }

        public static bool IsPlausibleSpeed(float speed, float topSpeed, float maxBoostMultiplier, AntiCheatConfig cfg)
        {
            return speed >= 0f && speed <= topSpeed * maxBoostMultiplier * cfg.maxSpeedTolerance;
        }
    }

    public static class LapValidator
    {
        public static float MinimumLapSeconds(TrackDefinition track, AntiCheatConfig cfg)
        {
            float fromReference = track.referenceLapSeconds * cfg.minLapFractionOfReference;
            return System.Math.Max(cfg.absoluteMinLapSeconds, fromReference);
        }
    }

    /// <summary>Rate-limits input frames per client to prevent floods; drops out-of-order frames.</summary>
    public sealed class InputRateLimiter
    {
        private readonly int _maxPerSecond;
        private float _windowStart;
        private int _count;
        private int _lastSequence = -1;

        public InputRateLimiter(int maxPerSecond)
        {
            _maxPerSecond = System.Math.Max(1, maxPerSecond);
        }

        public bool Accept(int sequence, float now)
        {
            if (now - _windowStart >= 1f)
            {
                _windowStart = now;
                _count = 0;
            }
            if (_count >= _maxPerSecond) return false;
            if (sequence <= _lastSequence) return false;
            _lastSequence = sequence;
            _count++;
            return true;
        }
    }
}
