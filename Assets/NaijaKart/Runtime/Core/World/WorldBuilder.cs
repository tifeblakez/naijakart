using System;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.World
{
    /// <summary>
    /// Generates the whole visible world of a track from its definition: road, Lagos kerbs, lane
    /// marks, a bridge over the lagoon with pillars, railings and street lights, ground, water, three
    /// building districts, palms, sign gantries, market stalls, item boxes, and mesh templates for
    /// every kart and hazard. Deterministic for a seed. No imported art anywhere (product decision:
    /// the world is code).
    /// </summary>
    public static class WorldBuilder
    {
        // Palette (Lagos daylight)
        static readonly float[] Asphalt = { 0.16f, 0.17f, 0.19f };
        static readonly float[] Concrete = { 0.72f, 0.7f, 0.66f };
        static readonly float[] KerbYellow = { 0.95f, 0.75f, 0.12f };
        static readonly float[] KerbBlack = { 0.08f, 0.08f, 0.09f };
        static readonly float[] LineWhite = { 0.92f, 0.92f, 0.9f };
        static readonly float[] Water = { 0.1f, 0.42f, 0.5f };
        static readonly float[] Ground = { 0.42f, 0.4f, 0.3f };
        static readonly float[] Grass = { 0.3f, 0.48f, 0.22f };
        static readonly float[] Sand = { 0.78f, 0.7f, 0.5f };
        static readonly float[] DanfoYellow = { 0.98f, 0.76f, 0.1f };
        static readonly float[] Rust = { 0.6f, 0.28f, 0.15f };
        static readonly float[] Trunk = { 0.45f, 0.32f, 0.2f };
        static readonly float[] Frond = { 0.2f, 0.55f, 0.25f };
        static readonly float[] Steel = { 0.55f, 0.57f, 0.6f };
        static readonly float[] SignGreen = { 0.05f, 0.45f, 0.22f };

        private sealed class Ctx
        {
            public TrackDefinition Track;
            public TrackGeometry Geo;
            public MeshBuilder M;
            public WorldModel World;
            public DeterministicRandom Rng;
            public List<Vec3> Centre = new List<Vec3>();
            public List<Vec3> Dir = new List<Vec3>();
            public List<Vec3> Right = new List<Vec3>();
            public List<float> Along = new List<float>();
            public float Step = 2.5f;
        }

        public static WorldModel Build(TrackDefinition track, ulong seed = 1)
        {
            var geo = new TrackGeometry(track);
            var ctx = new Ctx { Track = track, Geo = geo, M = new MeshBuilder(), World = new WorldModel { trackId = track.id, displayName = track.displayName, city = track.city }, Rng = new DeterministicRandom(seed) };
            int n = (int)(geo.LapLength / ctx.Step);
            for (int i = 0; i < n; i++)
            {
                float d = geo.LapLength * i / n;
                geo.Sample(d, out Vec3 p, out Vec3 dir);
                ctx.Centre.Add(p); ctx.Dir.Add(dir); ctx.Right.Add(Vec3.Cross(Vec3.Up, dir)); ctx.Along.Add(d);
            }

            BuildGroundAndWater(ctx);
            BuildRoad(ctx);
            BuildShortcuts(ctx);
            BuildBridge(ctx);
            BuildGantries(ctx);
            BuildPalmsAndLights(ctx);
            BuildDistricts(ctx);
            BuildMarket(ctx);
            BuildTemplates(ctx);
            BuildProps(ctx);

            ctx.World.batches.AddRange(ctx.M.Batches);
            return ctx.World;
        }

        private static MeshBatch B(Ctx c, string mat, float[] col, float emissive = 0f, float opacity = 1f) => c.M.Batch(mat, col[0], col[1], col[2], emissive, opacity);

        private static bool IsBridge(Ctx c, int i) => c.Centre[i].Y > 0.4f;

        /// <summary>Lagoon cut: the bridge crosses water between these z values on the outbound straight.</summary>
        private static bool OverWater(Vec3 p) => p.X < 22f && p.Z > 60f && p.Z < 300f;

        private static void BuildGroundAndWater(Ctx c)
        {
            var water = B(c, "water", Water);
            c.M.Plane(water, new Vec3(-300f, c.World.waterLevel, 180f), 1300f, 240f);
            var ground = B(c, "ground", Ground);
            // Ground everywhere except the lagoon rectangle (x < 22, 60 < z < 300).
            c.M.Plane(ground, new Vec3(0f, -0.05f, -170f), 1600f, 460f);      // south block (z < 60)
            c.M.Plane(ground, new Vec3(0f, -0.05f, 650f), 1600f, 700f);       // north block (z > 300)
            c.M.Plane(ground, new Vec3(411f, -0.05f, 180f), 778f, 240f);      // east of the lagoon (x > 22)
            // Sandy shoreline strips and grass verges near the roads
            var sand = B(c, "ground", Sand);
            c.M.Plane(sand, new Vec3(26f, -0.02f, 180f), 8f, 240f);
            c.M.Plane(sand, new Vec3(-300f, -0.02f, 58f), 1300f, 4f);
            c.M.Plane(sand, new Vec3(-300f, -0.02f, 302f), 1300f, 4f);
            var grass = B(c, "ground", Grass);
            for (int i = 0; i < c.Centre.Count; i += 2)
            {
                if (IsBridge(c, i) || OverWater(c.Centre[i])) continue;
                var p = c.Centre[i];
                float hw = c.Track.roadHalfWidth;
                c.M.Box(grass, p + c.Right[i] * (hw + 2.4f) + new Vec3(0, -0.03f, 0), new Vec3(2.8f, 0.04f, c.Step * 2f), c.Dir[i].ToYaw());
                c.M.Box(grass, p - c.Right[i] * (hw + 2.4f) + new Vec3(0, -0.03f, 0), new Vec3(2.8f, 0.04f, c.Step * 2f), c.Dir[i].ToYaw());
            }
        }

        private static void BuildRoad(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var road = B(c, "road", Asphalt);
            var left = new List<Vec3>(); var right = new List<Vec3>();
            for (int i = 0; i < c.Centre.Count; i++) { left.Add(c.Centre[i] - c.Right[i] * hw); right.Add(c.Centre[i] + c.Right[i] * hw); }
            c.M.Ribbon(road, left, right, closed: true);
            // Road thickness (visible on the bridge): side skirts
            var skirt = B(c, "concrete", Concrete);
            var leftDown = new List<Vec3>(); var rightDown = new List<Vec3>();
            for (int i = 0; i < c.Centre.Count; i++) { leftDown.Add(left[i] + new Vec3(0, -0.9f, 0)); rightDown.Add(right[i] + new Vec3(0, -0.9f, 0)); }
            c.M.Ribbon(skirt, leftDown, left, closed: true);
            c.M.Ribbon(skirt, right, rightDown, closed: true);
            c.M.Ribbon(skirt, rightDown, leftDown, closed: true); // underside

            // Edge lines and centre dashes
            var line = B(c, "paint", LineWhite);
            var el = new List<Vec3>(); var er = new List<Vec3>(); var fl = new List<Vec3>(); var fr = new List<Vec3>();
            for (int i = 0; i < c.Centre.Count; i++)
            {
                Vec3 up = new Vec3(0, 0.03f, 0);
                el.Add(c.Centre[i] - c.Right[i] * (hw - 0.5f) + up); er.Add(c.Centre[i] - c.Right[i] * (hw - 0.35f) + up);
                fl.Add(c.Centre[i] + c.Right[i] * (hw - 0.35f) + up); fr.Add(c.Centre[i] + c.Right[i] * (hw - 0.5f) + up);
            }
            c.M.Ribbon(line, el, er, true);
            c.M.Ribbon(line, fl, fr, true);
            for (int i = 0; i < c.Centre.Count; i++)
            {
                if ((int)(c.Along[i] / 3f) % 3 != 0) continue;
                c.M.Box(line, c.Centre[i] + new Vec3(0, 0.03f, 0), new Vec3(0.16f, 0.01f, 2.6f), c.Dir[i].ToYaw());
            }
            // Lane dashes at ±hw/2 (three lanes)
            var lane = B(c, "paint", new[] { 0.8f, 0.8f, 0.75f });
            for (int i = 0; i < c.Centre.Count; i++)
            {
                if ((int)(c.Along[i] / 3f) % 3 != 1) continue;
                c.M.Box(lane, c.Centre[i] + c.Right[i] * (hw * 0.5f) + new Vec3(0, 0.03f, 0), new Vec3(0.12f, 0.01f, 2.2f), c.Dir[i].ToYaw());
                c.M.Box(lane, c.Centre[i] - c.Right[i] * (hw * 0.5f) + new Vec3(0, 0.03f, 0), new Vec3(0.12f, 0.01f, 2.2f), c.Dir[i].ToYaw());
            }

            // Lagos kerbs: alternating yellow/black blocks
            var ky = B(c, "paint", KerbYellow); var kb = B(c, "paint", KerbBlack);
            for (int i = 0; i < c.Centre.Count; i++)
            {
                var batch = (int)(c.Along[i] / 4f) % 2 == 0 ? ky : kb;
                float yaw = c.Dir[i].ToYaw();
                c.M.Box(batch, c.Centre[i] + c.Right[i] * (hw + 0.3f) + new Vec3(0, 0.08f, 0), new Vec3(0.6f, 0.16f, c.Step), yaw);
                c.M.Box(batch, c.Centre[i] - c.Right[i] * (hw + 0.3f) + new Vec3(0, 0.08f, 0), new Vec3(0.6f, 0.16f, c.Step), yaw);
            }
            // Start/finish line checkers
            var white = B(c, "paint", LineWhite); var black = B(c, "paint", KerbBlack);
            geoSample(c, 0f, out Vec3 sp, out Vec3 sd);
            Vec3 sr = Vec3.Cross(Vec3.Up, sd);
            for (int k = -6; k < 6; k++)
            {
                for (int row = 0; row < 2; row++)
                {
                    var batch = (k + row) % 2 == 0 ? white : black;
                    c.M.Box(batch, sp + sr * (k * 1.2f + 0.6f) + sd * (row * 1.2f - 0.6f) + new Vec3(0, 0.035f, 0), new Vec3(1.2f, 0.01f, 1.2f), sd.ToYaw());
                }
            }
        }

        private static void geoSample(Ctx c, float d, out Vec3 p, out Vec3 dir) => c.Geo.Sample(d, out p, out dir);

        private static void BuildShortcuts(Ctx c)
        {
            if (c.Track.shortcutRoads == null) return;
            var concrete = B(c, "concrete", new[] { 0.5f, 0.5f, 0.48f });
            var cone = B(c, "paint", new[] { 1f, 0.45f, 0.05f });
            foreach (var sc in c.Track.shortcutRoads)
            {
                var pts = sc.points;
                var left = new List<Vec3>(); var right = new List<Vec3>();
                for (int i = 0; i < pts.Length; i++)
                {
                    Vec3 prev = pts[System.Math.Max(0, i - 1)], next = pts[System.Math.Min(pts.Length - 1, i + 1)];
                    Vec3 dir = (next - prev).Flat.Normalized; Vec3 r = Vec3.Cross(Vec3.Up, dir);
                    left.Add(pts[i] - r * sc.halfWidth); right.Add(pts[i] + r * sc.halfWidth);
                }
                c.M.Ribbon(concrete, left, right, closed: false);
                // Entrance cones: "faster, but LASTMA dey watch"
                c.M.Cone(cone, pts[0] + new Vec3(0, 0, 0), 0.3f, 0.7f);
                c.M.Cone(cone, pts[pts.Length - 1], 0.3f, 0.7f);
            }
        }

        private static void BuildBridge(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var rail = B(c, "concrete", new[] { 0.85f, 0.85f, 0.82f });
            var post = B(c, "metal", Steel);
            var pillar = B(c, "concrete", new[] { 0.62f, 0.6f, 0.56f });
            float lastPillar = -100f;
            for (int i = 0; i < c.Centre.Count; i++)
            {
                if (!IsBridge(c, i) && !OverWater(c.Centre[i])) continue;
                var p = c.Centre[i]; float yaw = c.Dir[i].ToYaw();
                // Parapets
                c.M.Box(rail, p + c.Right[i] * (hw + 0.85f) + new Vec3(0, 0.55f, 0), new Vec3(0.3f, 1.1f, c.Step), yaw);
                c.M.Box(rail, p - c.Right[i] * (hw + 0.85f) + new Vec3(0, 0.55f, 0), new Vec3(0.3f, 1.1f, c.Step), yaw);
                if ((int)(c.Along[i] / 5f) * 5f <= c.Along[i] && c.Along[i] - (int)(c.Along[i] / 5f) * 5f < c.Step)
                {
                    c.M.Box(post, p + c.Right[i] * (hw + 0.85f) + new Vec3(0, 1.4f, 0), new Vec3(0.12f, 0.6f, 0.12f), yaw);
                    c.M.Box(post, p - c.Right[i] * (hw + 0.85f) + new Vec3(0, 1.4f, 0), new Vec3(0.12f, 0.6f, 0.12f), yaw);
                }
                if (OverWater(p) && c.Along[i] - lastPillar >= 24f)
                {
                    lastPillar = c.Along[i];
                    float h = p.Y - 0.9f - c.World.waterLevel;
                    c.M.Cylinder(pillar, new Vec3(p.X + c.Right[i].X * 3.5f, c.World.waterLevel - 1f, p.Z + c.Right[i].Z * 3.5f), 1.3f, h + 1f, 10);
                    c.M.Cylinder(pillar, new Vec3(p.X - c.Right[i].X * 3.5f, c.World.waterLevel - 1f, p.Z - c.Right[i].Z * 3.5f), 1.3f, h + 1f, 10);
                    c.M.Box(pillar, p + new Vec3(0, p.Y > 1.5f ? p.Y - 1.6f - (p.Y - 1.6f) : -1.3f, 0) * 0f + new Vec3(0, -1.6f, 0) + new Vec3(0, p.Y, 0) * 0f, new Vec3(hw * 2f + 1.5f, 0.9f, 2.2f), yaw);
                }
            }
        }

        private static void BuildGantries(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var steel = B(c, "metal", Steel);
            var green = B(c, "sign", SignGreen);
            var white = B(c, "paint", LineWhite);
            var red = B(c, "paint", new[] { 0.85f, 0.15f, 0.2f });
            var yellow = B(c, "paint", DanfoYellow);
            void Gantry(float along, float[] boardColor, bool checkers)
            {
                geoSample(c, along, out Vec3 p, out Vec3 d); Vec3 r = Vec3.Cross(Vec3.Up, d); float yaw = d.ToYaw();
                float h = 6.5f;
                c.M.Cylinder(steel, p + r * (hw + 1.4f), 0.22f, h, 8);
                c.M.Cylinder(steel, p - r * (hw + 1.4f), 0.22f, h, 8);
                c.M.Box(steel, p + new Vec3(0, h, 0), new Vec3(hw * 2f + 3.4f, 0.45f, 0.45f), yaw);
                var board = c.M.Batch("sign", boardColor[0], boardColor[1], boardColor[2]);
                c.M.Box(board, p + new Vec3(0, h - 1.3f, 0), new Vec3(hw * 1.5f, 2.0f, 0.15f), yaw);
                c.M.Box(white, p + new Vec3(0, h - 1.3f, 0) - d * 0.09f, new Vec3(hw * 1.5f - 0.5f, 0.18f, 0.02f), yaw);
                c.M.Box(white, p + new Vec3(0, h - 0.7f, 0) - d * 0.09f, new Vec3(hw * 1.1f, 0.18f, 0.02f), yaw);
                if (checkers)
                {
                    for (int k = -7; k < 7; k++) c.M.Box((k % 2 == 0) ? white : B(c, "paint", KerbBlack), p + r * (k * 1.0f + 0.5f) + new Vec3(0, h + 0.5f, 0), new Vec3(1f, 0.6f, 0.5f), yaw);
                }
            }
            Gantry(1f, SignGreen, true);                      // start/finish
            Gantry(c.Geo.LapLength * 0.18f, SignGreen, false); // "Third Mainland Bridge → Ikorodu"
            Gantry(c.Geo.LapLength * 0.55f, SignGreen, false);
            // Billboards (Naija Kart style: yellow/red panels on a post)
            foreach (float f in new[] { 0.08f, 0.33f, 0.62f, 0.9f })
            {
                geoSample(c, c.Geo.LapLength * f, out Vec3 p, out Vec3 d); Vec3 r = Vec3.Cross(Vec3.Up, d); float yaw = d.ToYaw();
                if (OverWater(p)) continue;
                Vec3 basePos = p + r * (hw + 6f);
                c.M.Cylinder(steel, basePos, 0.25f, 7f, 8);
                c.M.Box(yellow, basePos + new Vec3(0, 8.5f, 0), new Vec3(0.25f, 3.6f, 7f), yaw);
                c.M.Box(red, basePos + new Vec3(0, 9.4f, 0) - r * 0.14f, new Vec3(0.02f, 1.0f, 6.2f), yaw);
                c.M.Box(white, basePos + new Vec3(0, 7.7f, 0) - r * 0.14f, new Vec3(0.02f, 0.7f, 5.0f), yaw);
            }
        }

        private static void BuildPalmsAndLights(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var trunk = B(c, "trunk", Trunk);
            var frond = B(c, "foliage", Frond);
            var pole = B(c, "metal", Steel);
            var lamp = B(c, "emissive", new[] { 1f, 0.9f, 0.6f }, 1.5f);
            float lastPalm = -100f, lastLight = -100f;
            for (int i = 0; i < c.Centre.Count; i++)
            {
                var p = c.Centre[i];
                bool bridge = IsBridge(c, i) || OverWater(p);
                if (bridge)
                {
                    if (c.Along[i] - lastLight >= 30f)
                    {
                        lastLight = c.Along[i];
                        Vec3 b = p + c.Right[i] * (hw + 0.85f) + new Vec3(0, 1.1f, 0);
                        c.M.Cylinder(pole, b, 0.12f, 7f, 6);
                        c.M.Box(pole, b + new Vec3(0, 7f, 0) - c.Right[i] * 1.5f, new Vec3(3f, 0.12f, 0.12f), c.Dir[i].ToYaw() + MathUtil.Pi * 0.5f);
                        c.M.Box(lamp, b + new Vec3(0, 6.85f, 0) - c.Right[i] * 2.9f, new Vec3(0.6f, 0.2f, 0.35f), c.Dir[i].ToYaw());
                    }
                    continue;
                }
                if (c.Along[i] - lastPalm < 16f) continue;
                lastPalm = c.Along[i];
                if (c.Rng.Chance(0.35f)) continue;
                float side = c.Rng.Chance(0.5f) ? 1f : -1f;
                Vec3 basePos = p + c.Right[i] * (side * (hw + 4.5f + c.Rng.Range(0f, 3f)));
                basePos = new Vec3(basePos.X, -0.05f, basePos.Z);
                Palm(c, trunk, frond, basePos, c.Rng.Range(4.5f, 8f));
            }
        }

        private static void Palm(Ctx c, MeshBatch trunk, MeshBatch frond, Vec3 basePos, float height)
        {
            c.M.Cylinder(trunk, basePos, 0.28f, height, 7, 0.2f);
            Vec3 top = basePos + new Vec3(0, height, 0);
            c.M.Cone(frond, top + new Vec3(0, -0.4f, 0), 2.6f, 1.2f, 8);
            for (int k = 0; k < 6; k++)
            {
                float a = MathUtil.TwoPi * k / 6f + c.Rng.Range(0f, 0.4f);
                Vec3 dir = Vec3.FromYaw(a);
                c.M.Box(frond, top + dir * 1.6f + new Vec3(0, -0.15f, 0), new Vec3(0.55f, 0.08f, 3.2f), a);
            }
        }

        private static float DistanceToRoads(Ctx c, Vec3 p)
        {
            float d = System.Math.Abs(c.Geo.Project(p).LateralOffset);
            if (c.Track.shortcutRoads != null)
                foreach (var sc in c.Track.shortcutRoads) d = System.Math.Min(d, TrackGeometry.DistanceToOpenPolyline(p, sc.points));
            return d;
        }

        private static void BuildDistricts(Ctx c)
        {
            // (a) Lagos Island skyline: tall glass/concrete towers south-west of the lagoon
            District(c, xMin: -260f, xMax: -40f, zMin: -220f, zMax: 50f, cell: 34f, minH: 28f, maxH: 95f, density: 0.75f, tower: true);
            // (b) Mainland mid-rise east of the return road (Ebute Metta / Yaba feel)
            District(c, xMin: 60f, xMax: 260f, zMin: -120f, zMax: 230f, cell: 26f, minH: 7f, maxH: 24f, density: 0.7f, tower: false);
            // (c) Low colourful shops around the ramp and hairpin
            District(c, xMin: 110f, xMax: 340f, zMin: 400f, zMax: 640f, cell: 22f, minH: 4f, maxH: 12f, density: 0.6f, tower: false);
            // (d) Far skyline across the water to the west (silhouette only)
            District(c, xMin: -600f, xMax: -300f, zMin: 100f, zMax: 420f, cell: 40f, minH: 20f, maxH: 70f, density: 0.5f, tower: true);
        }

        private static void District(Ctx c, float xMin, float xMax, float zMin, float zMax, float cell, float minH, float maxH, float density, bool tower)
        {
            float[][] walls = tower
                ? new[] { new[] { 0.55f, 0.65f, 0.75f }, new[] { 0.8f, 0.8f, 0.78f }, new[] { 0.35f, 0.4f, 0.5f }, new[] { 0.6f, 0.58f, 0.52f } }
                : new[] { new[] { 0.9f, 0.84f, 0.68f }, new[] { 0.75f, 0.45f, 0.35f }, new[] { 0.6f, 0.72f, 0.78f }, new[] { 0.85f, 0.7f, 0.5f }, new[] { 0.5f, 0.6f, 0.45f } };
            var glass = B(c, "glass", new[] { 0.45f, 0.6f, 0.72f });
            var roofRust = B(c, "paint", Rust);
            var roofGrey = B(c, "concrete", new[] { 0.5f, 0.5f, 0.5f });
            for (float x = xMin; x < xMax; x += cell)
            {
                for (float z = zMin; z < zMax; z += cell)
                {
                    if (!c.Rng.Chance(density)) continue;
                    Vec3 p = new Vec3(x + c.Rng.Range(-cell * 0.2f, cell * 0.2f), 0f, z + c.Rng.Range(-cell * 0.2f, cell * 0.2f));
                    float w = c.Rng.Range(cell * 0.4f, cell * 0.75f), dep = c.Rng.Range(cell * 0.4f, cell * 0.75f);
                    if (DistanceToRoads(c, p) < c.Track.roadHalfWidth + System.Math.Max(w, dep) * 0.5f + 6f) continue;
                    if (OverWater(p)) continue;
                    float h = c.Rng.Range(minH, maxH);
                    var wall = walls[c.Rng.Range(0, walls.Length)];
                    var batch = B(c, tower && c.Rng.Chance(0.5f) ? "glass" : "concrete", wall);
                    float yaw = c.Rng.Range(-0.08f, 0.08f);
                    c.M.Box(batch, new Vec3(p.X, h * 0.5f, p.Z), new Vec3(w, h, dep), yaw);
                    if (tower)
                    {
                        // Window bands
                        int bands = (int)(h / 3.5f);
                        for (int k = 1; k < bands; k++) c.M.Box(glass, new Vec3(p.X, k * 3.5f, p.Z), new Vec3(w + 0.1f, 1.2f, dep + 0.1f), yaw);
                        if (c.Rng.Chance(0.4f)) c.M.Box(batch, new Vec3(p.X, h + 2f, p.Z), new Vec3(w * 0.5f, 4f, dep * 0.5f), yaw);
                    }
                    else
                    {
                        var roof = c.Rng.Chance(0.6f) ? roofRust : roofGrey;
                        c.M.Box(roof, new Vec3(p.X, h + 0.25f, p.Z), new Vec3(w + 0.6f, 0.5f, dep + 0.6f), yaw);
                        // Shop front colour band + awning
                        var band = B(c, "paint", new[] { c.Rng.Range(0.3f, 1f), c.Rng.Range(0.2f, 0.9f), c.Rng.Range(0.1f, 0.8f) });
                        c.M.Box(band, new Vec3(p.X, 1.8f, p.Z), new Vec3(w + 0.12f, 0.9f, dep + 0.12f), yaw);
                        if (c.Rng.Chance(0.5f))
                        {
                            // Rooftop water tank (very Lagos)
                            c.M.Cylinder(B(c, "paint", KerbBlack), new Vec3(p.X + w * 0.25f, h + 0.5f, p.Z), 0.8f, 1.2f, 8);
                        }
                    }
                }
            }
        }

        private static void BuildMarket(Ctx c)
        {
            // Roadside stalls along the return road and before the ramp: table + coloured umbrella
            var wood = B(c, "trunk", new[] { 0.55f, 0.4f, 0.25f });
            float hw = c.Track.roadHalfWidth;
            int placed = 0;
            for (int i = 0; i < c.Centre.Count && placed < 60; i += 6)
            {
                var p = c.Centre[i];
                if (IsBridge(c, i) || OverWater(p) || !c.Rng.Chance(0.35f)) continue;
                float side = c.Rng.Chance(0.5f) ? 1f : -1f;
                Vec3 b = p + c.Right[i] * (side * (hw + 3.2f));
                b = new Vec3(b.X, 0f, b.Z);
                float yaw = c.Dir[i].ToYaw();
                c.M.Box(wood, b + new Vec3(0, 0.8f, 0), new Vec3(1.2f, 0.1f, 2.0f), yaw);
                c.M.Box(wood, b + new Vec3(0, 0.4f, 0), new Vec3(0.1f, 0.8f, 1.8f), yaw);
                c.M.Cylinder(B(c, "metal", Steel), b, 0.05f, 2.4f, 5);
                var umb = B(c, "paint", new[] { c.Rng.Range(0.5f, 1f), c.Rng.Range(0.1f, 0.8f), c.Rng.Range(0.1f, 0.6f) });
                c.M.Cone(umb, b + new Vec3(0, 2.3f, 0), 1.5f, 0.6f, 8);
                // Goods: small colourful crates
                for (int k = 0; k < 3; k++)
                {
                    var crate = B(c, "paint", new[] { c.Rng.Range(0.2f, 1f), c.Rng.Range(0.2f, 1f), c.Rng.Range(0.2f, 1f) });
                    c.M.Box(crate, b + new Vec3(0, 1.0f, 0) + c.Dir[i] * (k * 0.5f - 0.5f), new Vec3(0.4f, 0.3f, 0.4f), yaw);
                }
                placed++;
            }
        }

        // ---------------- Templates (karts, hazards, props) ----------------

        public static string KartTemplateFor(string vehicleId) => "kart_" + (vehicleId ?? "compact_sedan");

        private static MeshTemplate Template(Ctx c, string name)
        {
            var t = new MeshTemplate { name = name };
            c.World.templates.Add(t);
            return t;
        }

        private static void BuildTemplates(Ctx c)
        {
            foreach (var id in new[] { "danfo", "keke", "okada", "compact_sedan", "executive_sedan", "suv", "sports_car", "food_truck", "generator_kart", "delivery_bike" })
                Kart(c, id);
            Hazards(c);
            var box = Template(c, "item_box");
            var mb = new MeshBuilder();
            mb.Box(mb.Batch("glass", 0.45f, 0.35f, 1f, 0.6f, 0.85f), new Vec3(0, 0, 0), new Vec3(1.3f, 1.3f, 1.3f), 0.6f);
            mb.Box(mb.Batch("emissive", 1f, 0.85f, 0.2f, 1.8f), new Vec3(0, 0, 0), new Vec3(0.55f, 0.9f, 0.55f), 0f);
            box.batches.AddRange(mb.Batches);
        }

        private static void Kart(Ctx c, string id)
        {
            var t = Template(c, "kart_" + id);
            var m = new MeshBuilder();
            var rubber = m.Batch("rubber", 0.06f, 0.06f, 0.07f);
            var rim = m.Batch("metal", 0.75f, 0.75f, 0.72f);
            var chassis = m.Batch("metal", 0.2f, 0.2f, 0.22f);
            var glass = m.Batch("glass", 0.35f, 0.5f, 0.62f);
            var skin = m.Batch("paint", 0.42f, 0.27f, 0.17f);
            var hair = m.Batch("paint", 0.08f, 0.07f, 0.06f);
            var jacket = m.Batch("paint", 0.12f, 0.5f, 0.25f);
            var light = m.Batch("emissive", 1f, 0.95f, 0.7f, 1.2f);
            var tail = m.Batch("emissive", 1f, 0.15f, 0.1f, 1.2f);

            bool bike = id == "okada" || id == "delivery_bike";
            // Chassis plate + wheels
            if (!bike)
            {
                m.Box(chassis, new Vec3(0, 0.32f, 0), new Vec3(1.7f, 0.12f, 2.5f));
                foreach (var w in new[] { new Vec3(-0.85f, 0.36f, 0.95f), new Vec3(0.85f, 0.36f, 0.95f), new Vec3(-0.85f, 0.36f, -0.95f), new Vec3(0.85f, 0.36f, -0.95f) })
                {
                    m.Wheel(rubber, w, 0.36f, 0.32f, 0f, 12);
                    m.Wheel(rim, w, 0.2f, 0.34f, 0f, 8);
                }
            }
            else
            {
                m.Box(chassis, new Vec3(0, 0.45f, 0), new Vec3(0.25f, 0.25f, 1.9f));
                m.Wheel(rubber, new Vec3(0, 0.38f, 0.95f), 0.38f, 0.14f, 0f, 14);
                m.Wheel(rubber, new Vec3(0, 0.38f, -0.85f), 0.38f, 0.16f, 0f, 14);
                m.Wheel(rim, new Vec3(0, 0.38f, 0.95f), 0.2f, 0.16f, 0f, 8);
                m.Wheel(rim, new Vec3(0, 0.38f, -0.85f), 0.2f, 0.18f, 0f, 8);
            }

            switch (id)
            {
                case "danfo":
                {
                    var yellow = m.Batch("paint", DanfoYellow[0], DanfoYellow[1], DanfoYellow[2]);
                    var black = m.Batch("paint", 0.05f, 0.05f, 0.05f);
                    m.Box(yellow, new Vec3(0, 0.9f, -0.15f), new Vec3(1.55f, 1.0f, 2.2f));
                    m.Box(black, new Vec3(0, 0.78f, -0.15f), new Vec3(1.58f, 0.16f, 2.22f));           // the Danfo stripe
                    m.Box(glass, new Vec3(0, 1.15f, 0.96f), new Vec3(1.35f, 0.5f, 0.08f));             // windscreen
                    m.Box(glass, new Vec3(-0.79f, 1.15f, -0.3f), new Vec3(0.06f, 0.42f, 1.5f));
                    m.Box(glass, new Vec3(0.79f, 1.15f, -0.3f), new Vec3(0.06f, 0.42f, 1.5f));
                    m.Box(chassis, new Vec3(0, 1.45f, -0.15f), new Vec3(1.2f, 0.1f, 1.8f));           // roof rack
                    m.Box(light, new Vec3(-0.5f, 0.7f, 1.0f), new Vec3(0.3f, 0.18f, 0.06f)); m.Box(light, new Vec3(0.5f, 0.7f, 1.0f), new Vec3(0.3f, 0.18f, 0.06f));
                    m.Box(tail, new Vec3(-0.55f, 0.7f, -1.26f), new Vec3(0.3f, 0.18f, 0.06f)); m.Box(tail, new Vec3(0.55f, 0.7f, -1.26f), new Vec3(0.3f, 0.18f, 0.06f));
                    Driver(m, skin, hair, jacket, new Vec3(0, 1.45f, 0.1f), open: false);
                    break;
                }
                case "keke":
                {
                    var yellow = m.Batch("paint", DanfoYellow[0], DanfoYellow[1], DanfoYellow[2]);
                    var green = m.Batch("paint", 0.1f, 0.45f, 0.25f);
                    m.Box(yellow, new Vec3(0, 0.8f, -0.1f), new Vec3(1.3f, 0.7f, 2.0f));
                    m.Box(green, new Vec3(0, 1.55f, -0.1f), new Vec3(1.4f, 0.1f, 2.1f));                // canopy
                    m.Box(chassis, new Vec3(-0.6f, 1.2f, -0.9f), new Vec3(0.06f, 0.7f, 0.06f)); m.Box(chassis, new Vec3(0.6f, 1.2f, -0.9f), new Vec3(0.06f, 0.7f, 0.06f));
                    m.Box(chassis, new Vec3(0, 1.2f, 0.85f), new Vec3(0.06f, 0.7f, 0.06f));
                    m.Box(glass, new Vec3(0, 1.2f, 0.9f), new Vec3(1.1f, 0.5f, 0.05f));
                    m.Box(light, new Vec3(0, 0.75f, 1.0f), new Vec3(0.35f, 0.25f, 0.06f));
                    Driver(m, skin, hair, m.Batch("paint", 0.9f, 0.9f, 0.85f), new Vec3(0, 1.15f, 0.2f), open: true);
                    break;
                }
                case "okada":
                case "delivery_bike":
                {
                    var body = m.Batch("paint", id == "okada" ? 0.8f : 0.95f, id == "okada" ? 0.12f : 0.5f, id == "okada" ? 0.1f : 0.05f);
                    m.Box(body, new Vec3(0, 0.75f, 0.1f), new Vec3(0.45f, 0.35f, 1.1f));
                    m.Box(chassis, new Vec3(0, 1.05f, 0.85f), new Vec3(0.7f, 0.06f, 0.06f));           // handlebar
                    m.Box(chassis, new Vec3(0, 0.95f, 0.85f), new Vec3(0.06f, 0.4f, 0.06f));
                    m.Box(light, new Vec3(0, 0.95f, 1.0f), new Vec3(0.2f, 0.2f, 0.08f));
                    if (id == "delivery_bike") m.Box(m.Batch("paint", 0.95f, 0.5f, 0.05f), new Vec3(0, 1.1f, -0.75f), new Vec3(0.7f, 0.6f, 0.6f));
                    Driver(m, skin, hair, id == "okada" ? m.Batch("paint", 0.9f, 0.9f, 0.9f) : m.Batch("paint", 0.95f, 0.5f, 0.05f), new Vec3(0, 1.25f, -0.05f), open: true);
                    break;
                }
                case "suv":
                {
                    var body = m.Batch("paint", 0.12f, 0.3f, 0.2f);
                    m.Box(body, new Vec3(0, 0.85f, -0.05f), new Vec3(1.6f, 0.6f, 2.4f));
                    m.Box(body, new Vec3(0, 1.3f, -0.2f), new Vec3(1.45f, 0.5f, 1.6f));
                    m.Box(glass, new Vec3(0, 1.3f, 0.62f), new Vec3(1.3f, 0.4f, 0.06f));
                    m.Box(chassis, new Vec3(0, 1.58f, -0.2f), new Vec3(1.0f, 0.08f, 1.4f));
                    m.Box(light, new Vec3(-0.5f, 0.85f, 1.21f), new Vec3(0.35f, 0.2f, 0.06f)); m.Box(light, new Vec3(0.5f, 0.85f, 1.21f), new Vec3(0.35f, 0.2f, 0.06f));
                    Driver(m, skin, hair, jacket, new Vec3(0, 1.55f, 0.1f), open: false);
                    break;
                }
                case "food_truck":
                {
                    var body = m.Batch("paint", 0.95f, 0.95f, 0.92f);
                    var orange = m.Batch("paint", 1f, 0.5f, 0.1f);
                    m.Box(body, new Vec3(0, 1.0f, -0.25f), new Vec3(1.6f, 1.2f, 2.0f));
                    m.Box(orange, new Vec3(0, 1.0f, -0.25f), new Vec3(1.62f, 0.3f, 2.02f));
                    m.Box(orange, new Vec3(0.9f, 1.4f, -0.3f), new Vec3(0.4f, 0.05f, 1.2f));             // serving hatch awning
                    m.Box(glass, new Vec3(0, 1.1f, 0.78f), new Vec3(1.3f, 0.5f, 0.06f));
                    Driver(m, skin, hair, m.Batch("paint", 0.9f, 0.2f, 0.2f), new Vec3(0, 1.65f, 0.3f), open: false);
                    break;
                }
                case "generator_kart":
                {
                    var blue = m.Batch("paint", 0.15f, 0.35f, 0.75f);
                    var gen = m.Batch("metal", 0.35f, 0.36f, 0.4f);
                    m.Box(blue, new Vec3(0, 0.7f, 0.3f), new Vec3(1.4f, 0.4f, 1.4f));
                    m.Box(gen, new Vec3(0, 0.95f, -0.75f), new Vec3(1.3f, 0.8f, 0.9f));                 // the generator
                    m.Cylinder(gen, new Vec3(0.45f, 1.35f, -0.75f), 0.08f, 0.6f, 6);                    // exhaust
                    m.Box(light, new Vec3(0, 0.75f, 1.0f), new Vec3(0.5f, 0.15f, 0.06f));
                    Driver(m, skin, hair, m.Batch("paint", 0.9f, 0.85f, 0.2f), new Vec3(0, 1.05f, 0.3f), open: true);
                    break;
                }
                case "sports_car":
                {
                    var red = m.Batch("paint", 0.85f, 0.1f, 0.12f);
                    m.Box(red, new Vec3(0, 0.62f, 0f), new Vec3(1.65f, 0.4f, 2.6f));
                    m.Box(red, new Vec3(0, 0.95f, -0.3f), new Vec3(1.3f, 0.35f, 1.3f));
                    m.Box(glass, new Vec3(0, 0.98f, 0.38f), new Vec3(1.2f, 0.3f, 0.06f));
                    m.Box(red, new Vec3(0, 1.0f, -1.2f), new Vec3(1.5f, 0.08f, 0.3f));                  // spoiler
                    m.Box(light, new Vec3(-0.55f, 0.65f, 1.31f), new Vec3(0.35f, 0.12f, 0.06f)); m.Box(light, new Vec3(0.55f, 0.65f, 1.31f), new Vec3(0.35f, 0.12f, 0.06f));
                    Driver(m, skin, hair, m.Batch("paint", 0.1f, 0.1f, 0.12f), new Vec3(0, 1.2f, 0.0f), open: true);
                    break;
                }
                case "executive_sedan":
                {
                    var black = m.Batch("paint", 0.08f, 0.08f, 0.1f);
                    m.Box(black, new Vec3(0, 0.72f, 0f), new Vec3(1.6f, 0.5f, 2.6f));
                    m.Box(black, new Vec3(0, 1.1f, -0.2f), new Vec3(1.4f, 0.4f, 1.5f));
                    m.Box(glass, new Vec3(0, 1.12f, 0.57f), new Vec3(1.25f, 0.34f, 0.06f));
                    m.Box(light, new Vec3(-0.55f, 0.75f, 1.31f), new Vec3(0.35f, 0.15f, 0.06f)); m.Box(light, new Vec3(0.55f, 0.75f, 1.31f), new Vec3(0.35f, 0.15f, 0.06f));
                    Driver(m, skin, hair, m.Batch("paint", 0.95f, 0.95f, 0.95f), new Vec3(0, 1.4f, 0.05f), open: false);
                    break;
                }
                default: // compact sedan
                {
                    var silver = m.Batch("paint", 0.78f, 0.8f, 0.82f);
                    m.Box(silver, new Vec3(0, 0.7f, 0f), new Vec3(1.55f, 0.45f, 2.4f));
                    m.Box(silver, new Vec3(0, 1.05f, -0.15f), new Vec3(1.35f, 0.38f, 1.4f));
                    m.Box(glass, new Vec3(0, 1.07f, 0.57f), new Vec3(1.2f, 0.32f, 0.06f));
                    m.Box(light, new Vec3(-0.5f, 0.72f, 1.21f), new Vec3(0.32f, 0.15f, 0.06f)); m.Box(light, new Vec3(0.5f, 0.72f, 1.21f), new Vec3(0.32f, 0.15f, 0.06f));
                    Driver(m, skin, hair, jacket, new Vec3(0, 1.35f, 0.05f), open: false);
                    break;
                }
            }
            t.batches.AddRange(m.Batches);
        }

        private static void Driver(MeshBuilder m, MeshBatch skin, MeshBatch hair, MeshBatch jacket, Vec3 seat, bool open)
        {
            // Visible driver (kart games show the character): torso, head, hair, arms to the wheel.
            m.Box(jacket, seat + new Vec3(0, 0.2f, 0), new Vec3(0.55f, 0.5f, 0.35f));
            m.Box(skin, seat + new Vec3(0, 0.62f, 0), new Vec3(0.3f, 0.3f, 0.3f));
            m.Box(hair, seat + new Vec3(0, 0.82f, -0.02f), new Vec3(0.36f, 0.16f, 0.36f));
            m.Box(jacket, seat + new Vec3(-0.28f, 0.3f, 0.2f), new Vec3(0.12f, 0.12f, 0.45f));
            m.Box(jacket, seat + new Vec3(0.28f, 0.3f, 0.2f), new Vec3(0.12f, 0.12f, 0.45f));
            if (open) m.Box(m.Batch("metal", 0.2f, 0.2f, 0.22f), seat + new Vec3(0, 0.3f, 0.45f), new Vec3(0.5f, 0.06f, 0.06f));
        }

        private static void Hazards(Ctx c)
        {
            // Full-size Danfo bus (traffic / crossing)
            {
                var t = Template(c, "danfo_bus"); var m = new MeshBuilder();
                var yellow = m.Batch("paint", DanfoYellow[0], DanfoYellow[1], DanfoYellow[2]);
                var black = m.Batch("paint", 0.05f, 0.05f, 0.05f);
                var glass = m.Batch("glass", 0.35f, 0.5f, 0.62f);
                var rubber = m.Batch("rubber", 0.06f, 0.06f, 0.07f);
                m.Box(yellow, new Vec3(0, 1.3f, 0), new Vec3(2.0f, 1.9f, 4.6f));
                m.Box(black, new Vec3(0, 1.0f, 0), new Vec3(2.04f, 0.25f, 4.64f));
                m.Box(glass, new Vec3(0, 1.7f, 2.31f), new Vec3(1.7f, 0.7f, 0.06f));
                for (int k = -1; k <= 1; k++) { m.Box(glass, new Vec3(-1.02f, 1.7f, k * 1.4f), new Vec3(0.06f, 0.6f, 1.0f)); m.Box(glass, new Vec3(1.02f, 1.7f, k * 1.4f), new Vec3(0.06f, 0.6f, 1.0f)); }
                foreach (var w in new[] { new Vec3(-1f, 0.42f, 1.5f), new Vec3(1f, 0.42f, 1.5f), new Vec3(-1f, 0.42f, -1.5f), new Vec3(1f, 0.42f, -1.5f) }) m.Wheel(rubber, w, 0.42f, 0.3f, 0f, 12);
                m.Box(m.Batch("emissive", 1f, 0.95f, 0.7f, 1.2f), new Vec3(-0.6f, 0.9f, 2.32f), new Vec3(0.4f, 0.25f, 0.06f));
                m.Box(m.Batch("emissive", 1f, 0.95f, 0.7f, 1.2f), new Vec3(0.6f, 0.9f, 2.32f), new Vec3(0.4f, 0.25f, 0.06f));
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "okada_bike"); var m = new MeshBuilder();
                var body = m.Batch("paint", 0.2f, 0.25f, 0.8f); var rubber = m.Batch("rubber", 0.06f, 0.06f, 0.07f);
                m.Box(body, new Vec3(0, 0.75f, 0.1f), new Vec3(0.45f, 0.35f, 1.1f));
                m.Wheel(rubber, new Vec3(0, 0.38f, 0.95f), 0.38f, 0.14f, 0f, 12); m.Wheel(rubber, new Vec3(0, 0.38f, -0.85f), 0.38f, 0.16f, 0f, 12);
                Driver(m, m.Batch("paint", 0.42f, 0.27f, 0.17f), m.Batch("paint", 0.08f, 0.07f, 0.06f), m.Batch("paint", 0.9f, 0.3f, 0.1f), new Vec3(0, 1.25f, -0.05f), true);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "oil_patch"); var m = new MeshBuilder();
                m.Cylinder(m.Batch("glass", 0.05f, 0.05f, 0.08f, 0f, 0.9f), new Vec3(0, 0.01f, 0), 1.0f, 0.03f, 14);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "pothole"); var m = new MeshBuilder();
                m.Cylinder(m.Batch("road", 0.05f, 0.05f, 0.05f), new Vec3(0, 0.005f, 0), 1.1f, 0.02f, 10);
                m.Cylinder(m.Batch("water", 0.25f, 0.22f, 0.15f), new Vec3(0, 0.01f, 0), 0.8f, 0.02f, 10);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "spike_strip"); var m = new MeshBuilder();
                var metal = m.Batch("metal", 0.6f, 0.6f, 0.62f);
                m.Box(metal, new Vec3(0, 0.05f, 0), new Vec3(3.6f, 0.08f, 0.4f));
                for (int k = -6; k <= 6; k++) m.Cone(metal, new Vec3(k * 0.28f, 0.08f, 0), 0.07f, 0.3f, 5);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "cone_row"); var m = new MeshBuilder();
                var orange = m.Batch("paint", 1f, 0.45f, 0.05f);
                for (int k = -2; k <= 2; k++) { m.Cone(orange, new Vec3(k * 2.5f, 0, 0), 0.3f, 0.75f, 8); m.Box(m.Batch("paint", 0.95f, 0.95f, 0.95f), new Vec3(k * 2.5f, 0.4f, 0), new Vec3(0.32f, 0.1f, 0.32f)); }
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "flood"); var m = new MeshBuilder();
                m.Cylinder(m.Batch("water", 0.35f, 0.3f, 0.2f, 0f, 0.75f), new Vec3(0, 0.02f, 0), 7f, 0.04f, 16);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "checkpoint"); var m = new MeshBuilder();
                var white = m.Batch("paint", 0.95f, 0.95f, 0.95f); var red = m.Batch("paint", 0.85f, 0.15f, 0.2f);
                m.Box(white, new Vec3(0, 0.5f, 0), new Vec3(6f, 0.25f, 0.3f));
                m.Box(red, new Vec3(0, 0.5f, 0), new Vec3(2f, 0.27f, 0.32f));
                m.Box(m.Batch("metal", 0.5f, 0.5f, 0.5f), new Vec3(-2.9f, 0.3f, 0), new Vec3(0.1f, 0.6f, 0.4f)); m.Box(m.Batch("metal", 0.5f, 0.5f, 0.5f), new Vec3(2.9f, 0.3f, 0), new Vec3(0.1f, 0.6f, 0.4f));
                t.batches.AddRange(m.Batches);
            }
        }

        private static void BuildProps(Ctx c)
        {
            foreach (var box in c.Track.itemBoxes)
            {
                var p = c.Geo.Project(box.position);
                c.World.props.Add(new PropInstance { template = "item_box", position = new Vec3(box.position.X, p.Point.Y + 1.0f, box.position.Z), yaw = 0f, scale = 1f, label = box.id });
            }
        }
    }
}
