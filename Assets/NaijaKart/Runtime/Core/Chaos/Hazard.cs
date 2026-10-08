using NaijaKart.Core.Math;

namespace NaijaKart.Core.Chaos
{
    /// <summary>
    /// Kinds of Nigerian road chaos. Crossing hazards move laterally across the road; traffic moves
    /// along it; zones are stretches of road with modified surface; statics sit still.
    /// </summary>
    public enum HazardKind
    {
        OilPatch,
        Pothole,
        DanfoCrossing,
        OkadaCrossing,
        TrafficCar,
        GoSlowZone,
        FloodZone,
        PoliceCheckpoint
    }

    public sealed class Hazard
    {
        public string Id;
        public HazardKind Kind;
        public Vec3 Position;
        public Vec3 Velocity;
        public float Radius;
        /// <summary>For zones: half-length along the track direction (metres).</summary>
        public float ZoneHalfLength;
        public Vec3 ZoneDirection;
        public float TimeToLive;
        /// <summary>While &gt; 0 the hazard is visible/audible but not yet dangerous (readable chaos, PRD §98).</summary>
        public float TelegraphRemaining;
        /// <summary>Player who created it (items), null for road events.</summary>
        public string OwnerPlayerId;
        /// <summary>Owner is immune for a short time so you cannot hit your own oil.</summary>
        public float OwnerImmunity;

        public bool IsArmed => TelegraphRemaining <= 0f;
        public bool IsZone => Kind == HazardKind.GoSlowZone || Kind == HazardKind.FloodZone || Kind == HazardKind.PoliceCheckpoint;
        public bool IsHard => Kind == HazardKind.DanfoCrossing || Kind == HazardKind.OkadaCrossing || Kind == HazardKind.TrafficCar;

        public bool Contains(Vec3 point, float extraRadius)
        {
            if (IsZone)
            {
                Vec3 d = (point - Position).Flat;
                float along = Vec3.Dot(d, ZoneDirection);
                Vec3 lateral = d - ZoneDirection * along;
                return System.Math.Abs(along) <= ZoneHalfLength && lateral.FlatMagnitude <= Radius + extraRadius;
            }
            return Vec3.FlatDistance(point, Position) <= Radius + extraRadius;
        }
    }
}
