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

        /// <summary>Tapered box: bottom face of size bottom, top face of size top, both centred on the vertical axis (car bodies, roofs, hulls).</summary>
        public void Frustum(MeshBatch m, Vec3 centreBase, Vec3 bottom, Vec3 top, float height, float yaw = 0f, float topOffsetZ = 0f)
        {
            Vec3 P(float x, float y, float z) => centreBase + Rot(new Vec3(x, y, z), yaw);
            float bx = bottom.X * 0.5f, bz = bottom.Z * 0.5f, tx = top.X * 0.5f, tz = top.Z * 0.5f;
            Vec3 a = P(-bx, 0, -bz), b = P(bx, 0, -bz), c = P(bx, 0, bz), d = P(-bx, 0, bz);
            Vec3 e = P(-tx, height, -tz + topOffsetZ), f = P(tx, height, -tz + topOffsetZ), g = P(tx, height, tz + topOffsetZ), h = P(-tx, height, tz + topOffsetZ);
            Quad(m, e, f, g, h);
            Quad(m, d, c, b, a);
            Quad(m, a, b, f, e);
            Quad(m, c, d, h, g);
            Quad(m, b, c, g, f);
            Quad(m, d, a, e, h);
        }

        /// <summary>Low-poly sphere (heads, hair, tree canopies, buoys).</summary>
        public void Sphere(MeshBatch m, Vec3 centre, float radius, int segments = 8, int rings = 6, float yScale = 1f)
        {
            var prev = new Vec3[segments];
            for (int r = 0; r <= rings; r++)
            {
                float phi = MathUtil.Pi * r / rings;
                float y = (float)System.Math.Cos(phi) * radius * yScale, rr = (float)System.Math.Sin(phi) * radius;
                var ring = new Vec3[segments];
                for (int i = 0; i < segments; i++)
                {
                    float a = MathUtil.TwoPi * i / segments;
                    ring[i] = centre + new Vec3((float)System.Math.Cos(a) * rr, y, (float)System.Math.Sin(a) * rr);
                }
                if (r > 0)
                {
                    for (int i = 0; i < segments; i++)
                    {
                        int j = (i + 1) % segments;
                        if (r == 1) Tri(m, prev[0], ring[j], ring[i]);
                        else if (r == rings) Tri(m, prev[i], prev[j], ring[0]);
                        else Quad(m, prev[i], prev[j], ring[j], ring[i]);
                    }
                }
                prev = ring;
            }
        }

        /// <summary>Thin beam between two points (cables, rails, poles at an angle).</summary>
        // ---------------- smooth-shaded primitives (per-vertex normals) ----------------

        private static int AddVert(MeshBatch m, Vec3 p, Vec3 n)
        {
            int i = m.VertexCount;
            m.vertices.Add(p.X); m.vertices.Add(p.Y); m.vertices.Add(p.Z);
            m.normals.Add(n.X); m.normals.Add(n.Y); m.normals.Add(n.Z);
            return i;
        }

        private static void AddTri(MeshBatch m, int a, int b, int c) { m.triangles.Add(a); m.triangles.Add(b); m.triangles.Add(c); }

        /// <summary>Grid points packed towards the ends so a face's rounded border gets its share of segments.</summary>
        private static float Warp(float t) => System.Math.Sign(t) * (float)System.Math.Pow(System.Math.Abs(t), 0.6);

        /// <summary>
        /// Box with every edge and corner rounded by <paramref name="radius"/>, smooth-shaded: car bodies,
        /// canopies, lamps, seats. <paramref name="n"/> grid cells per face side (6 is plenty for a kart part).
        /// </summary>
        public void RoundedBox(MeshBatch m, Vec3 centre, Vec3 size, float radius, int n = 6, float yaw = 0f)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            float r = System.Math.Min(radius, System.Math.Min(hx, System.Math.Min(hy, hz)) * 0.98f);
            float ix = hx - r, iy = hy - r, iz = hz - r;
            n = System.Math.Max(2, n);
            var pos = new Vec3[(n + 1) * (n + 1)];
            var idx = new int[(n + 1) * (n + 1)];
            for (int axis = 0; axis < 3; axis++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vec3 faceN = axis == 0 ? new Vec3(sgn, 0, 0) : axis == 1 ? new Vec3(0, sgn, 0) : new Vec3(0, 0, sgn);
                    for (int i = 0; i <= n; i++)
                        for (int j = 0; j <= n; j++)
                        {
                            float u = Warp(2f * i / n - 1f), v = Warp(2f * j / n - 1f);
                            Vec3 q = axis == 0 ? new Vec3(sgn * hx, u * hy, v * hz) : axis == 1 ? new Vec3(u * hx, sgn * hy, v * hz) : new Vec3(u * hx, v * hy, sgn * hz);
                            Vec3 c = new Vec3(MathUtil.Clamp(q.X, -ix, ix), MathUtil.Clamp(q.Y, -iy, iy), MathUtil.Clamp(q.Z, -iz, iz));
                            Vec3 d = q - c; float len = d.Magnitude;
                            Vec3 nrm = len > 1e-6f ? d / len : faceN;
                            Vec3 p = c + nrm * r;
                            int k = i * (n + 1) + j;
                            pos[k] = p;
                            idx[k] = AddVert(m, centre + Rot(p, yaw), Rot(nrm, yaw));
                        }
                    // Winding: outward = along the face normal.
                    Vec3 a0 = pos[0], b0 = pos[n + 1], c0 = pos[n + 2];
                    bool flip = Vec3.Dot(Vec3.Cross(b0 - a0, c0 - a0), faceN) < 0f;
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                        {
                            int a = idx[i * (n + 1) + j], b = idx[(i + 1) * (n + 1) + j], c = idx[(i + 1) * (n + 1) + j + 1], dd = idx[i * (n + 1) + j + 1];
                            if (flip) { AddTri(m, a, c, b); AddTri(m, a, dd, c); }
                            else { AddTri(m, a, b, c); AddTri(m, a, c, dd); }
                        }
                }
        }

        /// <summary>Round tube between two points with smooth sides and flat caps: roll cages, bull bars, exhausts, rails.</summary>
        public void Tube(MeshBatch m, Vec3 from, Vec3 to, float radius, int segments = 10, bool caps = true)
        {
            Vec3 d = to - from; float len = d.Magnitude; if (len < 1e-4f) return;
            Vec3 axis = d / len;
            Vec3 side = Vec3.Cross(axis, System.Math.Abs(axis.Y) > 0.9f ? new Vec3(1, 0, 0) : Vec3.Up).Normalized;
            Vec3 up = Vec3.Cross(side, axis).Normalized;
            var r0 = new int[segments]; var r1 = new int[segments];
            var ringN = new Vec3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = MathUtil.TwoPi * i / segments;
                ringN[i] = side * (float)System.Math.Cos(a) + up * (float)System.Math.Sin(a);
                r0[i] = AddVert(m, from + ringN[i] * radius, ringN[i]);
                r1[i] = AddVert(m, to + ringN[i] * radius, ringN[i]);
            }
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                AddTri(m, r0[i], r1[i], r1[j]); AddTri(m, r0[i], r1[j], r0[j]);
            }
            if (!caps) return;
            int c0 = AddVert(m, from, axis * -1f), c1 = AddVert(m, to, axis);
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                int a0 = AddVert(m, from + ringN[i] * radius, axis * -1f), b0 = AddVert(m, from + ringN[j] * radius, axis * -1f);
                AddTri(m, c0, b0, a0);
                int a1 = AddVert(m, to + ringN[i] * radius, axis), b1 = AddVert(m, to + ringN[j] * radius, axis);
                AddTri(m, c1, a1, b1);
            }
        }

        /// <summary>Tyre: smooth tread with rounded shoulders and flat sidewalls, axle along local X rotated by yaw.</summary>
        public void Tyre(MeshBatch m, Vec3 centre, float radius, float width, float yaw, int segments = 20)
        {
            float hw = width * 0.5f, sh = System.Math.Min(hw * 0.5f, radius * 0.18f);   // shoulder size
            var px = new[] { -hw, -hw + sh, hw - sh, hw };
            var prr = new[] { radius - sh, radius, radius, radius - sh };
            var pnx = new[] { -0.7f, -0.3f, 0.3f, 0.7f };
            var pnr = new[] { 0.7f, 0.95f, 0.95f, 0.7f };
            int P = px.Length;
            var ring = new int[segments, P];
            for (int i = 0; i < segments; i++)
            {
                float a = MathUtil.TwoPi * i / segments; float cy = (float)System.Math.Cos(a), cz = (float)System.Math.Sin(a);
                for (int k = 0; k < P; k++)
                {
                    Vec3 p = new Vec3(px[k], cy * prr[k], cz * prr[k]);
                    Vec3 nrm = new Vec3(pnx[k], cy * pnr[k], cz * pnr[k]).Normalized;
                    ring[i, k] = AddVert(m, centre + Rot(p, yaw), Rot(nrm, yaw));
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                for (int k = 0; k < P - 1; k++)
                {
                    AddTri(m, ring[i, k], ring[i, k + 1], ring[j, k + 1]); AddTri(m, ring[i, k], ring[j, k + 1], ring[j, k]);
                }
            }
            // Sidewalls: flat fans to the hub
            foreach (int sideK in new[] { 0, P - 1 })
            {
                float x = px[sideK]; Vec3 nrm = Rot(new Vec3(x < 0 ? -1f : 1f, 0, 0), yaw);
                int c = AddVert(m, centre + Rot(new Vec3(x, 0, 0), yaw), nrm);
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    float ai = MathUtil.TwoPi * i / segments, aj = MathUtil.TwoPi * j / segments;
                    int a = AddVert(m, centre + Rot(new Vec3(x, (float)System.Math.Cos(ai) * prr[sideK], (float)System.Math.Sin(ai) * prr[sideK]), yaw), nrm);
                    int b = AddVert(m, centre + Rot(new Vec3(x, (float)System.Math.Cos(aj) * prr[sideK], (float)System.Math.Sin(aj) * prr[sideK]), yaw), nrm);
                    if (x < 0) AddTri(m, c, b, a); else AddTri(m, c, a, b);
                }
            }
        }

        public void Beam(MeshBatch m, Vec3 from, Vec3 to, float thickness)
        {
            Vec3 d = to - from; float len = d.Magnitude; if (len < 1e-4f) return;
            Vec3 axis = d / len;
            Vec3 side = Vec3.Cross(axis, System.Math.Abs(axis.Y) > 0.9f ? new Vec3(1, 0, 0) : Vec3.Up).Normalized * (thickness * 0.5f);
            Vec3 up = Vec3.Cross(side, axis).Normalized * (thickness * 0.5f);
            Vec3 a = from - side - up, b = from + side - up, c = from + side + up, dd = from - side + up;
            Vec3 e = to - side - up, f = to + side - up, g = to + side + up, h = to - side + up;
            Quad(m, a, b, f, e); Quad(m, b, c, g, f); Quad(m, c, dd, h, g); Quad(m, dd, a, e, h);
            Quad(m, dd, c, b, a); Quad(m, e, f, g, h);
        }
    }
}
