using NaijaKart.Core.Config;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Vehicle
{
    /// <summary>
    /// Physical driving parameters derived from a VehicleDefinition (+ optional character modifiers)
    /// through DrivingConfig. Built once per race participant.
    /// </summary>
    public sealed class VehicleStats
    {
        public string VehicleId;
        public float TopSpeed;
        public float Acceleration;
        public float YawRate;
        public float LateralGrip;
        public float DriftYawMultiplier;
        public float BoostDurationScale;
        /// <summary>0..1 normalised weight used by collision resolution.</summary>
        public float Weight01;

        public static VehicleStats From(VehicleDefinition v, CharacterDefinition character, GameConfig cfg)
        {
            int speed = v.speed, accel = v.acceleration, handling = v.handling, drift = v.drift,
                weight = v.weight, boost = v.boost, traction = v.traction;

            if (character != null)
            {
                Apply(character.strengthStat, character.strengthBonus, ref speed, ref accel, ref handling, ref drift, ref weight, ref boost, ref traction);
                Apply(character.weaknessStat, -character.weaknessPenalty, ref speed, ref accel, ref handling, ref drift, ref weight, ref boost, ref traction);
            }

            var d = cfg.driving;
            return new VehicleStats
            {
                VehicleId = v.id,
                TopSpeed = MathUtil.StatToRange(speed, d.minTopSpeed, d.maxTopSpeed),
                Acceleration = MathUtil.StatToRange(accel, d.minAcceleration, d.maxAcceleration),
                YawRate = MathUtil.StatToRange(handling, d.minYawRate, d.maxYawRate),
                LateralGrip = MathUtil.StatToRange(traction, d.minLateralGrip, d.maxLateralGrip),
                DriftYawMultiplier = MathUtil.StatToRange(drift, cfg.drift.minYawMultiplier, cfg.drift.maxYawMultiplier),
                BoostDurationScale = MathUtil.StatToRange(boost, cfg.boost.minDurationScale, cfg.boost.maxDurationScale),
                Weight01 = MathUtil.Clamp(weight, 0, 100) / 100f
            };
        }

        private static void Apply(string stat, int delta, ref int speed, ref int accel, ref int handling,
            ref int drift, ref int weight, ref int boost, ref int traction)
        {
            if (string.IsNullOrEmpty(stat) || delta == 0) return;
            switch (stat.ToLowerInvariant())
            {
                case "speed": speed = MathUtil.Clamp(speed + delta, 0, 100); break;
                case "acceleration": accel = MathUtil.Clamp(accel + delta, 0, 100); break;
                case "handling": handling = MathUtil.Clamp(handling + delta, 0, 100); break;
                case "drift": drift = MathUtil.Clamp(drift + delta, 0, 100); break;
                case "weight": weight = MathUtil.Clamp(weight + delta, 0, 100); break;
                case "boost": boost = MathUtil.Clamp(boost + delta, 0, 100); break;
                case "traction": traction = MathUtil.Clamp(traction + delta, 0, 100); break;
            }
        }
    }
}
