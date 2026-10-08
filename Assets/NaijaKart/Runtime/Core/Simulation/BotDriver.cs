using System.Collections.Generic;
using NaijaKart.Core.Chaos;
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
        private readonly float _laneOffset;
        private int _sequence;
        private float _itemHoldTimer;

        public BotDriver(TrackGeometry track, ulong seed, float skill = 0.6f)
        {
            _track = track;
            _rng = new DeterministicRandom(seed);
            _skill = MathUtil.Clamp01(skill);
            _laneOffset = _rng.Range(-track.Definition.roadHalfWidth * 0.5f, track.Definition.roadHalfWidth * 0.5f);
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

        public PlayerInputFrame Think(RaceParticipant p, float dt) => Think(p, null, dt);

        public PlayerInputFrame Think(RaceParticipant p, IReadOnlyList<Hazard> hazards, float dt)
        {
            var s = p.State;
            var proj = _track.Project(s.Position);
            float lookahead = MathUtil.Lerp(8f, 18f, _skill) + s.Speed * 0.6f;
            _track.Sample(proj.DistanceAlong + lookahead, out Vec3 target, out Vec3 dir);
            Vec3 right = Vec3.Cross(Vec3.Up, dir);
            float lane = _laneOffset + AvoidanceOffset(s, hazards, lookahead * 1.5f);
            float halfWidth = _track.Definition.roadHalfWidth - 1f;
            target = target + right * MathUtil.Clamp(lane, -halfWidth, halfWidth);

            Vec3 toTarget = (target - s.Position).Flat;
            float desired = toTarget.ToYaw();
            float error = MathUtil.WrapAngle(desired - s.Heading);
            float steer = MathUtil.Clamp(error * MathUtil.Lerp(1.2f, 2.2f, _skill), -1f, 1f);

            // Drift through sustained turns when skilled enough.
            bool drift = _skill > 0.3f && System.Math.Abs(error) > MathUtil.Lerp(0.5f, 0.3f, _skill) && s.Speed > 10f;
            if (s.IsDrifting && System.Math.Abs(error) > 0.12f) drift = true;

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
                UseItem = useItem
            };
        }
    }
}
