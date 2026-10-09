using System.Collections.Generic;
using NaijaKart.Core.Chaos;
using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Math;
using NaijaKart.Core.Race;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Simulation
{
    /// <summary>
    /// Centreline-following AI that produces PlayerInputFrames like a client would. Bots are always
    /// flagged (RaceParticipant.IsBot) and are never presented as real players (PRD §87 "no fake
    /// multiplayer"). Uses: headless tests, server soak tests, practice mode, filling private rooms on
    /// request. Skill 0..1 scales lookahead, drift usage and item usage.
    /// </summary>
    public sealed class BotDriver
    {
        private readonly TrackGeometry _track;
        private readonly DeterministicRandom _rng;
        private readonly float _skill;
        private readonly BotConfig _cfg;
        private float _laneOffset;
        private float _laneTarget;
        private float _laneChangeTimer;
        private int _sequence;
        private float _itemHoldTimer;
        private float _driftHeld;
        /// <summary>Hold a started drift at least this long so it banks a charge (the first drift level).</summary>
        private const float MinDriftSeconds = 1.0f;
        private const float MaxDriftSeconds = 2.5f;

        public BotDriver(TrackGeometry track, ulong seed, float skill = 0.6f, BotConfig config = null)
        {
            _track = track;
            _rng = new DeterministicRandom(seed);
            _skill = MathUtil.Clamp01(skill);
            _cfg = config ?? new BotConfig();
            _laneOffset = _laneTarget = PickLane();
            _laneChangeTimer = _rng.Range(_cfg.laneChangeMinSeconds, _cfg.laneChangeMaxSeconds);
        }

        /// <summary>The lane (metres right of the centreline) this bot currently prefers.</summary>
        public float LaneOffset => _laneOffset;

        private float PickLane()
        {
            float spread = _track.Definition.roadHalfWidth * MathUtil.Clamp01(_cfg.laneSpreadFraction);
            return _rng.Range(-spread, spread);
        }

        /// <summary>Lateral offset (metres, +right) that steers around slower karts just ahead, so the field spreads out when it bunches.</summary>
        private float KartAvoidanceOffset(RaceParticipant self, IReadOnlyList<RaceParticipant> others, float selfLateral, float safeHalfWidth)
        {
            if (others == null) return 0f;
            var s = self.State;
            float range = _cfg.kartAvoidanceRange;
            float offset = 0f;
            Vec3 fwd = s.Forward;
            Vec3 right = s.Right;
            for (int i = 0; i < others.Count; i++)
            {
                var o = others[i];
                if (o == null || ReferenceEquals(o, self) || !o.IsActiveRacer) continue;
                Vec3 d = (o.State.Position - s.Position).Flat;
                float ahead = Vec3.Dot(d, fwd);
                if (ahead < 0.5f || ahead > range) continue;
                if (o.State.Speed > s.Speed + _cfg.overtakeSpeedMargin) continue;
                float lateral = Vec3.Dot(d, right);
                float clearance = _cfg.kartAvoidanceClearance;
                if (System.Math.Abs(lateral) > clearance) continue;
                // Pass on the side with more room; the closer the kart, the stronger the pull. Never
                // pull towards the verge: if that side has no road left, go round the other way.
                float side = lateral >= 0f ? -1f : 1f;
                if (System.Math.Abs(selfLateral + side * clearance) > safeHalfWidth) side = -side;
                float weight = 1f - ahead / range;
                offset += side * (clearance - System.Math.Abs(lateral) + 0.8f) * weight;
            }
            return offset;
        }

        /// <summary>Lateral offset (metres, +right) that steers around armed non-zone hazards ahead.</summary>
        private float AvoidanceOffset(in VehicleState s, IReadOnlyList<Hazard> hazards, float range)
        {
            if (hazards == null) return 0f;
            float offset = 0f;
            Vec3 fwd = s.Forward;
            Vec3 right = s.Right;
            for (int i = 0; i < hazards.Count; i++)
            {
                var h = hazards[i];
                if (h.IsZone) continue;
                Vec3 d = (h.Position - s.Position).Flat;
                float ahead = Vec3.Dot(d, fwd);
                if (ahead < 0f || ahead > range) continue;
                float lateral = Vec3.Dot(d, right);
                float clearance = h.Radius + 2.5f;
                if (System.Math.Abs(lateral) > clearance) continue;
                // Steer to the side with more room; closer hazards weigh more.
                float side = lateral >= 0f ? -1f : 1f;
                float weight = 1f - ahead / range;
                offset += side * (clearance - System.Math.Abs(lateral) + 1f) * weight;
            }
            return offset;
        }

        public PlayerInputFrame Think(RaceParticipant p, float dt) => Think(p, null, null, dt);

        public PlayerInputFrame Think(RaceParticipant p, IReadOnlyList<Hazard> hazards, float dt) => Think(p, hazards, null, dt);

        /// <summary>Produces the next input frame. <paramref name="others"/> are the race's participants (self included is fine) so the bot can pass slower karts.</summary>
        public PlayerInputFrame Think(RaceParticipant p, IReadOnlyList<Hazard> hazards, IReadOnlyList<RaceParticipant> others, float dt)
        {
            var s = p.State;
            var proj = _track.Project(s.Position);
            float lookahead = MathUtil.Lerp(8f, 18f, _skill) + s.Speed * 0.6f;
            _track.Sample(proj.DistanceAlong + lookahead, out Vec3 target, out Vec3 dir);
            Vec3 right = Vec3.Cross(Vec3.Up, dir);

            // Preferred lane: re-picked now and then, approached smoothly.
            _laneChangeTimer -= dt;
            if (_laneChangeTimer <= 0f)
            {
                _laneChangeTimer = _rng.Range(_cfg.laneChangeMinSeconds, _cfg.laneChangeMaxSeconds);
                _laneTarget = PickLane();
            }
            float maxStep = _cfg.laneBlendMetresPerSecond * dt;
            _laneOffset += MathUtil.Clamp(_laneTarget - _laneOffset, -maxStep, maxStep);

            // Keep a kart's width plus a margin inside the verge, and straighten the line through corners:
            // the preferred lane shrinks with how much the road turns over the lookahead.
            float halfWidth = _track.Definition.roadHalfWidth - 1.8f;
            _track.Sample(proj.DistanceAlong, out _, out Vec3 hereDir);
            float turn = System.Math.Abs(MathUtil.WrapAngle(dir.ToYaw() - hereDir.ToYaw()));
            float cornerScale = 1f - 0.7f * MathUtil.Clamp01(turn / 0.6f);
            float lane = s.IsOffroad ? 0f : _laneOffset * cornerScale + AvoidanceOffset(s, hazards, lookahead * 1.5f) + KartAvoidanceOffset(p, others, proj.LateralOffset, halfWidth);
            target = target + right * MathUtil.Clamp(lane, -halfWidth, halfWidth);

            Vec3 toTarget = (target - s.Position).Flat;
            float desired = toTarget.ToYaw();
            float error = MathUtil.WrapAngle(desired - s.Heading);
            float steer = MathUtil.Clamp(error * MathUtil.Lerp(1.2f, 2.2f, _skill), -1f, 1f);

            // Drift through sustained turns when skilled enough; once started, hold it long enough to bank a charge.
            float absError = System.Math.Abs(error);
            // Drift through medium turns only: hairpins (large error) and offroad are handled by steering, not sliding.
            bool drift = _skill > 0.3f && absError > MathUtil.Lerp(0.5f, 0.3f, _skill) && absError < 1.0f && s.Speed > 12f && !s.IsOffroad;
            if (s.IsDrifting)
            {
                _driftHeld += dt;
                // Skilled bots hold the drift long enough to bank a charge; never past MaxDriftSeconds or while offroad.
                float hold = _skill >= 0.5f ? MinDriftSeconds : 0f;
                drift = !s.IsOffroad && _driftHeld < MaxDriftSeconds && (absError > 0.12f || _driftHeld < hold);
            }
            else
            {
                _driftHeld = 0f;
            }

            // Spend banked boost on straights (small steering error), like a player would.
            bool boost = s.BoostCharges > 0 && !s.IsBoosting && System.Math.Abs(error) < 0.15f && s.Speed > 12f && _rng.Chance(MathUtil.Lerp(0.05f, 0.2f, _skill));

            bool useItem = false;
            _itemHoldTimer -= dt;
            if (_itemHoldTimer <= 0f)
            {
                _itemHoldTimer = _rng.Range(0.8f, 2.5f);
                useItem = _rng.Chance(MathUtil.Lerp(0.3f, 0.8f, _skill));
            }

            return new PlayerInputFrame
            {
                Sequence = ++_sequence,
                Steer = steer,
                Drift = drift,
                UseItem = useItem,
                ItemSlot = -1,
                Boost = boost
            };
        }
    }
}
