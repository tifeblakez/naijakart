using System;
using System.Collections.Generic;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.World
{
    /// <summary>
    /// Appends primitive geometry to MeshBatches keyed by material + colour so a whole city shares a
    /// handful of draw calls. All shapes are flat-shaded boxes, cylinders, cones and ribbons: the
    /// stylised look comes from colour, proportion and composition, not from sculpted art.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly Dictionary<string, MeshBatch> _batches = new Dictionary<string, MeshBatch>();
        private readonly List<MeshBatch> _ordered = new List<MeshBatch>();

        public IReadOnlyList<MeshBatch> Batches => _ordered;

        public MeshBatch Batch(string material, float r, float g, float b, float emissive = 0f, float opacity = 1f)
        {
            string key = $"{material}|{r:0.00}|{g:0.00}|{b:0.00}|{emissive:0.0}|{opacity:0.00}";
            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = new MeshBatch { name = material + "_" + _ordered.Count, material = material, r = r, g = g, b = b, emissive = emissive, opacity = opacity };
                _batches[key] = batch;
                _ordered.Add(batch);
            }
            return batch;
        }

        private static void Quad(MeshBatch m, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            // a,b,c,d counter-clockwise seen from the outside; flat normal.
            Vec3 n = Vec3.Cross(b - a, c - a).Normalized;
            int i = m.VertexCount;
            foreach (var p in new[] { a, b, c, d })
            {
                m.vertices.Add(p.X); m.vertices.Add(p.Y); m.vertices.Add(p.Z);
                m.normals.Add(n.X); m.normals.Add(n.Y); m.normals.Add(n.Z);
            }
            m.triangles.Add(i); m.triangles.Add(i + 1); m.triangles.Add(i + 2);
            m.triangles.Add(i); m.triangles.Add(i + 2); m.triangles.Add(i + 3);
        }

        private static void Tri(MeshBatch m, Vec3 a, Vec3 b, Vec3 c)
        {
            Vec3 n = Vec3.Cross(b - a, c - a).Normalized;
            int i = m.VertexCount;
            foreach (var p in new[] { a, b, c })
            {
                m.vertices.Add(p.X); m.vertices.Add(p.Y); m.vertices.Add(p.Z);
                m.normals.Add(n.X); m.normals.Add(n.Y); m.normals.Add(n.Z);
            }
            m.triangles.Add(i); m.triangles.Add(i + 1); m.triangles.Add(i + 2);
        }

        private static Vec3 Rot(Vec3 local, float yaw)
        {
            float c = (float)System.Math.Cos(yaw), s = (float)System.Math.Sin(yaw);
            return new Vec3(local.X * c + local.Z * s, local.Y, -local.X * s + local.Z * c);
        }

        /// <summary>Axis-aligned box rotated by yaw about its centre.</summary>
        public void Box(MeshBatch m, Vec3 centre, Vec3 size, float yaw = 0f)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            Vec3 P(float x, float y, float z) => centre + Rot(new Vec3(x, y, z), yaw);
            Vec3 a = P(-hx, -hy, -hz), b = P(hx, -hy, -hz), c = P(hx, -hy, hz), d = P(-hx, -hy, hz);
            Vec3 e = P(-hx, hy, -hz), f = P(hx, hy, -hz), g = P(hx, hy, hz), h = P(-hx, hy, hz);
            Quad(m, e, f, g, h);      // top
            Quad(m, d, c, b, a);      // bottom
            Quad(m, a, b, f, e);      // -z
            Quad(m, c, d, h, g);      // +z
            Quad(m, b, c, g, f);      // +x
            Quad(m, d, a, e, h);      // -x
        }

        /// <summary>Vertical cylinder (axis Y) with its base at centreBase.</summary>
        public void Cylinder(MeshBatch m, Vec3 centreBase, float radius, float height, int segments = 10, float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            var ring0 = new Vec3[segments];
            var ring1 = new Vec3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = MathUtil.TwoPi * i / segments;
                float cx = (float)System.Math.Cos(a), cz = (float)System.Math.Sin(a);
                ring0[i] = centreBase + new Vec3(cx * radius, 0f, cz * radius);
                ring1[i] = centreBase + new Vec3(cx * topRadius, height, cz * topRadius);
            }
            Vec3 top = centreBase + new Vec3(0f, height, 0f);
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                Quad(m, ring0[i], ring0[j], ring1[j], ring1[i]);
                Tri(m, ring1[i], ring1[j], top);
                Tri(m, ring0[j], ring0[i], centreBase);
            }
        }

        /// <summary>Horizontal cylinder along the local X axis (wheels), rotated by yaw.</summary>
        public void Wheel(MeshBatch m, Vec3 centre, float radius, float width, float yaw, int segments = 12)
        {
            var ring0 = new Vec3[segments];
            var ring1 = new Vec3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = MathUtil.TwoPi * i / segments;
                float cy = (float)System.Math.Cos(a) * radius, cz = (float)System.Math.Sin(a) * radius;
                ring0[i] = centre + Rot(new Vec3(-width * 0.5f, cy, cz), yaw);
                ring1[i] = centre + Rot(new Vec3(width * 0.5f, cy, cz), yaw);
            }
            Vec3 c0 = centre + Rot(new Vec3(-width * 0.5f, 0f, 0f), yaw);
            Vec3 c1 = centre + Rot(new Vec3(width * 0.5f, 0f, 0f), yaw);
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                Quad(m, ring0[i], ring1[i], ring1[j], ring0[j]);
                Tri(m, ring0[j], c0, ring0[i]);
                Tri(m, ring1[i], c1, ring1[j]);
            }
        }

        /// <summary>Ribbon between left/right edge polylines (road surfaces, rails, water edges).</summary>
        public void Ribbon(MeshBatch m, IList<Vec3> left, IList<Vec3> right, bool closed)
        {
            int n = System.Math.Min(left.Count, right.Count);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int j = (i + 1) % n;
                // left → left(next) → right(next) → right gives an upward normal for a road travelling along the ribbon.
                Quad(m, left[i], left[j], right[j], right[i]);
            }
        }

        /// <summary>Flat horizontal rectangle (ground, water).</summary>
        public void Plane(MeshBatch m, Vec3 centre, float sizeX, float sizeZ)
        {
            float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
            Quad(m, centre + new Vec3(-hx, 0, -hz), centre + new Vec3(-hx, 0, hz), centre + new Vec3(hx, 0, hz), centre + new Vec3(hx, 0, -hz));
        }

        public void Cone(MeshBatch m, Vec3 centreBase, float radius, float height, int segments = 8) =>
            Cylinder(m, centreBase, radius, height, segments, 0.02f);
    }
}
