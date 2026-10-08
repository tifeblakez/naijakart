using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Track
{
    /// <summary>
    /// Builds simple procedural tracks (ovals/rounded rectangles) for tests, the driving prototype and
    /// greybox iteration. Real tracks are authored as TrackDefinition JSON / ScriptableObjects.
    /// </summary>
    public static class TrackFactory
    {
        /// <summary>Flat oval: straights of the given length, semicircular ends of the given radius.</summary>
        public static TrackDefinition Oval(string id, float straightLength, float radius, float halfWidth,
            int checkpointCount = 8, int itemBoxRows = 2, float referenceLap = 40f)
        {
            var pts = new List<Vec3>();
            int arcSegments = 12;
            // Start line at origin heading +Z along the right-hand straight (x = +radius).
            for (int i = 0; i <= arcSegments; i++) pts.Add(new Vec3(radius, 0, i * straightLength / arcSegments));
            for (int i = 1; i < arcSegments; i++)
            {
                float a = MathUtil.Pi * i / arcSegments;
                pts.Add(new Vec3((float)System.Math.Cos(a) * radius, 0, straightLength + (float)System.Math.Sin(a) * radius));
            }
            for (int i = 0; i <= arcSegments; i++) pts.Add(new Vec3(-radius, 0, straightLength - i * straightLength / arcSegments));
            for (int i = 1; i < arcSegments; i++)
            {
                float a = MathUtil.Pi + MathUtil.Pi * i / arcSegments;
                pts.Add(new Vec3((float)System.Math.Cos(a) * radius, 0, (float)System.Math.Sin(a) * radius));
            }
            // Rotate so the start/finish line is mid-straight (the grid must sit on a straight) and
            // re-origin so the start line is at z = 0.
            int startIndex = arcSegments / 2;
            var rotated = new List<Vec3>();
            float zOffset = pts[startIndex].Z;
            for (int i = 0; i < pts.Count; i++)
            {
                var q = pts[(startIndex + i) % pts.Count];
                rotated.Add(new Vec3(q.X, q.Y, q.Z - zOffset));
            }
            pts = rotated;
            var def = new TrackDefinition
            {
                id = id,
                displayName = id,
                city = "Test",
                centreline = pts.ToArray(),
                roadHalfWidth = halfWidth,
                referenceLapSeconds = referenceLap
            };
            var geo = new TrackGeometry(def);
            var cps = new List<CheckpointDefinition>();
            for (int c = 0; c < checkpointCount; c++)
            {
                geo.Sample(geo.LapLength * c / checkpointCount, out Vec3 p, out _);
                cps.Add(new CheckpointDefinition
                {
                    id = "cp" + c,
                    gates = new[] { new CheckpointGate { position = p, radius = halfWidth + 2f, label = c == 0 ? "start" : null } }
                });
            }
            def.checkpoints = cps.ToArray();
            // Item box rows live on the straights (where karts are predictable), spread evenly over
            // the two straights. Straight A is split by the start line: [0, L/2] and [lap - L/2, lap].
            var boxes = new List<ItemBoxDefinition>();
            float arcLength = (geo.LapLength - 2f * straightLength) * 0.5f;
            for (int r = 0; r < itemBoxRows; r++)
            {
                float d = 2f * straightLength * (r + 0.5f) / itemBoxRows;
                float along;
                if (d < straightLength * 0.5f) along = d;
                else if (d < straightLength * 1.5f) along = straightLength * 0.5f + arcLength + (d - straightLength * 0.5f);
                else along = geo.LapLength - straightLength * 0.5f + (d - straightLength * 1.5f);
                geo.Sample(along, out Vec3 p, out Vec3 dir);
                Vec3 right = Vec3.Cross(Vec3.Up, dir);
                for (int k = -1; k <= 1; k++)
                {
                    boxes.Add(new ItemBoxDefinition { id = $"box{r}_{k + 1}", position = p + right * (k * halfWidth * 0.6f) });
                }
            }
            def.itemBoxes = boxes.ToArray();
            return def;
        }

        /// <summary>Adds a shortcut gate to a checkpoint: an alternative position off the main line.</summary>
        public static void AddShortcutGate(TrackDefinition def, int checkpointIndex, Vec3 position, float radius)
        {
            var cp = def.checkpoints[checkpointIndex];
            var gates = new List<CheckpointGate>(cp.gates) { new CheckpointGate { position = position, radius = radius, isShortcut = true, label = "shortcut" } };
            cp.gates = gates.ToArray();
        }
    }
}
