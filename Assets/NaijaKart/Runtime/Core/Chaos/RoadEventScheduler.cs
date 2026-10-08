using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Race;

namespace NaijaKart.Core.Chaos
{
    /// <summary>Road event kinds in the order of RoadEventsConfig.kindWeights.</summary>
    public enum RoadEventKind
    {
        Traffic = 0,
        Pothole = 1,
        OkadaCross = 2,
        DanfoCross = 3,
        GoSlow = 4,
        Flood = 5,
        CheckpointStop = 6
    }

    /// <summary>
    /// Dynamic Nigerian Road System (PRD §29). Schedules partially predictable events: timing is seeded
    /// random within a configured interval, every event is telegraphed before it becomes dangerous, and
    /// placement is ahead of a randomly chosen active racer (so chaos is spread across the field, not
    /// aimed at the leader). Track anchors can constrain which kinds appear where.
    /// </summary>
    public sealed class RoadEventScheduler
    {
        private readonly IRaceContext _ctx;
        private readonly RoadEventsConfig _cfg;
        private float _nextEventIn;

        public RoadEventScheduler(IRaceContext ctx)
        {
            _ctx = ctx;
            _cfg = ctx.Config.roadEvents;
            _nextEventIn = _ctx.Rng.Range(_cfg.minIntervalSeconds, _cfg.maxIntervalSeconds);
        }

        public void Tick(float dt)
        {
            if (!_cfg.enabled) return;
            _nextEventIn -= dt;
            if (_nextEventIn > 0f) return;
            _nextEventIn = _ctx.Rng.Range(_cfg.minIntervalSeconds, _cfg.maxIntervalSeconds);

            var active = _ctx.ActiveByPosition;
            if (active.Count == 0) return;
            var anchor = active[_ctx.Rng.Range(0, active.Count)];
            int kindIndex = _ctx.Rng.WeightedIndex(_cfg.kindWeights);
            if (kindIndex < 0) return;
            Spawn((RoadEventKind)kindIndex, anchor);
        }

        /// <summary>Spawns a road event ahead of the given racer. Public so items (Danfo/Okada) and tests reuse it.</summary>
        public Hazard Spawn(RoadEventKind kind, RaceParticipant anchor, float aheadMetersOverride = -1f)
        {
            var track = _ctx.Track;
            var proj = track.Project(anchor.State.Position);
            float ahead = aheadMetersOverride >= 0f ? aheadMetersOverride : _cfg.placeAheadMeters;
            track.Sample(proj.DistanceAlong + ahead, out Vec3 centre, out Vec3 dir);
            Vec3 right = Vec3.Cross(Vec3.Up, dir);
            float halfWidth = track.Definition.roadHalfWidth;
            Hazard h = null;
            switch (kind)
            {
                case RoadEventKind.Traffic:
                    h = _ctx.Hazards.Spawn(HazardKind.TrafficCar, centre + right * _ctx.Rng.Range(-halfWidth * 0.6f, halfWidth * 0.6f),
                        1.6f, _cfg.staticLifetimeSeconds, _cfg.telegraphSeconds);
                    if (h != null) h.Velocity = dir * (_cfg.crossingSpeed * 0.5f);
                    break;
                case RoadEventKind.Pothole:
                    h = _ctx.Hazards.Spawn(HazardKind.Pothole, centre + right * _ctx.Rng.Range(-halfWidth * 0.7f, halfWidth * 0.7f),
                        1.2f, _cfg.staticLifetimeSeconds, _cfg.telegraphSeconds * 0.5f);
                    break;
                case RoadEventKind.OkadaCross:
                case RoadEventKind.DanfoCross:
                {
                    bool fromLeft = _ctx.Rng.Chance(0.5f);
                    float side = fromLeft ? -1f : 1f;
                    var hk = kind == RoadEventKind.OkadaCross ? HazardKind.OkadaCrossing : HazardKind.DanfoCrossing;
                    float radius = kind == RoadEventKind.OkadaCross ? 1.0f : 2.2f;
                    float speed = kind == RoadEventKind.OkadaCross ? _cfg.crossingSpeed * 1.4f : _cfg.crossingSpeed;
                    h = _ctx.Hazards.Spawn(hk, centre + right * (side * (halfWidth + radius + 1f)), radius,
                        _cfg.crossingLifetimeSeconds + _cfg.telegraphSeconds, _cfg.telegraphSeconds);
                    if (h != null) h.Velocity = right * (-side * speed);
                    break;
                }
                case RoadEventKind.GoSlow:
                    h = _ctx.Hazards.SpawnZone(HazardKind.GoSlowZone, centre, dir, _cfg.goSlowLengthMeters * 0.5f, halfWidth,
                        _cfg.staticLifetimeSeconds, _cfg.telegraphSeconds);
                    break;
                case RoadEventKind.Flood:
                    h = _ctx.Hazards.SpawnZone(HazardKind.FloodZone, centre, dir, _cfg.goSlowLengthMeters * 0.4f, halfWidth,
                        _cfg.staticLifetimeSeconds, _cfg.telegraphSeconds);
                    break;
                case RoadEventKind.CheckpointStop:
                    h = _ctx.Hazards.SpawnZone(HazardKind.PoliceCheckpoint, centre, dir, 4f, halfWidth,
                        _cfg.staticLifetimeSeconds * 0.6f, _cfg.telegraphSeconds);
                    break;
            }
            if (h != null)
            {
                var e = _ctx.Emit(RaceEventType.RoadEventTelegraph, anchor.PlayerId, null, h.Kind.ToString());
                e.Position = h.Position;
                var s = _ctx.Emit(RaceEventType.HazardSpawned, null, null, h.Id);
                s.Position = h.Position;
            }
            return h;
        }
    }
}
