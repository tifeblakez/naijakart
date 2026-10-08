using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.Chaos
{
    /// <summary>Owns all live hazards in a race: spawning, motion, expiry, and contact queries.</summary>
    public sealed class HazardSystem
    {
        private readonly List<Hazard> _hazards = new List<Hazard>();
        private readonly List<Hazard> _expired = new List<Hazard>();
        private readonly int _maxActive;

        public IReadOnlyList<Hazard> All => _hazards;

        public HazardSystem(ItemSystemConfig cfg)
        {
            _maxActive = cfg.maxActiveHazards;
        }

        public Hazard Spawn(HazardKind kind, Vec3 position, float radius, float ttl, float telegraph, string owner = null)
        {
            if (_hazards.Count >= _maxActive)
            {
                // Drop the oldest non-zone hazard to stay within budget.
                for (int i = 0; i < _hazards.Count; i++)
                {
                    if (!_hazards[i].IsZone) { _hazards.RemoveAt(i); break; }
                }
                if (_hazards.Count >= _maxActive) return null;
            }
            var h = new Hazard
            {
                Id = IdGenerator.NextString("hz"),
                Kind = kind,
                Position = position,
                Radius = radius,
                TimeToLive = ttl,
                TelegraphRemaining = telegraph,
                OwnerPlayerId = owner,
                OwnerImmunity = owner != null ? 1.5f : 0f,
                ZoneDirection = Vec3.Forward
            };
            _hazards.Add(h);
            return h;
        }

        public Hazard SpawnZone(HazardKind kind, Vec3 centre, Vec3 direction, float halfLength, float halfWidth, float ttl, float telegraph)
        {
            var h = Spawn(kind, centre, halfWidth, ttl, telegraph);
            if (h != null)
            {
                h.ZoneDirection = direction.Flat.Normalized;
                h.ZoneHalfLength = halfLength;
            }
            return h;
        }

        /// <summary>Advances motion and timers; returns hazards that expired this tick.</summary>
        public IReadOnlyList<Hazard> Tick(float dt)
        {
            _expired.Clear();
            for (int i = _hazards.Count - 1; i >= 0; i--)
            {
                var h = _hazards[i];
                if (h.TelegraphRemaining > 0f) h.TelegraphRemaining -= dt;
                else h.Position = h.Position + h.Velocity * dt;
                if (h.OwnerImmunity > 0f) h.OwnerImmunity -= dt;
                h.TimeToLive -= dt;
                if (h.TimeToLive <= 0f)
                {
                    _expired.Add(h);
                    _hazards.RemoveAt(i);
                }
            }
            return _expired;
        }

        public Hazard FirstContact(Vec3 point, float radius, string playerId)
        {
            for (int i = 0; i < _hazards.Count; i++)
            {
                var h = _hazards[i];
                if (!h.IsArmed) continue;
                if (h.OwnerPlayerId == playerId && h.OwnerImmunity > 0f) continue;
                if (h.Contains(point, radius)) return h;
            }
            return null;
        }

        public void Remove(Hazard h) => _hazards.Remove(h);
        public void Clear() => _hazards.Clear();
    }
}
