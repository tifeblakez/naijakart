using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Vehicle
{
    /// <summary>
    /// Deterministic arcade driving model (PRD §22–§24). Runs identically on the server (authority) and
    /// on the client (prediction), so it must be pure: no engine calls, no allocation, no randomness.
    /// Fun over simulation: automatic acceleration, speed-sensitive steering, drift with charge levels
    /// that release into boost, soft lateral slide instead of rigid-body physics.
    /// </summary>
    public sealed class ArcadeVehicleModel
    {
        private readonly GameConfig _cfg;
        private readonly float _driftBoostDurationMul;

        public ArcadeVehicleModel(GameConfig cfg, float driftBoostDurationMultiplier = 1f)
        {
            _cfg = cfg;
            _driftBoostDurationMul = driftBoostDurationMultiplier > 0f ? driftBoostDurationMultiplier : 1f;
        }

        public VehicleStepEvents Step(ref VehicleState s, in PlayerInputFrame input, VehicleStats stats,
            in SurfaceSample surface, float dt)
        {
            VehicleStepEvents ev = VehicleStepEvents.None;
            if (dt <= 0f) return ev;

            TickTimers(ref s, dt, ref ev);

            var drv = _cfg.driving;
            var drift = _cfg.drift;

            float gripMul = surface.GripMultiplier * s.ExternalGripMultiplier;
            float speedMul = surface.SpeedMultiplier * s.ExternalSpeedMultiplier;
            if (!surface.OnRoad)
            {
                gripMul *= drv.offroadGripMultiplier;
                speedMul *= drv.offroadSpeedMultiplier;
            }

            // Offroad bookkeeping + recovery
            if (surface.OnRoad != !s.IsOffroad)
            {
                s.IsOffroad = !surface.OnRoad;
                ev |= s.IsOffroad ? VehicleStepEvents.WentOffroad : VehicleStepEvents.ReturnedToRoad;
            }
            if (!surface.OnRoad && surface.DistanceBeyondRoadEdge > drv.recoveryMarginMeters)
            {
                s.OffroadBeyondMarginTime += dt;
            }
            else
            {
                s.OffroadBeyondMarginTime = 0f;
            }
            s.OffroadTime = surface.OnRoad ? 0f : s.OffroadTime + dt;

            if (s.IsImmobilised)
            {
                s.Speed = MathUtil.MoveTowards(s.Speed, 0f, drv.brakeDeceleration * dt);
                s.LateralVelocity = MathUtil.Damp(s.LateralVelocity, 0f, stats.LateralGrip, dt);
                CancelDrift(ref s, ref ev);
                Integrate(ref s, dt);
                return ev;
            }

            float steer = input.Steer;
            if (s.WobbleTimeRemaining > 0f)
            {
                // Egg: steering oscillation the player must fight.
                steer += s.WobbleAmplitude * (float)System.Math.Sin(s.WobbleTimeRemaining * 14f);
                steer = MathUtil.Clamp(steer, -1f, 1f);
            }

            bool controllable = !s.IsStunned;

            // --- Speed ---
            float boostMul = s.IsBoosting ? MathUtil.Clamp(s.BoostMultiplier, 1f, _cfg.boost.maxMultiplier) : 1f;
            float targetSpeed = stats.TopSpeed * speedMul * boostMul;
            if (s.IsStunned)
            {
                targetSpeed = 0f;
            }
            if (s.IsDrifting)
            {
                targetSpeed *= drift.speedRetention;
            }

            if (s.Speed < targetSpeed)
            {
                float accel = s.IsBoosting ? _cfg.boost.boostAcceleration : stats.Acceleration;
                s.Speed = MathUtil.MoveTowards(s.Speed, targetSpeed, accel * dt);
            }
            else
            {
                float decel = s.IsStunned ? drv.brakeDeceleration : drv.coastDeceleration;
                s.Speed = MathUtil.MoveTowards(s.Speed, targetSpeed, decel * dt);
            }

            // --- Boost button spends a banked charge ---
            if (controllable && input.Boost && s.BoostCharges > 0 && !s.BoostHeld)
            {
                s.BoostCharges--;
                bool was = s.IsBoosting;
                ApplyBoost(ref s, _cfg.boost.storedChargeDuration * stats.BoostDurationScale, _cfg.boost.storedChargeMultiplier);
                ev |= VehicleStepEvents.ChargeSpent;
                if (!was) ev |= VehicleStepEvents.BoostStarted;
            }
            s.BoostHeld = input.Boost;

            // --- Drift state machine ---
            if (controllable)
            {
                if (!s.IsDrifting)
                {
                    if (input.Drift && s.Speed >= drift.minSpeed && System.Math.Abs(steer) >= drift.minSteerToStart)
                    {
                        s.IsDrifting = true;
                        s.DriftDirection = steer < 0f ? -1 : 1;
                        s.DriftCharge = 0f;
                        s.DriftLevel = DriftLevel.None;
                        ev |= VehicleStepEvents.DriftStarted;
                    }
                }
                else
                {
                    if (!input.Drift)
                    {
                        ReleaseDrift(ref s, stats, ref ev);
                    }
                    else if (s.Speed < drift.minSpeed)
                    {
                        CancelDrift(ref s, ref ev);
                    }
                    else
                    {
                        float steerToward = MathUtil.Clamp01(steer * s.DriftDirection);
                        s.DriftCharge += dt * drift.chargeRate * (1f + drift.steerIntoChargeBonus * steerToward);
                        DriftLevel newLevel = LevelFor(s.DriftCharge, drift);
                        if (newLevel > s.DriftLevel)
                        {
                            s.DriftLevel = newLevel;
                            ev |= VehicleStepEvents.DriftLevelUp;
                        }
                    }
                }
            }
            else if (s.IsDrifting)
            {
                CancelDrift(ref s, ref ev);
            }

            // --- Steering ---
            if (controllable)
            {
                float speedFactor = s.Speed < drv.steeringPeakSpeed
                    ? s.Speed / System.Math.Max(0.01f, drv.steeringPeakSpeed)
                    : MathUtil.Lerp(1f, drv.highSpeedSteerFactor,
                        MathUtil.InverseLerp(drv.steeringPeakSpeed, stats.TopSpeed, s.Speed));

                float yaw;
                if (s.IsDrifting)
                {
                    float steerToward = MathUtil.Clamp(steer * s.DriftDirection, -1f, 1f);
                    float factor = MathUtil.LerpUnclamped(drift.innerSteerFactor, drift.outerSteerFactor, (steerToward + 1f) * 0.5f);
                    yaw = stats.YawRate * stats.DriftYawMultiplier * factor * s.DriftDirection;
                    s.LateralVelocity -= s.DriftDirection * drift.slideInjection * dt;
                }
                else
                {
                    yaw = stats.YawRate * steer;
                }
                s.Heading = MathUtil.WrapAngle(s.Heading + yaw * speedFactor * dt);
            }

            // --- Lateral grip ---
            float grip = stats.LateralGrip * gripMul * (s.IsDrifting ? drift.gripMultiplier : 1f);
            s.LateralVelocity = MathUtil.Damp(s.LateralVelocity, 0f, grip, dt);

            Integrate(ref s, dt);
            return ev;
        }

        /// <summary>Starts (or extends) a boost. Used by drift release and boost items.</summary>
        public void ApplyBoost(ref VehicleState s, float duration, float multiplier)
        {
            bool wasBoosting = s.IsBoosting;
            float m = MathUtil.Clamp(multiplier, 1f, _cfg.boost.maxMultiplier);
            if (!wasBoosting)
            {
                s.BoostMultiplier = m;
                s.BoostTimeRemaining = duration;
            }
            else
            {
                s.BoostMultiplier = System.Math.Max(s.BoostMultiplier, m);
                s.BoostTimeRemaining = System.Math.Max(s.BoostTimeRemaining, duration);
            }
        }

        public void ApplyStun(ref VehicleState s, float seconds, float speedRetention)
        {
            s.StunTimeRemaining = System.Math.Max(s.StunTimeRemaining, seconds);
            s.Speed *= MathUtil.Clamp01(speedRetention);
            s.BoostTimeRemaining = 0f;
        }

        /// <summary>Teleports the kart back onto the track after leaving it (recovery zone behaviour).</summary>
        public void Recover(ref VehicleState s, Vec3 position, float heading, VehicleStats stats)
        {
            s.Position = position;
            s.Heading = heading;
            s.Speed = stats.TopSpeed * _cfg.driving.recoverySpeedFraction;
            s.LateralVelocity = 0f;
            s.OffroadBeyondMarginTime = 0f;
            s.OffroadTime = 0f;
            s.IsOffroad = false;
            s.StunTimeRemaining = System.Math.Max(s.StunTimeRemaining, _cfg.driving.recoveryStunSeconds);
            s.IsDrifting = false;
            s.DriftCharge = 0f;
            s.DriftLevel = DriftLevel.None;
        }

        public bool NeedsRecovery(in VehicleState s) =>
            s.OffroadBeyondMarginTime >= _cfg.driving.recoveryDelaySeconds
            || (_cfg.driving.offroadStuckSeconds > 0f && s.OffroadTime >= _cfg.driving.offroadStuckSeconds);

        private static DriftLevel LevelFor(float charge, DriftConfig drift)
        {
            if (charge >= drift.levelThresholds[2]) return DriftLevel.Purple;
            if (charge >= drift.levelThresholds[1]) return DriftLevel.Orange;
            if (charge >= drift.levelThresholds[0]) return DriftLevel.Blue;
            return DriftLevel.None;
        }

        private void ReleaseDrift(ref VehicleState s, VehicleStats stats, ref VehicleStepEvents ev)
        {
            DriftLevel level = s.DriftLevel;
            s.IsDrifting = false;
            s.DriftCharge = 0f;
            s.DriftLevel = DriftLevel.None;
            if (level == DriftLevel.None)
            {
                ev |= VehicleStepEvents.DriftCancelled;
                return;
            }
            int idx = (int)level - 1;
            if (_cfg.drift.releaseMode == DriftReleaseMode.StoreCharge)
            {
                int add = _cfg.drift.levelStoredCharges[System.Math.Min(idx, _cfg.drift.levelStoredCharges.Length - 1)];
                s.BoostCharges = System.Math.Min(_cfg.boost.maxStoredCharges, s.BoostCharges + add);
                ev |= VehicleStepEvents.DriftReleasedWithBoost | VehicleStepEvents.ChargeStored;
                return;
            }
            float duration = _cfg.drift.levelBoostDuration[idx] * stats.BoostDurationScale * _driftBoostDurationMul;
            float mul = _cfg.drift.levelBoostMultiplier[idx];
            bool wasBoosting = s.IsBoosting;
            ApplyBoost(ref s, duration, mul);
            ev |= VehicleStepEvents.DriftReleasedWithBoost;
            if (!wasBoosting) ev |= VehicleStepEvents.BoostStarted;
        }

        private static void CancelDrift(ref VehicleState s, ref VehicleStepEvents ev)
        {
            if (!s.IsDrifting) return;
            s.IsDrifting = false;
            s.DriftCharge = 0f;
            s.DriftLevel = DriftLevel.None;
            ev |= VehicleStepEvents.DriftCancelled;
        }

        private static void TickTimers(ref VehicleState s, float dt, ref VehicleStepEvents ev)
        {
            if (s.BoostTimeRemaining > 0f)
            {
                s.BoostTimeRemaining -= dt;
                if (s.BoostTimeRemaining <= 0f)
                {
                    s.BoostTimeRemaining = 0f;
                    s.BoostMultiplier = 1f;
                    ev |= VehicleStepEvents.BoostEnded;
                }
            }
            if (s.StunTimeRemaining > 0f)
            {
                s.StunTimeRemaining -= dt;
                if (s.StunTimeRemaining <= 0f)
                {
                    s.StunTimeRemaining = 0f;
                    ev |= VehicleStepEvents.StunEnded;
                }
            }
            if (s.WobbleTimeRemaining > 0f)
            {
                s.WobbleTimeRemaining = System.Math.Max(0f, s.WobbleTimeRemaining - dt);
                if (s.WobbleTimeRemaining <= 0f) s.WobbleAmplitude = 0f;
            }
            if (s.BlindTimeRemaining > 0f) s.BlindTimeRemaining = System.Math.Max(0f, s.BlindTimeRemaining - dt);
            if (s.HazardImmunityTime > 0f) s.HazardImmunityTime = System.Math.Max(0f, s.HazardImmunityTime - dt);
            if (s.ShieldTimeRemaining > 0f) s.ShieldTimeRemaining = System.Math.Max(0f, s.ShieldTimeRemaining - dt);
            if (s.ExternalSpeedMultiplierTime > 0f)
            {
                s.ExternalSpeedMultiplierTime -= dt;
                if (s.ExternalSpeedMultiplierTime <= 0f) { s.ExternalSpeedMultiplierTime = 0f; s.ExternalSpeedMultiplier = 1f; }
            }
            if (s.ExternalGripMultiplierTime > 0f)
            {
                s.ExternalGripMultiplierTime -= dt;
                if (s.ExternalGripMultiplierTime <= 0f) { s.ExternalGripMultiplierTime = 0f; s.ExternalGripMultiplier = 1f; }
            }
        }

        private static void Integrate(ref VehicleState s, float dt)
        {
            s.Position = s.Position + s.Velocity * dt;
        }
    }
}
