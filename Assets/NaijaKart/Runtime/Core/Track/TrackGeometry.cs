using System;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Track
{
    /// <summary>
    /// Runtime queries over a TrackDefinition's closed centreline: nearest point, distance along the
    /// lap, surface sampling for the driving model and grid placement. Precomputes segment lengths.
    /// </summary>
    public sealed class TrackGeometry
    {
        public readonly TrackDefinition Definition;
        private readonly Vec3[] _points;
        private readonly float[] _cumulative; // cumulative length at start of segment i
        public readonly float LapLength;

        public TrackGeometry(TrackDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _points = definition.centreline;
            if (_points == null || _points.Length < 3)
            {
                throw new ArgumentException("Track centreline needs at least 3 points: " + definition.id);
            }
            _cumulative = new float[_points.Length + 1];
            float total = 0f;
            for (int i = 0; i < _points.Length; i++)
            {
                _cumulative[i] = total;
                total += Vec3.FlatDistance(_points[i], _points[(i + 1) % _points.Length]);
            }
            _cumulative[_points.Length] = total;
            LapLength = total;
        }

        public int SegmentCount => _points.Length;

        public struct Projection
        {
            public int SegmentIndex;
            public float SegmentT;
            public Vec3 Point;
            public float DistanceAlong;
            public float LateralOffset;   // signed, +right of travel direction
            public Vec3 Direction;        // unit travel direction at the projected point
        }

        /// <summary>Projects a world position onto the centreline. O(n) over segments, n is small (&lt; 200).</summary>
        public Projection Project(Vec3 position)
        {
            float bestSqr = float.MaxValue;
            Projection best = default;
            for (int i = 0; i < _points.Length; i++)
            {
                Vec3 a = _points[i];
                Vec3 b = _points[(i + 1) % _points.Length];
                Vec3 ab = (b - a).Flat;
                float len2 = ab.SqrMagnitude;
                float t = len2 > 1e-6f ? MathUtil.Clamp01(Vec3.Dot((position - a).Flat, ab) / len2) : 0f;
                Vec3 p = a + ab * t;
                float d2 = (position - p).Flat.SqrMagnitude;
                if (d2 < bestSqr)
                {
                    bestSqr = d2;
                    Vec3 dir = ab.Normalized;
                    Vec3 right = Vec3.Cross(Vec3.Up, dir);
                    best = new Projection
                    {
                        SegmentIndex = i,
                        SegmentT = t,
                        Point = p,
                        DistanceAlong = _cumulative[i] + (float)System.Math.Sqrt(len2) * t,
                        LateralOffset = Vec3.Dot((position - p).Flat, right),
                        Direction = dir
                    };
                }
            }
            return best;
        }

        /// <summary>World point and direction at a distance along the lap (wraps).</summary>
        public void Sample(float distanceAlong, out Vec3 point, out Vec3 direction)
        {
            float d = distanceAlong % LapLength;
            if (d < 0f) d += LapLength;
            for (int i = 0; i < _points.Length; i++)
            {
                float segLen = _cumulative[i + 1] - _cumulative[i];
                if (d <= _cumulative[i + 1] || i == _points.Length - 1)
                {
                    Vec3 a = _points[i];
                    Vec3 b = _points[(i + 1) % _points.Length];
                    float t = segLen > 1e-6f ? MathUtil.Clamp01((d - _cumulative[i]) / segLen) : 0f;
                    point = Vec3.Lerp(a, b, t);
                    direction = (b - a).Flat.Normalized;
                    return;
                }
            }
            point = _points[0];
            direction = (_points[1] - _points[0]).Flat.Normalized;
        }

        public SurfaceSample SampleSurface(Vec3 position, float extraGripMultiplier, float extraSpeedMultiplier)
        {
            var proj = Project(position);
            float beyond = System.Math.Abs(proj.LateralOffset) - Definition.roadHalfWidth;
            float grip = Definition.baseGripMultiplier, speed = Definition.baseSpeedMultiplier;
            // Shortcut roads: the kart is on-road if inside any of them; the nearest one wins its modifiers.
            var shortcuts = Definition.shortcutRoads;
            if (shortcuts != null)
            {
                for (int i = 0; i < shortcuts.Length; i++)
                {
                    float d = DistanceToOpenPolyline(position, shortcuts[i].points);
                    float b = d - shortcuts[i].halfWidth;
                    if (b < beyond)
                    {
                        beyond = b;
                        if (b <= 0f)
                        {
                            grip = Definition.baseGripMultiplier * shortcuts[i].gripMultiplier;
                            speed = Definition.baseSpeedMultiplier * shortcuts[i].speedMultiplier;
                        }
                    }
                }
            }
            return new SurfaceSample
            {
                OnRoad = beyond <= 0f,
                DistanceBeyondRoadEdge = System.Math.Max(0f, beyond),
                GripMultiplier = grip * extraGripMultiplier,
                SpeedMultiplier = speed * extraSpeedMultiplier
            };
        }

        /// <summary>Flat distance from a point to an open polyline.</summary>
        public static float DistanceToOpenPolyline(Vec3 position, Vec3[] points)
        {
            if (points == null || points.Length == 0) return float.MaxValue;
            if (points.Length == 1) return Vec3.FlatDistance(position, points[0]);
            float best = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vec3 a = points[i];
                Vec3 ab = (points[i + 1] - a).Flat;
                float len2 = ab.SqrMagnitude;
                float t = len2 > 1e-6f ? MathUtil.Clamp01(Vec3.Dot((position - a).Flat, ab) / len2) : 0f;
                float d = (position - (a + ab * t)).FlatMagnitude;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>True if the position is within any road surface (main or shortcut).</summary>
        public bool IsOnAnyRoad(Vec3 position) => SampleSurface(position, 1f, 1f).OnRoad;

        /// <summary>Start grid slot: rows go backwards from the start line, two columns, staggered.</summary>
        public void GridSlot(int slotIndex, float rowSpacing, float columnSpacing, out Vec3 position, out float heading)
        {
            int row = slotIndex / 2;
            int column = slotIndex % 2;
            float back = (row + 1) * rowSpacing + column * rowSpacing * 0.5f;
            Sample(LapLength - back, out Vec3 p, out Vec3 dir);
            Vec3 right = Vec3.Cross(Vec3.Up, dir);
            position = p + right * ((column == 0 ? -0.5f : 0.5f) * columnSpacing);
            heading = dir.ToYaw();
        }
    }
}
