using System;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Vehicle
{
    public enum DriftLevel { None = 0, Blue = 1, Orange = 2, Purple = 3 }

    /// <summary>
    /// Complete dynamic state of one kart. Plain struct so it can be copied for prediction/rollback and
    /// serialised into snapshots. All timers are in seconds and count down to zero.
    /// </summary>
    [Serializable]
    public struct VehicleState
    {
        public Vec3 Position;
        /// <summary>Yaw in radians. 0 faces +Z.</summary>
        public float Heading;
        /// <summary>Forward speed along the heading (m/s). Never negative in the arcade model.</summary>
        public float Speed;
        /// <summary>Sideways slide velocity (m/s), positive = sliding right.</summary>
        public float LateralVelocity;

        public bool IsDrifting;
        /// <summary>-1 = drifting left, +1 = drifting right.</summary>
        public int DriftDirection;
        public float DriftCharge;
        public DriftLevel DriftLevel;

        public float BoostTimeRemaining;
        public float BoostMultiplier;

        public float StunTimeRemaining;
        public float WobbleTimeRemaining;
        public float WobbleAmplitude;
        public float BlindTimeRemaining;
        public float ShieldTimeRemaining;
        public int AutoAvoidCharges;
        /// <summary>Brief immunity to hazards after being hit, so a stunned kart is not hit again every tick.</summary>
        public float HazardImmunityTime;
        /// <summary>Temporary speed multiplier (e.g. go-slow zone, LASTMA pulled over). 1 = none.</summary>
        public float ExternalSpeedMultiplier;
        public float ExternalSpeedMultiplierTime;
        /// <summary>Temporary grip multiplier (oil, flood). 1 = none.</summary>
        public float ExternalGripMultiplier;
        public float ExternalGripMultiplierTime;

        public bool IsOffroad;
        public float OffroadBeyondMarginTime;
        /// <summary>Set by LASTMA while the racer is pulled over. Vehicle cannot move.</summary>
        public bool IsImmobilised;

        public static VehicleState AtRest(Vec3 position, float heading)
        {
            return new VehicleState
            {
                Position = position,
                Heading = heading,
                BoostMultiplier = 1f,
                ExternalSpeedMultiplier = 1f,
                ExternalGripMultiplier = 1f,
                DriftDirection = 0
            };
        }

        public bool IsBoosting => BoostTimeRemaining > 0f;
        public bool IsStunned => StunTimeRemaining > 0f;
        public bool HasShield => ShieldTimeRemaining > 0f;
        public Vec3 Forward => Vec3.FromYaw(Heading);
        public Vec3 Right => Vec3.FromYaw(Heading + MathUtil.Pi * 0.5f);
        public Vec3 Velocity => Forward * Speed + Right * LateralVelocity;
    }

    /// <summary>Events produced by one vehicle step, for presentation and telemetry.</summary>
    [Flags]
    public enum VehicleStepEvents
    {
        None = 0,
        DriftStarted = 1 << 0,
        DriftLevelUp = 1 << 1,
        DriftReleasedWithBoost = 1 << 2,
        DriftCancelled = 1 << 3,
        BoostStarted = 1 << 4,
        BoostEnded = 1 << 5,
        WentOffroad = 1 << 6,
        ReturnedToRoad = 1 << 7,
        Recovered = 1 << 8,
        StunEnded = 1 << 9
    }

    /// <summary>Surface the kart is currently on, sampled by the track geometry each tick.</summary>
    public struct SurfaceSample
    {
        public bool OnRoad;
        public float DistanceBeyondRoadEdge;
        public float GripMultiplier;
        public float SpeedMultiplier;

        public static SurfaceSample Road => new SurfaceSample { OnRoad = true, GripMultiplier = 1f, SpeedMultiplier = 1f };
    }
}
