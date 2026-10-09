using System;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.World
{
    /// <summary>
    /// Generates the whole visible world of a track from its definition: a three-lane road with Lagos
    /// kerbs and markings, two bridge decks over the lagoon with piers, cable-stay pylons, parapets and
    /// street lights, water, shorelines with palms, crowds and market umbrellas, boats, four building
    /// districts (Lagos Island towers, mainland low-rise, hairpin shops, far skylines), sign gantries,
    /// billboards with Nigerian copy, item boxes, and mesh templates for every kart and hazard.
    /// Deterministic for a seed. No imported art anywhere: the world is code, text and shaders.
    /// Renderers (Unity TrackWorldBuilder, tools/world-preview) map the material hints to PBR shaders
    /// with procedural detail (asphalt grain, concrete stains, tower windows, water waves).
    /// </summary>
    public static class WorldBuilder
    {
        // Environment colour palette from the reference board (Lagos Yellow, Danfo Blue, Road Grey,
        // Sky Blue, Green, Building Beige, Ocean Blue, Night Purple)
        public static readonly float[] LagosYellow = { 0.99f, 0.78f, 0.08f };
        public static readonly float[] DanfoBlue = { 0.23f, 0.42f, 0.76f };
        public static readonly float[] RoadGrey = { 0.45f, 0.47f, 0.52f };
        public static readonly float[] SkyBlue = { 0.55f, 0.78f, 0.95f };
        public static readonly float[] NaijaGreen = { 0.08f, 0.58f, 0.34f };
        public static readonly float[] BuildingBeige = { 0.93f, 0.87f, 0.75f };
        public static readonly float[] OceanBlue = { 0.08f, 0.48f, 0.74f };
        public static readonly float[] NightPurple = { 0.24f, 0.18f, 0.44f };

        // Palette (Lagos daylight)
        static readonly float[] Asphalt = { 0.13f, 0.135f, 0.15f };
        static readonly float[] Concrete = { 0.74f, 0.72f, 0.68f };
        static readonly float[] Barrier = { 0.8f, 0.79f, 0.76f };
        static readonly float[] KerbYellow = { 0.98f, 0.78f, 0.1f };
        static readonly float[] KerbBlack = { 0.07f, 0.07f, 0.08f };
        static readonly float[] LineWhite = { 0.95f, 0.95f, 0.93f };
        static readonly float[] Water = { 0.06f, 0.44f, 0.68f };
        static readonly float[] Ground = { 0.34f, 0.42f, 0.24f };
        static readonly float[] Grass = { 0.28f, 0.5f, 0.2f };
        static readonly float[] Sand = { 0.82f, 0.74f, 0.55f };
        static readonly float[] DanfoYellow = { 0.98f, 0.76f, 0.08f };
        static readonly float[] Rust = { 0.62f, 0.3f, 0.16f };
        static readonly float[] Trunk = { 0.45f, 0.33f, 0.2f };
        static readonly float[] Frond = { 0.18f, 0.55f, 0.22f };
        static readonly float[] Steel = { 0.6f, 0.62f, 0.65f };
        static readonly float[] PylonWhite = { 0.9f, 0.9f, 0.88f };
        static readonly float[] SignGreen = { 0.03f, 0.42f, 0.2f };
        static readonly float[] Chrome = { 0.85f, 0.86f, 0.88f };

        static readonly float[][] SkinTones = { new[] { 0.36f, 0.22f, 0.13f }, new[] { 0.5f, 0.32f, 0.2f }, new[] { 0.27f, 0.16f, 0.1f }, new[] { 0.6f, 0.42f, 0.28f } };
        static readonly float[][] ClothTones = { new[] { 0.95f, 0.85f, 0.2f }, new[] { 0.1f, 0.55f, 0.3f }, new[] { 0.9f, 0.2f, 0.2f }, new[] { 0.95f, 0.95f, 0.95f }, new[] { 0.2f, 0.4f, 0.9f }, new[] { 0.95f, 0.5f, 0.1f }, new[] { 0.6f, 0.2f, 0.7f }, new[] { 0.1f, 0.1f, 0.12f } };
        static readonly string[] ShopNames = { "MAMA PUT", "JOLLOF JUNCTION", "SUYA SPOT", "PURE WATER ₦50", "NO WAHALA TYRES", "GOD'S GRACE BARBING", "CHOP LIFE LOUNGE", "OKADA PARK", "BOLE & FISH", "BLESSING PHONES", "EBA PALACE", "DANFO PARK", "AMALA ZONE", "GENERATOR REPAIRS" };
        static readonly string[][] Billboards = { new[] { "NAIJA KART", "RACE WEEKEND" }, new[] { "LAGOS", "No Stress" }, new[] { "Y PLACE", "LAGOS" }, new[] { "LASTMA DEY WATCH", "DRIVE WELL" }, new[] { "JOLLOF", "NO. 1 IN AFRICA" }, new[] { "PURE WATER", "COLD ONE ₦50" }, new[] { "EKO O NI BAJE", "LAGOS" }, new[] { "THIRD MAINLAND", "RUSH" } };

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
            public List<Vec3> Islands = new List<Vec3>();
        }

        // Lagoon: a band of water across the whole world; both carriageways cross it.
        const float LagoonSouth = 30f, LagoonNorth = 330f;
        const float WaterLevel = -4f;

        public static WorldModel Build(TrackDefinition track, ulong seed = 1, string theme = "day")
        {
            var geo = new TrackGeometry(track);
            bool night = string.Equals(theme, "night", StringComparison.OrdinalIgnoreCase);
            var world = night
                ? new WorldModel
                {
                    trackId = track.id, displayName = track.displayName, city = track.city, theme = "night", isNight = true,
                    waterLevel = WaterLevel,
                    sunDirection = new Vec3(0.3f, -0.6f, -0.5f),
                    skyTop = new[] { 0.02f, 0.02f, 0.1f }, skyHorizon = new[] { NightPurple[0], NightPurple[1], NightPurple[2] },
                    fogColor = new[] { 0.1f, 0.08f, 0.2f }, fogStart = 120f, fogEnd = 900f,
                    ambient = new[] { 0.16f, 0.16f, 0.3f },
                    sunIntensity = 0.45f, sunColor = new[] { 0.6f, 0.7f, 1f }, lampGlow = 3f, windowLitChance = 0.5f,
                }
                : new WorldModel
                {
                    trackId = track.id, displayName = track.displayName, city = track.city, theme = "day", isNight = false,
                    waterLevel = WaterLevel,
                    sunDirection = new Vec3(-0.45f, -0.75f, -0.35f),
                    skyTop = new[] { 0.1f, 0.36f, 0.88f }, skyHorizon = new[] { 0.76f, 0.86f, 0.97f },
                    fogColor = new[] { 0.8f, 0.87f, 0.96f }, fogStart = 200f, fogEnd = 1600f,
                    ambient = new[] { 0.6f, 0.68f, 0.8f },
                    sunIntensity = 4f, sunColor = new[] { 1f, 0.9f, 0.77f }, lampGlow = 0.6f, windowLitChance = 0.14f,
                };
            var ctx = new Ctx { Track = track, Geo = geo, M = new MeshBuilder(), World = world, Rng = new DeterministicRandom(seed) };
            int n = (int)(geo.LapLength / ctx.Step);
            for (int i = 0; i < n; i++)
            {
                float d = geo.LapLength * i / n;
                geo.Sample(d, out Vec3 p, out Vec3 dir);
                ctx.Centre.Add(p); ctx.Dir.Add(dir); ctx.Right.Add(Vec3.Cross(Vec3.Up, dir)); ctx.Along.Add(d);
            }

            for (float d = 0f; d < geo.LapLength; d += 8f) { geo.Sample(d, out Vec3 mp, out _); world.minimap.Add(mp); }

            BuildGroundAndWater(ctx);
            BuildRoad(ctx);
            BuildShortcuts(ctx);
            BuildBridge(ctx);
            BuildGantries(ctx);
            BuildPalmsAndLights(ctx);
            BuildDistricts(ctx);
            BuildShoreLife(ctx);
            BuildBoats(ctx);
            BuildMarket(ctx);
            BuildRoadsideLife(ctx);
            BuildConstruction(ctx);
            BuildGrandstands(ctx);
            BuildTemplates(ctx);
            BuildProps(ctx);

            ctx.World.batches.AddRange(ctx.M.Batches);
            return ctx.World;
        }

        private static MeshBatch B(Ctx c, string mat, float[] col, float emissive = 0f, float opacity = 1f) => c.M.Batch(mat, col[0], col[1], col[2], emissive, opacity);

        private static bool OverWater(Vec3 p) => p.Z > LagoonSouth + 2f && p.Z < LagoonNorth - 2f;
        private static bool InLagoon(Vec3 p) => p.Z > LagoonSouth && p.Z < LagoonNorth;
        private static bool IsBridge(Ctx c, int i) => c.Centre[i].Y > 0.4f || OverWater(c.Centre[i]);

        private static Vec3 Flat(Vec3 p, float y) => new Vec3(p.X, y, p.Z);

        // ---------------- Ground, water ----------------

        private static void BuildGroundAndWater(Ctx c)
        {
            var water = B(c, "water", Water);
            c.M.Plane(water, new Vec3(0f, WaterLevel, (LagoonSouth + LagoonNorth) * 0.5f), 2600f, LagoonNorth - LagoonSouth + 2f);
            var ground = B(c, "ground", Ground);
            c.M.Plane(ground, new Vec3(0f, -0.06f, LagoonSouth * 0.5f - 400f), 2600f, 800f + LagoonSouth);   // south shore block
            c.M.Plane(ground, new Vec3(0f, -0.06f, LagoonNorth + 450f), 2600f, 900f);                       // north shore block
            // Islands across the water (far skylines sit on them)
            var sand = B(c, "sand", Sand);
            foreach (var isl in new[] { new Vec3(-740f, 0, 185f), new Vec3(740f, 0, 185f) })
            {
                c.Islands.Add(isl);
                c.M.Box(ground, Flat(isl, -2.2f), new Vec3(440f, 4.4f, 210f));
                c.M.Box(sand, Flat(isl, -0.03f), new Vec3(448f, 0.12f, 218f));
            }
            // Shorelines: sand then a low sea wall
            c.M.Box(sand, new Vec3(0f, -0.02f, LagoonSouth - 3f), new Vec3(2600f, 0.1f, 7f));
            c.M.Box(sand, new Vec3(0f, -0.02f, LagoonNorth + 3f), new Vec3(2600f, 0.1f, 7f));
            var wall = B(c, "concrete", Concrete);
            c.M.Box(wall, new Vec3(0f, -2f, LagoonSouth), new Vec3(2600f, 4.2f, 1.2f));
            c.M.Box(wall, new Vec3(0f, -2f, LagoonNorth), new Vec3(2600f, 4.2f, 1.2f));
            // Grass verges beside on-land road
            var grass = B(c, "grass", Grass);
            for (int i = 0; i < c.Centre.Count; i += 2)
            {
                if (IsBridge(c, i)) continue;
                var p = c.Centre[i];
                float hw = c.Track.roadHalfWidth;
                c.M.Box(grass, p + c.Right[i] * (hw + 4.5f) + new Vec3(0, -0.04f, 0), new Vec3(7.5f, 0.04f, c.Step * 2f + 0.1f), c.Dir[i].ToYaw());
                c.M.Box(grass, p - c.Right[i] * (hw + 4.5f) + new Vec3(0, -0.04f, 0), new Vec3(7.5f, 0.04f, c.Step * 2f + 0.1f), c.Dir[i].ToYaw());
            }
        }

        // ---------------- Road ----------------

        private static void BuildRoad(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var road = B(c, "road", Asphalt);
            var left = new List<Vec3>(); var right = new List<Vec3>();
            for (int i = 0; i < c.Centre.Count; i++) { left.Add(c.Centre[i] - c.Right[i] * hw); right.Add(c.Centre[i] + c.Right[i] * hw); }
            c.M.Ribbon(road, left, right, closed: true);

            // Deck thickness: side skirts + underside (the bridge is a hollow box girder)
            var skirt = B(c, "concrete", Concrete);
            var leftDown = new List<Vec3>(); var rightDown = new List<Vec3>();
            for (int i = 0; i < c.Centre.Count; i++) { leftDown.Add(left[i] - c.Right[i] * 1.2f + new Vec3(0, -1.6f, 0)); rightDown.Add(right[i] + c.Right[i] * 1.2f + new Vec3(0, -1.6f, 0)); }
            c.M.Ribbon(skirt, leftDown, left, closed: true);
            c.M.Ribbon(skirt, right, rightDown, closed: true);
            c.M.Ribbon(skirt, rightDown, leftDown, closed: true);

            // Edge lines, centre dashes, lane dashes (three lanes)
            var line = B(c, "paint", LineWhite);
            var el = new List<Vec3>(); var er = new List<Vec3>(); var fl = new List<Vec3>(); var fr = new List<Vec3>();
            Vec3 up = new Vec3(0, 0.02f, 0);
            for (int i = 0; i < c.Centre.Count; i++)
            {
                el.Add(c.Centre[i] - c.Right[i] * (hw - 0.55f) + up); er.Add(c.Centre[i] - c.Right[i] * (hw - 0.4f) + up);
                fl.Add(c.Centre[i] + c.Right[i] * (hw - 0.4f) + up); fr.Add(c.Centre[i] + c.Right[i] * (hw - 0.55f) + up);
            }
            c.M.Ribbon(line, el, er, true);
            c.M.Ribbon(line, fl, fr, true);
            var lane = B(c, "paint", new[] { 0.9f, 0.9f, 0.86f });
            for (int i = 0; i < c.Centre.Count; i++)
            {
                int cycle = (int)(c.Along[i] / 2.5f) % 4;
                if (cycle > 1) continue;
                float yaw = c.Dir[i].ToYaw();
                c.M.Box(lane, c.Centre[i] + c.Right[i] * (hw / 3f) + up, new Vec3(0.14f, 0.01f, 2.5f), yaw);
                c.M.Box(lane, c.Centre[i] - c.Right[i] * (hw / 3f) + up, new Vec3(0.14f, 0.01f, 2.5f), yaw);
            }
            // Yellow centre pair on the bridge (Lagos expressway style)
            var yellowLine = B(c, "paint", new[] { 0.95f, 0.8f, 0.2f });
            for (int i = 0; i < c.Centre.Count; i++)
            {
                if (!IsBridge(c, i) || (int)(c.Along[i] / 2.5f) % 3 != 0) continue;
                c.M.Box(yellowLine, c.Centre[i] + up, new Vec3(0.12f, 0.01f, 2.5f), c.Dir[i].ToYaw());
            }

            // Lagos kerbs: alternating yellow/black blocks with a bevel
            var ky = B(c, "paint", KerbYellow); var kb = B(c, "paint", KerbBlack);
            for (int i = 0; i < c.Centre.Count; i++)
            {
                var batch = (int)(c.Along[i] / 4f) % 2 == 0 ? ky : kb;
                float yaw = c.Dir[i].ToYaw();
                c.M.Frustum(batch, c.Centre[i] + c.Right[i] * (hw + 0.3f), new Vec3(0.62f, 0, c.Step + 0.02f), new Vec3(0.45f, 0, c.Step + 0.02f), 0.18f, yaw);
                c.M.Frustum(batch, c.Centre[i] - c.Right[i] * (hw + 0.3f), new Vec3(0.62f, 0, c.Step + 0.02f), new Vec3(0.45f, 0, c.Step + 0.02f), 0.18f, yaw);
            }
            // Start/finish checkers
            var white = B(c, "paint", LineWhite); var black = B(c, "paint", KerbBlack);
            c.Geo.Sample(0f, out Vec3 sp, out Vec3 sd);
            Vec3 sr = Vec3.Cross(Vec3.Up, sd);
            for (int k = -6; k < 6; k++)
                for (int row = 0; row < 2; row++)
                    c.M.Box((k + row) % 2 == 0 ? white : black, sp + sr * (k * 1.2f + 0.6f) + sd * (row * 1.2f - 0.6f) + new Vec3(0, 0.025f, 0), new Vec3(1.2f, 0.01f, 1.2f), sd.ToYaw());
        }

        private static void BuildShortcuts(Ctx c)
        {
            if (c.Track.shortcutRoads == null) return;
            var concrete = B(c, "concrete", new[] { 0.52f, 0.52f, 0.5f });
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
                c.M.Cone(cone, pts[0], 0.3f, 0.7f);
                c.M.Cone(cone, pts[pts.Length - 1], 0.3f, 0.7f);
                var sign = B(c, "sign", new[] { 0.9f, 0.15f, 0.2f });
                Vec3 mid = pts[pts.Length / 2];
                c.M.Cylinder(B(c, "metal", Steel), Flat(mid, -0.1f) + new Vec3(sc.halfWidth + 1f, 0, 0), 0.08f, 3.2f, 6);
                c.World.signs.Add(new SignInstance { style = "banner", text = "SHORTCUT", subText = "LASTMA DEY WATCH", position = Flat(mid, 2.6f) + new Vec3(sc.halfWidth + 1f, 0, 0), yaw = 0f, width = 3.2f, height = 1.1f, background = new[] { 0.9f, 0.15f, 0.2f }, foreground = new[] { 1f, 1f, 1f } });
            }
        }

        // ---------------- Bridge ----------------

        private static void BuildBridge(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var barrier = B(c, "barrier", Barrier);
            var rail = B(c, "metal", Steel);
            var pier = B(c, "concrete", new[] { 0.66f, 0.64f, 0.6f });
            var reflector = B(c, "emissive", new[] { 1f, 0.3f, 0.2f }, 0.8f);
            float lastPier = -100f;
            for (int i = 0; i < c.Centre.Count; i++)
            {
                if (!IsBridge(c, i)) continue;
                var p = c.Centre[i]; float yaw = c.Dir[i].ToYaw();
                // Jersey barriers (tapered) both sides
                c.M.Frustum(barrier, p + c.Right[i] * (hw + 0.95f), new Vec3(0.6f, 0, c.Step + 0.02f), new Vec3(0.25f, 0, c.Step + 0.02f), 0.9f, yaw);
                c.M.Frustum(barrier, p - c.Right[i] * (hw + 0.95f), new Vec3(0.6f, 0, c.Step + 0.02f), new Vec3(0.25f, 0, c.Step + 0.02f), 0.9f, yaw);
                // Guard rail on top
                c.M.Box(rail, p + c.Right[i] * (hw + 0.95f) + new Vec3(0, 1.45f, 0), new Vec3(0.08f, 0.14f, c.Step + 0.02f), yaw);
                c.M.Box(rail, p - c.Right[i] * (hw + 0.95f) + new Vec3(0, 1.45f, 0), new Vec3(0.08f, 0.14f, c.Step + 0.02f), yaw);
                if ((int)(c.Along[i] / 2.5f) % 2 == 0)
                {
                    c.M.Box(rail, p + c.Right[i] * (hw + 0.95f) + new Vec3(0, 1.15f, 0), new Vec3(0.1f, 0.55f, 0.1f), yaw);
                    c.M.Box(rail, p - c.Right[i] * (hw + 0.95f) + new Vec3(0, 1.15f, 0), new Vec3(0.1f, 0.55f, 0.1f), yaw);
                }
                if ((int)(c.Along[i] / 2.5f) % 4 == 0)
                {
                    c.M.Box(reflector, p + c.Right[i] * (hw + 0.72f) + new Vec3(0, 0.6f, 0), new Vec3(0.04f, 0.12f, 0.25f), yaw);
                    c.M.Box(reflector, p - c.Right[i] * (hw + 0.72f) + new Vec3(0, 0.6f, 0), new Vec3(0.04f, 0.12f, 0.25f), yaw);
                }
                // Piers into the lagoon
                if (OverWater(p) && c.Along[i] - lastPier >= 30f)
                {
                    lastPier = c.Along[i];
                    float top = p.Y - 1.6f;   // underside of the deck (absolute height)
                    c.M.Box(pier, Flat(p, top - 0.9f), new Vec3(hw * 2f + 2.4f, 1.8f, 3f), yaw);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vec3 b = Flat(p + c.Right[i] * (s * (hw - 1.2f)), WaterLevel - 2f);
                        c.M.Cylinder(pier, b, 1.5f, top - 1.8f - WaterLevel + 2f, 12);
                        c.M.Cylinder(pier, Flat(b, WaterLevel - 0.3f), 1.9f, 0.6f, 12);   // waterline collar
                    }
                }
            }
            // Cable-stay pylons: one per carriageway
            Pylon(c, 150f, hw);
            Pylon(c, c.Geo.LapLength - 210f, hw);
        }

        private static void Pylon(Ctx c, float along, float hw)
        {
            var pyl = B(c, "concrete", PylonWhite);
            var cable = B(c, "metal", new[] { 0.85f, 0.86f, 0.88f });
            c.Geo.Sample(along, out Vec3 p, out Vec3 d); Vec3 r = Vec3.Cross(Vec3.Up, d); float yaw = d.ToYaw();
            float h = 34f;
            foreach (float s in new[] { -1f, 1f })
            {
                Vec3 b = p + r * (s * (hw + 2.2f)) + new Vec3(0, -1.6f, 0);
                c.M.Frustum(pyl, b, new Vec3(2.2f, 0, 3.2f), new Vec3(1.2f, 0, 2.0f), h + 1.6f, yaw);
                c.M.Box(pyl, b + new Vec3(0, h + 2.2f, 0), new Vec3(1.6f, 1.2f, 2.6f), yaw);
            }
            c.M.Box(pyl, p + new Vec3(0, h - 4f, 0), new Vec3(hw * 2f + 5.6f, 1.6f, 1.8f), yaw);
            c.M.Box(pyl, p + new Vec3(0, 5.5f, 0), new Vec3(hw * 2f + 5.6f, 1.2f, 1.4f), yaw);
            for (int k = 1; k <= 7; k++)
            {
                float dist = k * 9f;
                foreach (float s in new[] { -1f, 1f })
                {
                    Vec3 top = p + r * (s * (hw + 2.2f)) + new Vec3(0, h - 1.5f - k * 1.6f, 0);
                    foreach (float dir in new[] { -1f, 1f })
                    {
                        c.Geo.Sample(along + dir * dist, out Vec3 q, out Vec3 qd);
                        Vec3 anchor = q + Vec3.Cross(Vec3.Up, qd) * (s * (hw + 0.95f)) + new Vec3(0, 1.0f, 0);
                        c.M.Beam(cable, top, anchor, 0.1f);
                    }
                }
            }
            // Pylon beacons
            var beacon = B(c, "emissive", new[] { 1f, 0.2f, 0.15f }, 2f);
            foreach (float s in new[] { -1f, 1f }) c.M.Sphere(beacon, p + r * (s * (hw + 2.2f)) + new Vec3(0, h + 3.1f, 0), 0.3f, 6, 4);
        }

        // ---------------- Gantries & billboards ----------------

        private static void BuildGantries(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var steel = B(c, "metal", Steel);
            var white = B(c, "paint", LineWhite);
            void Gantry(float along, string text, string sub, float[] bg, float[] fg, bool checkers)
            {
                c.Geo.Sample(along, out Vec3 p, out Vec3 d); Vec3 r = Vec3.Cross(Vec3.Up, d); float yaw = d.ToYaw();
                float h = 7f;
                foreach (float s in new[] { -1f, 1f })
                {
                    Vec3 b = p + r * (s * (hw + 1.6f));
                    c.M.Box(steel, b + new Vec3(0, h * 0.5f, 0), new Vec3(0.5f, h, 0.5f), yaw);
                    for (int k = 1; k < 6; k++) c.M.Beam(steel, b + new Vec3(0, k * 1.1f, 0) - d * 0.25f, b + new Vec3(0, k * 1.1f + 0.9f, 0) + d * 0.25f, 0.06f);
                }
                c.M.Box(steel, p + new Vec3(0, h, 0), new Vec3(hw * 2f + 3.8f, 0.7f, 0.7f), yaw);
                c.M.Box(B(c, "sign", bg), p + new Vec3(0, h - 1.55f, 0), new Vec3(hw * 1.6f, 2.6f, 0.18f), yaw);
                c.World.signs.Add(new SignInstance { style = "gantry", text = text, subText = sub, position = p + new Vec3(0, h - 1.55f, 0) - d * 0.1f, yaw = yaw + MathUtil.Pi, width = hw * 1.6f - 0.2f, height = 2.4f, background = bg, foreground = fg });
                if (checkers)
                    for (int k = -7; k < 7; k++) c.M.Box((k % 2 == 0) ? white : B(c, "paint", KerbBlack), p + r * (k * 1.0f + 0.5f) + new Vec3(0, h + 0.75f, 0), new Vec3(1f, 0.8f, 0.6f), yaw);
            }
            Gantry(2f, "NAIJA KART", "THIRD MAINLAND RUSH · LAP 1/3", new[] { 0.9f, 0.14f, 0.2f }, new[] { 1f, 0.95f, 0.3f }, true);
            Gantry(c.Geo.LapLength * 0.1f, "Third Mainland Bridge", "Yaba  ↑        Ikorodu  →", SignGreen, new[] { 1f, 1f, 1f }, false);
            Gantry(c.Geo.LapLength * 0.52f, "Oworonshoki", "Hairpin ahead · Slow down", SignGreen, new[] { 1f, 1f, 1f }, false);
            Gantry(c.Geo.LapLength * 0.86f, "Adekunle", "Lagos Island  ↑", SignGreen, new[] { 1f, 1f, 1f }, false);

            // Billboards on posts: shore heads of the bridge first (the "race weekend" pair), then along the land roads
            int bb = 0;
            void Billboard(Vec3 basePos, float yaw, float scale)
            {
                var copy = Billboards[bb++ % Billboards.Length];
                float w = 9f * scale, h = 4.2f * scale, top = 9f * scale;
                c.M.Cylinder(steel, basePos, 0.3f * scale, top - h * 0.5f, 10);
                var board = B(c, "sign", new[] { 0.98f, 0.8f, 0.1f });
                c.M.Box(board, basePos + new Vec3(0, top, 0), new Vec3(w, h, 0.3f), yaw);
                var frame = B(c, "metal", new[] { 0.2f, 0.2f, 0.22f });
                c.M.Box(frame, basePos + new Vec3(0, top, 0), new Vec3(w + 0.3f, h + 0.3f, 0.2f), yaw);
                Vec3 face = Vec3.FromYaw(yaw);
                c.World.signs.Add(new SignInstance { style = "billboard", text = copy[0], subText = copy[1], position = basePos + new Vec3(0, top, 0) + face * 0.17f, yaw = yaw, width = w - 0.3f, height = h - 0.3f, background = new[] { 0.98f, 0.8f, 0.1f }, foreground = new[] { 0.1f, 0.1f, 0.12f } });
                c.World.signs.Add(new SignInstance { style = "billboard", text = copy[0], subText = copy[1], position = basePos + new Vec3(0, top, 0) - face * 0.17f, yaw = yaw + MathUtil.Pi, width = w - 0.3f, height = h - 0.3f, background = new[] { 0.9f, 0.14f, 0.2f }, foreground = new[] { 1f, 1f, 1f } });
                // Spot lamps on the board
                var lamp = B(c, "metal", Steel);
                for (int k = -1; k <= 1; k++) c.M.Beam(lamp, basePos + new Vec3(0, top + h * 0.5f, 0) + Vec3.Cross(Vec3.Up, face) * (k * w * 0.3f), basePos + new Vec3(0, top + h * 0.5f + 0.7f, 0) + Vec3.Cross(Vec3.Up, face) * (k * w * 0.3f) + face * 0.8f, 0.08f);
            }
            // bridge heads: outbound (x=0) and return (x=34) at both shores
            Billboard(new Vec3(-hw - 9f, 0, LagoonSouth - 10f), MathUtil.Pi * 0.9f, 1.1f);
            Billboard(new Vec3(hw + 20f, 0, LagoonSouth - 12f), MathUtil.Pi * 1.1f, 1.1f);
            Billboard(new Vec3(-hw - 10f, 0, LagoonNorth + 10f), 0.2f, 1.0f);
            Billboard(new Vec3(34f + hw + 10f, 0, LagoonNorth + 12f), -0.2f, 1.0f);
            foreach (float f in new[] { 0.36f, 0.45f, 0.62f, 0.72f, 0.93f })
            {
                c.Geo.Sample(c.Geo.LapLength * f, out Vec3 p, out Vec3 d); Vec3 r = Vec3.Cross(Vec3.Up, d);
                if (OverWater(p)) continue;
                float side = bb % 2 == 0 ? 1f : -1f;
                Billboard(Flat(p + r * (side * (hw + 7f)), 0f), d.ToYaw() + MathUtil.Pi + side * 0.5f, 0.8f);
            }
        }

        // ---------------- Street furniture, palms ----------------

        private static void BuildPalmsAndLights(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var trunk = B(c, "trunk", Trunk);
            var frond = B(c, "foliage", Frond);
            var pole = B(c, "metal", new[] { 0.7f, 0.72f, 0.74f });
            var lamp = B(c, "emissive", new[] { 1f, 0.92f, 0.7f }, 0.6f);
            float lastPalm = -100f, lastLight = -100f;
            for (int i = 0; i < c.Centre.Count; i++)
            {
                var p = c.Centre[i];
                bool bridge = IsBridge(c, i);
                if (c.Along[i] - lastLight >= 28f)
                {
                    lastLight = c.Along[i];
                    float side = ((int)(c.Along[i] / 28f)) % 2 == 0 ? 1f : -1f;
                    if (!bridge) side = 1f;
                    Vec3 b = p + c.Right[i] * (side * (hw + (bridge ? 0.95f : 1.6f))) + new Vec3(0, bridge ? 0.9f : 0f, 0);
                    c.M.Cylinder(pole, b, 0.14f, 8.5f, 8, 0.09f);
                    Vec3 armEnd = b + new Vec3(0, 8.6f, 0) - c.Right[i] * (side * 2.6f);
                    c.M.Beam(pole, b + new Vec3(0, 8.2f, 0), armEnd + new Vec3(0, 0.3f, 0), 0.12f);
                    c.M.Box(lamp, armEnd + new Vec3(0, 0.15f, 0), new Vec3(0.9f, 0.22f, 0.4f), c.Dir[i].ToYaw());
                    c.M.Box(pole, armEnd + new Vec3(0, 0.32f, 0), new Vec3(1.0f, 0.12f, 0.5f), c.Dir[i].ToYaw());
                }
                if (bridge) continue;
                if (c.Along[i] - lastPalm < 14f) continue;
                lastPalm = c.Along[i];
                if (c.Rng.Chance(0.3f)) continue;
                float ps = c.Rng.Chance(0.5f) ? 1f : -1f;
                Vec3 basePos = Flat(p + c.Right[i] * (ps * (hw + 5f + c.Rng.Range(0f, 3f))), -0.05f);
                if (InLagoon(basePos)) continue;
                Palm(c, trunk, frond, basePos, c.Rng.Range(5f, 9f));
            }
        }

        private static void Palm(Ctx c, MeshBatch trunk, MeshBatch frond, Vec3 basePos, float height)
        {
            float lean = c.Rng.Range(-0.1f, 0.1f);
            Vec3 top = basePos + new Vec3(lean * height, height, c.Rng.Range(-0.1f, 0.1f) * height);
            int segs = 5;
            for (int k = 0; k < segs; k++)
            {
                float u0 = k / (float)segs, u1 = (k + 1) / (float)segs;
                Vec3 a = basePos + (top - basePos) * u0 + new Vec3(0, 0, 0), b = basePos + (top - basePos) * u1;
                a = new Vec3(a.X + (float)System.Math.Sin(u0 * 3f) * 0.3f, a.Y, a.Z); b = new Vec3(b.X + (float)System.Math.Sin(u1 * 3f) * 0.3f, b.Y, b.Z);
                c.M.Beam(trunk, a, b, 0.46f - k * 0.06f);
            }
            var frondTip = B(c, "foliage", new[] { 0.45f, 0.72f, 0.3f });
            c.M.Sphere(frond, top + new Vec3(0, -0.1f, 0), 0.6f, 6, 4);
            int fronds = 10;
            for (int k = 0; k < fronds; k++)
            {
                float a = MathUtil.TwoPi * k / fronds + c.Rng.Range(0f, 0.4f);
                Vec3 dir = Vec3.FromYaw(a);
                float len = c.Rng.Range(2.8f, 4.0f);
                float rise = c.Rng.Range(0.3f, 0.9f);
                Vec3 p0 = top + new Vec3(0, 0.1f, 0);
                Vec3 p1 = top + dir * (len * 0.35f) + new Vec3(0, rise, 0);
                Vec3 p2 = top + dir * (len * 0.72f) + new Vec3(0, rise * 0.2f, 0);
                Vec3 p3 = top + dir * len + new Vec3(0, -rise * 1.6f, 0);
                c.M.Beam(frond, p0, p1, 0.55f);
                c.M.Beam(frond, p1, p2, 0.7f);
                c.M.Beam(frondTip, p2, p3, 0.45f);
                // leaflets hanging off the rib
                Vec3 side = Vec3.Cross(Vec3.Up, dir);
                for (int j = 1; j <= 3; j++)
                {
                    float u = j / 4f;
                    Vec3 on = p1 + (p2 - p1) * u;
                    c.M.Beam(frond, on, on + side * 0.55f + new Vec3(0, -0.35f, 0), 0.22f);
                    c.M.Beam(frond, on, on - side * 0.55f + new Vec3(0, -0.35f, 0), 0.22f);
                }
            }
            var nut = c.M.Batch("paint", 0.55f, 0.42f, 0.2f);
            for (int k = 0; k < 3; k++) c.M.Sphere(nut, top + Vec3.FromYaw(k * 2.1f) * 0.45f + new Vec3(0, -0.5f, 0), 0.16f, 5, 3);
        }

        private static float DistanceToRoads(Ctx c, Vec3 p)
        {
            float d = System.Math.Abs(c.Geo.Project(p).LateralOffset);
            if (c.Track.shortcutRoads != null)
                foreach (var sc in c.Track.shortcutRoads) d = System.Math.Min(d, TrackGeometry.DistanceToOpenPolyline(p, sc.points));
            return d;
        }

        // ---------------- Districts ----------------

        private static void BuildDistricts(Ctx c)
        {
            // Lagos Island CBD south of the lagoon, both sides of the start straight
            District(c, -460f, -40f, -340f, 40f, 36f, 30f, 120f, 0.7f, true);
            District(c, 70f, 460f, -340f, 40f, 36f, 24f, 100f, 0.65f, true);
            // Mainland low-rise north of the lagoon (Ebute Metta / Yaba): dense, colourful
            District(c, -460f, -30f, 345f, 720f, 22f, 5f, 18f, 0.8f, false);
            District(c, 120f, 460f, 345f, 720f, 22f, 5f, 18f, 0.75f, false);
            // Tall skyline far behind the mainland (what the bridge points at)
            District(c, -420f, 420f, 740f, 1040f, 42f, 22f, 75f, 0.5f, true);
            // Islands across the water, left and right
            foreach (var isl in c.Islands) District(c, isl.X - 200f, isl.X + 200f, isl.Z - 90f, isl.Z + 90f, 38f, 18f, 90f, 0.6f, true);
        }

        private static void District(Ctx c, float xMin, float xMax, float zMin, float zMax, float cell, float minH, float maxH, float density, bool tower)
        {
            float[][] walls = tower
                ? new[] { new[] { 0.55f, 0.68f, 0.8f }, new[] { 0.85f, 0.86f, 0.84f }, new[] { 0.3f, 0.38f, 0.5f }, new[] { 0.7f, 0.66f, 0.58f }, new[] { 0.42f, 0.55f, 0.6f }, new[] { 0.92f, 0.9f, 0.84f } }
                : new[] { new[] { 0.92f, 0.86f, 0.7f }, new[] { 0.78f, 0.45f, 0.35f }, new[] { 0.62f, 0.75f, 0.8f }, new[] { 0.88f, 0.72f, 0.5f }, new[] { 0.52f, 0.62f, 0.46f }, new[] { 0.95f, 0.94f, 0.9f }, new[] { 0.85f, 0.55f, 0.25f } };
            var roofRust = B(c, "paint", Rust);
            var roofGrey = B(c, "concrete", new[] { 0.52f, 0.52f, 0.52f });
            var steel = B(c, "metal", Steel);
            for (float x = xMin; x < xMax; x += cell)
            {
                for (float z = zMin; z < zMax; z += cell)
                {
                    if (!c.Rng.Chance(density)) continue;
                    Vec3 p = new Vec3(x + c.Rng.Range(-cell * 0.15f, cell * 0.15f), 0f, z + c.Rng.Range(-cell * 0.15f, cell * 0.15f));
                    float w = c.Rng.Range(cell * 0.45f, cell * 0.8f), dep = c.Rng.Range(cell * 0.45f, cell * 0.8f);
                    if (DistanceToRoads(c, p) < c.Track.roadHalfWidth + System.Math.Max(w, dep) * 0.5f + 7f) continue;
                    if (InLagoon(p) && !OnIsland(c, p)) continue;
                    float h = c.Rng.Range(minH, maxH);
                    var wall = walls[c.Rng.Range(0, walls.Length)];
                    if (tower)
                    {
                        bool glassTower = c.Rng.Chance(0.35f);
                        var batch = B(c, "tower", glassTower ? new[] { 0.35f, 0.5f, 0.66f } : wall);
                        int shape = c.Rng.Range(0, 4);
                        if (shape == 0) c.M.Cylinder(batch, p, System.Math.Min(w, dep) * 0.5f, h, 14);
                        else
                        {
                            c.M.Box(batch, new Vec3(p.X, h * 0.5f, p.Z), new Vec3(w, h, dep));
                            if (shape == 2) c.M.Box(batch, new Vec3(p.X, h + h * 0.15f, p.Z), new Vec3(w * 0.6f, h * 0.3f, dep * 0.6f));    // setback crown
                            if (shape == 3) c.M.Box(batch, new Vec3(p.X + w * 0.35f, h * 0.35f, p.Z), new Vec3(w * 0.6f, h * 0.7f, dep * 1.3f)); // podium wing
                        }
                        // roof plant + antenna
                        c.M.Box(roofGrey, new Vec3(p.X, h + (shape == 2 ? h * 0.3f : 0f) + 1f, p.Z), new Vec3(w * 0.35f, 2f, dep * 0.35f));
                        if (c.Rng.Chance(0.45f)) c.M.Cylinder(steel, new Vec3(p.X, h + (shape == 2 ? h * 0.3f : 0f), p.Z), 0.25f, c.Rng.Range(4f, 12f), 6, 0.08f);
                        if (c.Rng.Chance(0.25f))
                        {
                            // crane on construction
                            float ch = h + 14f;
                            c.M.Box(B(c, "paint", new[] { 0.95f, 0.75f, 0.1f }), new Vec3(p.X + w * 0.5f + 3f, ch * 0.5f, p.Z), new Vec3(1.2f, ch, 1.2f));
                            c.M.Box(B(c, "paint", new[] { 0.95f, 0.75f, 0.1f }), new Vec3(p.X + w * 0.5f + 3f + 10f, ch, p.Z), new Vec3(28f, 0.9f, 0.9f));
                        }
                    }
                    else
                    {
                        var batch = B(c, "concrete", wall);
                        float yaw = c.Rng.Range(-0.06f, 0.06f);
                        c.M.Box(batch, new Vec3(p.X, h * 0.5f, p.Z), new Vec3(w, h, dep), yaw);
                        var roof = c.Rng.Chance(0.6f) ? roofRust : roofGrey;
                        c.M.Box(roof, new Vec3(p.X, h + 0.25f, p.Z), new Vec3(w + 0.8f, 0.5f, dep + 0.8f), yaw);
                        // Windows: dark recesses in rows
                        var win = B(c, "glass", new[] { 0.2f, 0.3f, 0.4f });
                        int floors = System.Math.Max(1, (int)(h / 3.2f));
                        for (int f = 0; f < floors; f++)
                            for (int k = -1; k <= 1; k++)
                            {
                                c.M.Box(win, new Vec3(p.X + k * w * 0.3f, f * 3.2f + 2.1f, p.Z - dep * 0.5f - 0.02f), new Vec3(1.2f, 1.1f, 0.05f), yaw);
                                c.M.Box(win, new Vec3(p.X + k * w * 0.3f, f * 3.2f + 2.1f, p.Z + dep * 0.5f + 0.02f), new Vec3(1.2f, 1.1f, 0.05f), yaw);
                            }
                        // Shop front band + awning + sign on the road-facing side
                        var band = B(c, "paint", ClothTones[c.Rng.Range(0, ClothTones.Length)]);
                        c.M.Box(band, new Vec3(p.X, 2.1f, p.Z), new Vec3(w + 0.14f, 1.0f, dep + 0.14f), yaw);
                        var proj = c.Geo.Project(p);
                        Vec3 toRoad = (proj.Point - p).Flat.Normalized;
                        float faceYaw = toRoad.ToYaw();
                        Vec3 face = p + toRoad * (System.Math.Abs(toRoad.Z) > System.Math.Abs(toRoad.X) ? dep * 0.5f : w * 0.5f);
                        if (proj.Point.FlatMagnitude > 0f && DistanceToRoads(c, p) < 60f)
                        {
                            c.World.signs.Add(new SignInstance { style = "shop", text = ShopNames[c.Rng.Range(0, ShopNames.Length)], position = Flat(face, 3.4f) + toRoad * 0.08f, yaw = faceYaw, width = 4.6f, height = 0.9f, background = ClothTones[c.Rng.Range(0, ClothTones.Length)], foreground = new[] { 1f, 1f, 1f } });
                            var awning = B(c, "cloth", ClothTones[c.Rng.Range(0, ClothTones.Length)]);
                            c.M.Frustum(awning, Flat(face, 2.65f) + toRoad * 0.9f, new Vec3(4.8f, 0, 1.8f), new Vec3(4.8f, 0, 0.3f), 0.3f, faceYaw, -0.75f);
                        }
                        if (c.Rng.Chance(0.55f)) c.M.Cylinder(B(c, "paint", KerbBlack), new Vec3(p.X + w * 0.25f, h + 0.5f, p.Z), 0.8f, 1.3f, 8);
                        if (c.Rng.Chance(0.4f)) c.M.Box(B(c, "metal", Steel), new Vec3(p.X - w * 0.25f, h + 1.2f, p.Z + dep * 0.2f), new Vec3(0.08f, 2.4f, 0.08f));   // antenna
                        if (c.Rng.Chance(0.3f)) c.M.Box(B(c, "paint", new[] { 0.2f, 0.4f, 0.9f }), new Vec3(p.X - w * 0.3f, h + 0.4f, p.Z - dep * 0.25f), new Vec3(1.1f, 0.8f, 1.1f)); // blue tank
                    }
                }
            }
        }

        private static bool OnIsland(Ctx c, Vec3 p)
        {
            foreach (var isl in c.Islands) if (System.Math.Abs(p.X - isl.X) < 215f && System.Math.Abs(p.Z - isl.Z) < 100f) return true;
            return false;
        }

        // ---------------- Shore life, boats, crowds ----------------

        private static void BuildShoreLife(Ctx c)
        {
            var trunk = B(c, "trunk", Trunk);
            var frond = B(c, "foliage", Frond);
            // Palms along both shores, umbrellas and people near the bridge heads
            foreach (float z in new[] { LagoonSouth - 7f, LagoonNorth + 7f })
            {
                for (float x = -520f; x <= 520f; x += 11f)
                {
                    if (System.Math.Abs(x) < 11f || System.Math.Abs(x - 34f) < 11f) continue;    // bridge corridors
                    if (c.Rng.Chance(0.35f)) continue;
                    Palm(c, trunk, frond, new Vec3(x + c.Rng.Range(-3f, 3f), -0.05f, z + c.Rng.Range(-3f, 3f)), c.Rng.Range(5f, 9.5f));
                }
                for (float x = -120f; x <= 160f; x += 4f)
                {
                    if (System.Math.Abs(x) < 10.5f || System.Math.Abs(x - 34f) < 10.5f) continue;
                    if (c.Rng.Chance(0.45f))
                    {
                        Vec3 b = new Vec3(x + c.Rng.Range(-1.5f, 1.5f), 0f, z + (z < 100f ? -1f : 1f) * c.Rng.Range(1f, 5f));
                        Umbrella(c, b);
                    }
                    for (int k = 0; k < 3; k++)
                        if (c.Rng.Chance(0.6f)) Person(c, new Vec3(x + c.Rng.Range(-2f, 2f), 0f, z + (z < 100f ? -1f : 1f) * c.Rng.Range(0.5f, 7f)), c.Rng.Range(0f, MathUtil.TwoPi));
                }
            }
            // Jetties with canoes
            var wood = B(c, "trunk", new[] { 0.5f, 0.36f, 0.22f });
            foreach (float x in new[] { -70f, 95f, -140f, 160f })
            {
                float z0 = c.Rng.Chance(0.5f) ? LagoonSouth : LagoonNorth;
                float dir = z0 < 100f ? 1f : -1f;
                c.M.Box(wood, new Vec3(x, WaterLevel + 1.5f, z0 + dir * 12f), new Vec3(2.4f, 0.25f, 24f));
                for (int k = 0; k < 5; k++) { c.M.Cylinder(wood, new Vec3(x - 1.1f, WaterLevel - 1f, z0 + dir * (3f + k * 5f)), 0.16f, 3.2f, 6); c.M.Cylinder(wood, new Vec3(x + 1.1f, WaterLevel - 1f, z0 + dir * (3f + k * 5f)), 0.16f, 3.2f, 6); }
            }
        }

        private static void Umbrella(Ctx c, Vec3 b)
        {
            c.M.Cylinder(B(c, "metal", Steel), b, 0.05f, 2.5f, 5);
            var col = ClothTones[c.Rng.Range(0, ClothTones.Length)];
            c.M.Cone(B(c, "cloth", col), b + new Vec3(0, 2.2f, 0), 1.7f, 0.7f, 10);
            c.M.Box(B(c, "trunk", new[] { 0.5f, 0.36f, 0.22f }), b + new Vec3(0, 0.75f, 0), new Vec3(1.1f, 0.08f, 1.1f));
        }

        private static void Person(Ctx c, Vec3 b, float yaw)
        {
            var skin = B(c, "skin", SkinTones[c.Rng.Range(0, SkinTones.Length)]);
            var shirt = B(c, "cloth", ClothTones[c.Rng.Range(0, ClothTones.Length)]);
            var trousers = B(c, "cloth", c.Rng.Chance(0.5f) ? new[] { 0.15f, 0.2f, 0.4f } : new[] { 0.1f, 0.1f, 0.1f });
            var hair = B(c, "paint", new[] { 0.06f, 0.05f, 0.05f });
            float h = c.Rng.Range(1.6f, 1.85f);
            c.M.Box(trousers, b + new Vec3(0, h * 0.25f, 0), new Vec3(0.36f, h * 0.5f, 0.22f), yaw);
            c.M.Frustum(shirt, b + new Vec3(0, h * 0.5f, 0), new Vec3(0.4f, 0, 0.24f), new Vec3(0.5f, 0, 0.28f), h * 0.33f, yaw);
            c.M.Sphere(skin, b + new Vec3(0, h * 0.9f, 0), 0.13f, 7, 5, 1.15f);
            if (c.Rng.Chance(0.5f)) c.M.Sphere(hair, b + new Vec3(0, h * 0.93f, 0), 0.15f, 7, 5, 0.8f);
            else c.M.Box(B(c, "cloth", ClothTones[c.Rng.Range(0, ClothTones.Length)]), b + new Vec3(0, h * 0.97f, 0), new Vec3(0.26f, 0.08f, 0.26f), yaw);
            // arms (one may be raised: cheering)
            bool cheer = c.Rng.Chance(0.4f);
            c.M.Beam(skin, b + new Vec3(0, h * 0.78f, 0) + Vec3.Cross(Vec3.Up, Vec3.FromYaw(yaw)) * 0.3f, b + new Vec3(0, cheer ? h * 1.05f : h * 0.5f, 0) + Vec3.Cross(Vec3.Up, Vec3.FromYaw(yaw)) * 0.36f, 0.09f);
            c.M.Beam(skin, b + new Vec3(0, h * 0.78f, 0) - Vec3.Cross(Vec3.Up, Vec3.FromYaw(yaw)) * 0.3f, b + new Vec3(0, h * 0.5f, 0) - Vec3.Cross(Vec3.Up, Vec3.FromYaw(yaw)) * 0.36f, 0.09f);
        }

        private static void BuildBoats(Ctx c)
        {
            int placed = 0, tries = 0;
            while (placed < 26 && tries++ < 400)
            {
                Vec3 p = new Vec3(c.Rng.Range(-600f, 640f), WaterLevel, c.Rng.Range(LagoonSouth + 14f, LagoonNorth - 14f));
                if (System.Math.Abs(p.X) < 16f || System.Math.Abs(p.X - 34f) < 16f || OnIsland(c, p) || DistanceToRoads(c, p) < 22f) continue;
                float yaw = c.Rng.Range(0f, MathUtil.TwoPi);
                var hull = B(c, "paint", ClothTones[c.Rng.Range(0, ClothTones.Length)]);
                var white = B(c, "paint", LineWhite);
                float len = c.Rng.Range(6f, 11f);
                c.M.Frustum(hull, p + new Vec3(0, -0.5f, 0), new Vec3(1.4f, 0, len * 0.7f), new Vec3(2.4f, 0, len), 1.1f, yaw);
                c.M.Frustum(white, p + new Vec3(0, 0.6f, 0), new Vec3(2.4f, 0, len), new Vec3(2.1f, 0, len - 0.6f), 0.15f, yaw);
                if (c.Rng.Chance(0.6f))
                {
                    var canopy = B(c, "cloth", ClothTones[c.Rng.Range(0, ClothTones.Length)]);
                    foreach (float s in new[] { -1f, 1f }) c.M.Cylinder(B(c, "metal", Steel), p + MeshRot(new Vec3(s * 0.9f, 0.7f, 0f), yaw), 0.05f, 1.8f, 5);
                    c.M.Box(canopy, p + new Vec3(0, 2.5f, 0), new Vec3(2.2f, 0.1f, len * 0.5f), yaw);
                }
                if (c.Rng.Chance(0.5f)) c.M.Cylinder(B(c, "metal", new[] { 0.3f, 0.3f, 0.32f }), p + MeshRot(new Vec3(0, 0.6f, -len * 0.45f), yaw), 0.18f, 0.9f, 6);
                for (int k = 0; k < c.Rng.Range(1, 4); k++) Person(c, p + MeshRot(new Vec3(c.Rng.Range(-0.5f, 0.5f), 0.7f, c.Rng.Range(-len * 0.35f, len * 0.35f)), yaw), yaw + c.Rng.Range(-0.5f, 0.5f));
                placed++;
            }
            // Buoys
            var buoy = B(c, "paint", new[] { 1f, 0.35f, 0.1f });
            for (int k = 0; k < 14; k++) c.M.Sphere(buoy, new Vec3(c.Rng.Range(-500f, 540f), WaterLevel + 0.2f, c.Rng.Range(LagoonSouth + 8f, LagoonNorth - 8f)), 0.5f, 7, 5);
        }

        private static Vec3 MeshRot(Vec3 local, float yaw)
        {
            float cs = (float)System.Math.Cos(yaw), sn = (float)System.Math.Sin(yaw);
            return new Vec3(local.X * cs + local.Z * sn, local.Y, -local.X * sn + local.Z * cs);
        }

        private static void BuildMarket(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            int placed = 0;
            for (int i = 0; i < c.Centre.Count && placed < 70; i += 5)
            {
                var p = c.Centre[i];
                if (IsBridge(c, i) || !c.Rng.Chance(0.4f)) continue;
                float side = c.Rng.Chance(0.5f) ? 1f : -1f;
                Vec3 b = Flat(p + c.Right[i] * (side * (hw + 3.6f)), 0f);
                if (InLagoon(b)) continue;
                float yaw = c.Dir[i].ToYaw();
                var wood = B(c, "trunk", new[] { 0.55f, 0.4f, 0.25f });
                c.M.Box(wood, b + new Vec3(0, 0.8f, 0), new Vec3(1.2f, 0.1f, 2.0f), yaw);
                c.M.Box(wood, b + new Vec3(0, 0.4f, 0), new Vec3(0.1f, 0.8f, 1.8f), yaw);
                Umbrella(c, b);
                for (int k = 0; k < 3; k++)
                    c.M.Box(B(c, "paint", ClothTones[c.Rng.Range(0, ClothTones.Length)]), b + new Vec3(0, 1.0f, 0) + c.Dir[i] * (k * 0.5f - 0.5f), new Vec3(0.4f, 0.3f, 0.4f), yaw);
                Person(c, b + c.Right[i] * (side * 1.2f), yaw + MathUtil.Pi * 0.5f * -side);
                if (c.Rng.Chance(0.5f)) Person(c, b - c.Dir[i] * 1.6f, yaw + c.Rng.Range(-1f, 1f));
                placed++;
            }
        }

        private static void BuildRoadsideLife(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var pole = B(c, "trunk", new[] { 0.4f, 0.3f, 0.2f });
            var wire = B(c, "metal", new[] { 0.12f, 0.12f, 0.13f });
            var trunk = B(c, "trunk", Trunk);
            var canopy = B(c, "foliage", new[] { 0.16f, 0.45f, 0.18f });
            var canopy2 = B(c, "foliage", new[] { 0.3f, 0.55f, 0.2f });
            var shelter = B(c, "paint", new[] { 0.1f, 0.5f, 0.3f });
            var steel = B(c, "metal", Steel);
            Vec3 prevWire = Vec3.Zero; bool hasPrev = false;
            float lastTree = -100f, lastStop = -200f, lastPark = -150f;
            for (int i = 0; i < c.Centre.Count; i++)
            {
                if (IsBridge(c, i)) { hasPrev = false; continue; }
                var p = c.Centre[i]; float yaw = c.Dir[i].ToYaw(); float along = c.Along[i];
                // Power poles with sagging wires on the left side every 28 m
                if ((int)(along / 28f) * 28f <= along && along - (int)(along / 28f) * 28f < c.Step)
                {
                    Vec3 b = Flat(p - c.Right[i] * (hw + 2.6f), 0f);
                    if (!InLagoon(b))
                    {
                        c.M.Cylinder(pole, b, 0.14f, 8f, 6, 0.1f);
                        c.M.Box(pole, b + new Vec3(0, 7.6f, 0), new Vec3(1.4f, 0.1f, 0.1f), yaw);
                        Vec3 top = b + new Vec3(0, 7.9f, 0);
                        if (hasPrev && (top - prevWire).Magnitude < 40f)
                        {
                            Vec3 mid = (top + prevWire) * 0.5f + new Vec3(0, -0.9f, 0);
                            foreach (float off in new[] { -0.6f, 0.6f })
                            {
                                Vec3 o = c.Right[i] * off;
                                c.M.Beam(wire, prevWire + o, mid + o, 0.05f); c.M.Beam(wire, mid + o, top + o, 0.05f);
                            }
                        }
                        prevWire = top; hasPrev = true;
                    }
                }
                // Broad trees (almond / mango) on the right every ~20 m
                if (along - lastTree > 20f && c.Rng.Chance(0.6f))
                {
                    lastTree = along;
                    Vec3 b = Flat(p + c.Right[i] * (hw + 6f + c.Rng.Range(0f, 5f)), 0f);
                    if (!InLagoon(b) && DistanceToRoads(c, b) > hw + 3f)
                    {
                        float h = c.Rng.Range(4f, 7f);
                        c.M.Cylinder(trunk, b, 0.35f, h, 7, 0.25f);
                        var cv = c.Rng.Chance(0.5f) ? canopy : canopy2;
                        c.M.Sphere(cv, b + new Vec3(0, h + 1.2f, 0), c.Rng.Range(2.6f, 4f), 9, 6, 0.75f);
                        c.M.Sphere(cv, b + new Vec3(1.4f, h + 0.4f, 0.8f), c.Rng.Range(1.6f, 2.4f), 8, 5, 0.8f);
                        c.M.Sphere(cv, b + new Vec3(-1.2f, h + 0.6f, -0.9f), c.Rng.Range(1.6f, 2.4f), 8, 5, 0.8f);
                    }
                }
                // Bus stops with waiting passengers every ~140 m
                if (along - lastStop > 140f)
                {
                    lastStop = along;
                    Vec3 b = Flat(p + c.Right[i] * (hw + 2.4f), 0f);
                    if (!InLagoon(b))
                    {
                        c.M.Box(shelter, b + new Vec3(0, 2.6f, 0), new Vec3(2.2f, 0.12f, 5f), yaw);
                        foreach (float dz in new[] { -2.3f, 2.3f }) c.M.Box(steel, b + c.Dir[i] * dz + c.Right[i] * 0.9f + new Vec3(0, 1.3f, 0), new Vec3(0.08f, 2.6f, 0.08f), yaw);
                        c.M.Box(steel, b + c.Right[i] * 0.9f + new Vec3(0, 0.5f, 0), new Vec3(0.4f, 0.08f, 4f), yaw);
                        c.World.signs.Add(new SignInstance { style = "banner", text = "BRT STOP", position = b + new Vec3(0, 2.25f, 0) - c.Right[i] * 1.12f, yaw = yaw - MathUtil.Pi * 0.5f, width = 2.4f, height = 0.5f, background = new[] { 0.05f, 0.3f, 0.7f }, foreground = new[] { 1f, 1f, 1f } });
                        for (int k = 0; k < 3; k++) Person(c, b + c.Dir[i] * (k * 1.3f - 1.3f) + c.Right[i] * 0.4f, yaw - MathUtil.Pi * 0.5f + c.Rng.Range(-0.5f, 0.5f));
                    }
                }
                // Parked danfos and okadas every ~90 m (left side)
                if (along - lastPark > 90f)
                {
                    lastPark = along;
                    Vec3 b = Flat(p - c.Right[i] * (hw + 4.2f), 0f);
                    if (!InLagoon(b) && DistanceToRoads(c, b) > hw + 2.5f)
                    {
                        c.World.props.Add(new PropInstance { template = c.Rng.Chance(0.6f) ? "danfo_bus" : "okada_bike", position = b, yaw = yaw + c.Rng.Range(-0.2f, 0.2f), scale = 1f, label = "parked" });
                        for (int k = 0; k < 2; k++) Person(c, b - c.Right[i] * 1.8f + c.Dir[i] * (k * 1.5f - 2f), yaw + c.Rng.Range(0f, MathUtil.TwoPi));
                    }
                }
            }
        }

        /// <summary>Road diversion at the construction chicane: striped barriers, cones, chevrons, a crane and pipes.</summary>
        private static void BuildConstruction(Ctx c)
        {
            float hw = c.Track.roadHalfWidth;
            var red = B(c, "paint", new[] { 0.9f, 0.12f, 0.15f });
            var white = B(c, "paint", LineWhite);
            var orange = B(c, "paint", new[] { 1f, 0.45f, 0.05f });
            var yellow = B(c, "paint", LagosYellow);
            var steel = B(c, "metal", Steel);
            var concrete = B(c, "concrete", new[] { 0.6f, 0.6f, 0.58f });
            float start = 392f, end = 500f;
            int n = 0;
            for (int i = 0; i < c.Centre.Count; i++)
            {
                float along = c.Along[i];
                if (along < start || along > end) continue;
                var p = c.Centre[i]; float yaw = c.Dir[i].ToYaw();
                if (n++ % 2 == 0)
                {
                    // striped barriers on both verges
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vec3 b = p + c.Right[i] * (side * (hw + 1.0f));
                        c.M.Box((n / 2) % 2 == 0 ? red : white, b + new Vec3(0, 0.55f, 0), new Vec3(0.3f, 0.5f, c.Step * 2f), yaw);
                        c.M.Box(steel, b + new Vec3(0, 0.15f, 0), new Vec3(0.5f, 0.3f, 0.3f), yaw);
                    }
                }
                // cones along the inside line of the chicane
                if (n % 3 == 0)
                {
                    float side = along < 445f ? -1f : 1f;
                    Vec3 b = p + c.Right[i] * (side * (hw - 1.2f));
                    c.M.Cone(orange, b, 0.28f, 0.75f, 8);
                    c.M.Box(white, b + new Vec3(0, 0.4f, 0), new Vec3(0.3f, 0.08f, 0.3f));
                }
                // flashing warning lamps on the barriers
                if (n % 6 == 0) c.M.Sphere(B(c, "emissive", new[] { 1f, 0.6f, 0.1f }, 2f), p + c.Right[i] * (hw + 1.0f) + new Vec3(0, 1.0f, 0), 0.12f, 6, 4);
            }
            // chevron and diversion signs at the entry
            c.Geo.Sample(start - 12f, out Vec3 e, out Vec3 ed); Vec3 er = Vec3.Cross(Vec3.Up, ed); float eyaw = ed.ToYaw();
            foreach (float side in new[] { -1f, 1f })
            {
                Vec3 b = Flat(e + er * (side * (hw + 2.5f)), 0f);
                c.M.Cylinder(steel, b, 0.08f, 2.4f, 6);
                c.M.Box(yellow, b + new Vec3(0, 2.6f, 0), new Vec3(1.8f, 1.0f, 0.08f), eyaw);
                c.World.signs.Add(new SignInstance { style = "banner", text = side < 0 ? "DIVERSION  →" : "←  SLOW", position = b + new Vec3(0, 2.6f, 0) - ed * 0.06f, yaw = eyaw + MathUtil.Pi, width = 1.7f, height = 0.9f, background = LagosYellow, foreground = new[] { 0.05f, 0.05f, 0.05f } });
            }
            // chevron board on the outside of the first bend
            c.Geo.Sample(start + 20f, out Vec3 ch, out Vec3 chd); Vec3 chr = Vec3.Cross(Vec3.Up, chd);
            Vec3 cb = Flat(ch + chr * (hw + 2.2f), 0f);
            c.M.Box(steel, cb + new Vec3(0, 0.6f, 0), new Vec3(0.1f, 1.2f, 0.1f));
            c.World.signs.Add(new SignInstance { style = "banner", text = ">>>>>>", position = cb + new Vec3(0, 1.6f, 0) - chd * 0.06f, yaw = chd.ToYaw() + MathUtil.Pi, width = 3.2f, height = 0.8f, background = LagosYellow, foreground = new[] { 0.05f, 0.05f, 0.05f } });
            // crane and concrete pipes beyond the verge
            Vec3 site = Flat(ch + chr * (hw + 12f), 0f);
            if (!InLagoon(site))
            {
                c.M.Box(yellow, site + new Vec3(0, 14f, 0), new Vec3(1.4f, 28f, 1.4f));
                c.M.Box(yellow, site + new Vec3(10f, 27.5f, 0), new Vec3(26f, 1.0f, 1.0f));
                c.M.Beam(steel, site + new Vec3(22f, 27f, 0), site + new Vec3(22f, 8f, 0), 0.06f);
                c.M.Box(concrete, site + new Vec3(22f, 7.2f, 0), new Vec3(2.4f, 1.6f, 2.4f));
                for (int k = 0; k < 4; k++) c.M.Wheel(concrete, site + new Vec3(-4f + k * 2.2f, 1.0f, 5f), 1.0f, 3f, 0f, 12);
                c.M.Box(B(c, "sand", Sand), site + new Vec3(4f, 1.2f, 6f), new Vec3(6f, 2.4f, 5f));
                c.M.Frustum(B(c, "sand", Sand), site + new Vec3(4f, 2.4f, 6f), new Vec3(6f, 0, 5f), new Vec3(1f, 0, 1f), 2f);
            }
        }

        private static void BuildGrandstands(Ctx c)
        {
            // Temporary stands along the start straight: tiered rows of fans with flags
            float hw = c.Track.roadHalfWidth;
            var frame = B(c, "metal", new[] { 0.3f, 0.32f, 0.35f });
            var seat = B(c, "paint", new[] { 0.1f, 0.5f, 0.3f });
            var seat2 = B(c, "paint", new[] { 0.95f, 0.95f, 0.95f });
            foreach (float side in new[] { -1f, 1f })
            {
                for (float along = c.Geo.LapLength - 52f; along < c.Geo.LapLength - 8f; along += 2.2f)
                {
                    c.Geo.Sample(along, out Vec3 p, out Vec3 d); Vec3 r = Vec3.Cross(Vec3.Up, d); float yaw = d.ToYaw();
                    for (int row = 0; row < 4; row++)
                    {
                        Vec3 b = Flat(p + r * (side * (hw + 4f + row * 1.3f)), row * 0.8f);
                        c.M.Box((row + (int)(along / 2.2f)) % 2 == 0 ? seat : seat2, b + new Vec3(0, 0.25f, 0), new Vec3(1.3f, 0.5f, 2.2f), yaw);
                        if (row > 0) c.M.Box(frame, Flat(b, row * 0.4f), new Vec3(1.2f, row * 0.8f, 2.2f), yaw);
                        if (c.Rng.Chance(0.75f)) Person(c, b + new Vec3(0, 0.5f, 0), yaw + MathUtil.Pi * 0.5f * -side + c.Rng.Range(-0.4f, 0.4f));
                    }
                    if ((int)(along / 2.2f) % 5 == 0)
                    {
                        Vec3 fb = Flat(p + r * (side * (hw + 9.5f)), 3.2f);
                        c.M.Cylinder(B(c, "metal", Steel), Flat(fb, 3.2f), 0.05f, 4f, 5);
                        c.M.Box(B(c, "cloth", (int)(along / 2.2f) % 10 == 0 ? new[] { 0.05f, 0.55f, 0.3f } : new[] { 0.95f, 0.95f, 0.95f }), fb + new Vec3(0, 6.5f, 0) + r * (side * 0.6f), new Vec3(1.2f, 0.8f, 0.03f), yaw + MathUtil.Pi * 0.5f);
                    }
                }
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
            mb.Box(mb.Batch("hologram", 1f, 0.55f, 0.1f, 1.4f, 0.6f), Vec3.Zero, new Vec3(1.4f, 1.4f, 1.4f), 0.6f);
            mb.Box(mb.Batch("emissive", 1f, 0.8f, 0.2f, 2.5f), Vec3.Zero, new Vec3(1.42f, 0.06f, 1.42f), 0.6f);
            mb.Box(mb.Batch("emissive", 1f, 0.8f, 0.2f, 2.5f), Vec3.Zero, new Vec3(0.06f, 1.42f, 1.42f), 0.6f);
            box.batches.AddRange(mb.Batches);
            box.signs.Add(new SignInstance { style = "hologram", text = "?", position = Vec3.Zero, yaw = 0f, width = 1.0f, height = 1.0f, background = new[] { 0f, 0f, 0f }, foreground = new[] { 1f, 0.95f, 0.6f } });
            box.signs.Add(new SignInstance { style = "hologram", text = "?", position = Vec3.Zero, yaw = MathUtil.Pi * 0.5f, width = 1.0f, height = 1.0f, background = new[] { 0f, 0f, 0f }, foreground = new[] { 1f, 0.95f, 0.6f } });
        }

        private static void Kart(Ctx c, string id)
        {
            var t = Template(c, "kart_" + id);
            var m = new MeshBuilder();
            var rubber = m.Batch("rubber", 0.05f, 0.05f, 0.06f);
            var rim = m.Batch("chrome", Chrome[0], Chrome[1], Chrome[2]);
            var chassis = m.Batch("metal", 0.16f, 0.16f, 0.18f);
            var glass = m.Batch("glass", 0.3f, 0.5f, 0.65f);
            var light = m.Batch("emissive", 1f, 0.95f, 0.75f, 1.4f);
            var tail = m.Batch("emissive", 1f, 0.12f, 0.08f, 1.4f);
            var chrome = m.Batch("chrome", Chrome[0], Chrome[1], Chrome[2]);
            var dark = m.Batch("paint", 0.06f, 0.06f, 0.07f);

            bool bike = id == "okada" || id == "delivery_bike";
            if (!bike)
            {
                m.Box(chassis, new Vec3(0, 0.3f, 0), new Vec3(1.7f, 0.1f, 2.6f));
                foreach (var w in new[] { new Vec3(-0.9f, 0.4f, 0.95f), new Vec3(0.9f, 0.4f, 0.95f), new Vec3(-0.9f, 0.4f, -0.95f), new Vec3(0.9f, 0.4f, -0.95f) })
                {
                    m.Wheel(rubber, w, 0.4f, 0.36f, 0f, 16);
                    m.Wheel(rim, w, 0.24f, 0.38f, 0f, 10);
                    m.Wheel(dark, w, 0.1f, 0.4f, 0f, 6);
                }
                // front/rear bumpers
                m.Box(dark, new Vec3(0, 0.45f, 1.32f), new Vec3(1.5f, 0.18f, 0.12f));
                m.Box(dark, new Vec3(0, 0.45f, -1.32f), new Vec3(1.5f, 0.18f, 0.12f));
            }
            else
            {
                m.Box(chassis, new Vec3(0, 0.45f, 0), new Vec3(0.25f, 0.25f, 1.9f));
                m.Wheel(rubber, new Vec3(0, 0.4f, 0.95f), 0.4f, 0.16f, 0f, 16);
                m.Wheel(rubber, new Vec3(0, 0.4f, -0.85f), 0.4f, 0.18f, 0f, 16);
                m.Wheel(rim, new Vec3(0, 0.4f, 0.95f), 0.22f, 0.18f, 0f, 10);
                m.Wheel(rim, new Vec3(0, 0.4f, -0.85f), 0.22f, 0.2f, 0f, 10);
            }

            void Mirrors(float y, float z, float halfW)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    m.Beam(chrome, new Vec3(s * halfW, y, z), new Vec3(s * (halfW + 0.22f), y + 0.05f, z), 0.04f);
                    m.Box(dark, new Vec3(s * (halfW + 0.26f), y + 0.05f, z), new Vec3(0.1f, 0.12f, 0.18f));
                }
            }
            void Plate(float y, float z, float yaw) =>
                t.signs.Add(new SignInstance { style = "plate", text = "LAGOS", position = new Vec3(0, y, z), yaw = yaw, width = 0.5f, height = 0.14f, background = new[] { 0.95f, 0.95f, 0.9f }, foreground = new[] { 0.1f, 0.1f, 0.3f }, template = t.name });

            switch (id)
            {
                case "danfo":
                {
                    var yellow = m.Batch("paint", DanfoYellow[0], DanfoYellow[1], DanfoYellow[2]);
                    var black = m.Batch("paint", 0.05f, 0.05f, 0.05f);
                    m.Frustum(yellow, new Vec3(0, 0.38f, -0.15f), new Vec3(1.55f, 0, 2.3f), new Vec3(1.5f, 0, 2.2f), 0.62f);   // lower body
                    m.Frustum(yellow, new Vec3(0, 1.0f, -0.25f), new Vec3(1.5f, 0, 2.0f), new Vec3(1.3f, 0, 1.7f), 0.6f);      // cabin
                    m.Box(black, new Vec3(0, 0.8f, -0.15f), new Vec3(1.58f, 0.16f, 2.26f));                                   // the stripe
                    m.Frustum(glass, new Vec3(0, 1.08f, 0.78f), new Vec3(1.3f, 0, 0.1f), new Vec3(1.15f, 0, 0.1f), 0.45f, 0f, -0.12f);
                    m.Box(glass, new Vec3(-0.72f, 1.3f, -0.35f), new Vec3(0.06f, 0.4f, 1.3f));
                    m.Box(glass, new Vec3(0.72f, 1.3f, -0.35f), new Vec3(0.06f, 0.4f, 1.3f));
                    m.Box(chassis, new Vec3(0, 1.66f, -0.3f), new Vec3(1.2f, 0.08f, 1.6f));                                    // roof rack
                    foreach (float s in new[] { -0.45f, 0.45f }) m.Box(chassis, new Vec3(s, 1.74f, -0.3f), new Vec3(0.06f, 0.1f, 1.5f));
                    m.Box(chrome, new Vec3(0, 0.55f, 1.0f), new Vec3(1.2f, 0.14f, 0.08f));                                      // grille
                    m.Box(light, new Vec3(-0.55f, 0.7f, 1.01f), new Vec3(0.3f, 0.2f, 0.06f)); m.Box(light, new Vec3(0.55f, 0.7f, 1.01f), new Vec3(0.3f, 0.2f, 0.06f));
                    m.Box(tail, new Vec3(-0.6f, 0.7f, -1.31f), new Vec3(0.26f, 0.2f, 0.06f)); m.Box(tail, new Vec3(0.6f, 0.7f, -1.31f), new Vec3(0.26f, 0.2f, 0.06f));
                    Mirrors(1.2f, 0.6f, 0.75f);
                    Plate(0.5f, -1.33f, MathUtil.Pi);
                    t.signs.Add(new SignInstance { style = "bus", text = "DANFO", position = new Vec3(0, 0.95f, -1.31f), yaw = MathUtil.Pi, width = 1.2f, height = 0.3f, background = DanfoYellow, foreground = new[] { 0.05f, 0.05f, 0.05f }, template = t.name });
                    Driver(m, c, new Vec3(0, 1.4f, 0.05f), open: false, jacket: new[] { 0.12f, 0.5f, 0.25f });
                    break;
                }
                case "keke":
                {
                    var yellow = m.Batch("paint", DanfoYellow[0], DanfoYellow[1], DanfoYellow[2]);
                    var green = m.Batch("paint", 0.1f, 0.45f, 0.25f);
                    m.Frustum(yellow, new Vec3(0, 0.45f, -0.1f), new Vec3(1.3f, 0, 2.0f), new Vec3(1.2f, 0, 1.8f), 0.7f);
                    m.Box(green, new Vec3(0, 1.6f, -0.1f), new Vec3(1.4f, 0.1f, 2.1f));
                    foreach (var post in new[] { new Vec3(-0.6f, 1.25f, -0.9f), new Vec3(0.6f, 1.25f, -0.9f), new Vec3(0, 1.25f, 0.85f) }) m.Box(chassis, post, new Vec3(0.06f, 0.7f, 0.06f));
                    m.Box(glass, new Vec3(0, 1.25f, 0.9f), new Vec3(1.1f, 0.5f, 0.05f));
                    m.Box(light, new Vec3(0, 0.8f, 1.0f), new Vec3(0.35f, 0.25f, 0.06f));
                    Plate(0.5f, -1.0f, MathUtil.Pi);
                    Driver(m, c, new Vec3(0, 1.15f, 0.2f), open: true, jacket: new[] { 0.9f, 0.9f, 0.85f });
                    break;
                }
                case "okada":
                case "delivery_bike":
                {
                    var body = m.Batch("paint", id == "okada" ? 0.8f : 0.95f, id == "okada" ? 0.12f : 0.5f, id == "okada" ? 0.1f : 0.05f);
                    m.Frustum(body, new Vec3(0, 0.6f, 0.1f), new Vec3(0.45f, 0, 1.1f), new Vec3(0.35f, 0, 0.9f), 0.35f);
                    m.Box(chrome, new Vec3(0, 1.05f, 0.85f), new Vec3(0.7f, 0.05f, 0.05f));
                    m.Beam(chrome, new Vec3(0, 0.55f, 0.75f), new Vec3(0, 1.05f, 0.9f), 0.06f);
                    m.Box(light, new Vec3(0, 0.95f, 1.0f), new Vec3(0.2f, 0.2f, 0.08f));
                    m.Cylinder(chrome, new Vec3(0.2f, 0.45f, -0.3f), 0.05f, 0.05f, 6);
                    m.Beam(chrome, new Vec3(0.2f, 0.5f, 0.2f), new Vec3(0.25f, 0.45f, -0.9f), 0.08f);  // exhaust
                    if (id == "delivery_bike") m.Box(m.Batch("paint", 0.95f, 0.5f, 0.05f), new Vec3(0, 1.1f, -0.75f), new Vec3(0.7f, 0.6f, 0.6f));
                    Driver(m, c, new Vec3(0, 1.25f, -0.05f), open: true, jacket: id == "okada" ? new[] { 0.9f, 0.9f, 0.9f } : new[] { 0.95f, 0.5f, 0.05f }, helmet: true);
                    break;
                }
                case "suv":
                {
                    var body = m.Batch("paint", 0.1f, 0.42f, 0.2f);
                    m.Frustum(body, new Vec3(0, 0.45f, -0.05f), new Vec3(1.6f, 0, 2.5f), new Vec3(1.55f, 0, 2.4f), 0.55f);
                    m.Frustum(body, new Vec3(0, 1.0f, -0.2f), new Vec3(1.5f, 0, 1.9f), new Vec3(1.3f, 0, 1.5f), 0.55f);
                    m.Frustum(glass, new Vec3(0, 1.02f, 0.7f), new Vec3(1.3f, 0, 0.08f), new Vec3(1.15f, 0, 0.08f), 0.45f, 0f, -0.2f);
                    m.Box(glass, new Vec3(-0.72f, 1.25f, -0.25f), new Vec3(0.06f, 0.36f, 1.2f)); m.Box(glass, new Vec3(0.72f, 1.25f, -0.25f), new Vec3(0.06f, 0.36f, 1.2f));
                    m.Box(chassis, new Vec3(0, 1.6f, -0.25f), new Vec3(1.1f, 0.08f, 1.3f));
                    m.Box(chrome, new Vec3(0, 0.6f, 1.2f), new Vec3(1.1f, 0.22f, 0.06f));
                    m.Box(dark, new Vec3(0, 0.95f, 1.1f), new Vec3(1.4f, 0.4f, 0.16f));                                        // bull bar
                    m.Box(light, new Vec3(-0.55f, 0.85f, 1.22f), new Vec3(0.35f, 0.2f, 0.06f)); m.Box(light, new Vec3(0.55f, 0.85f, 1.22f), new Vec3(0.35f, 0.2f, 0.06f));
                    m.Box(tail, new Vec3(-0.6f, 0.8f, -1.26f), new Vec3(0.25f, 0.3f, 0.06f)); m.Box(tail, new Vec3(0.6f, 0.8f, -1.26f), new Vec3(0.25f, 0.3f, 0.06f));
                    m.Wheel(rubber, new Vec3(0, 1.0f, -1.33f), 0.34f, 0.2f, MathUtil.Pi * 0.5f, 14);                            // spare on the back
                    Mirrors(1.1f, 0.55f, 0.78f);
                    Plate(0.55f, -1.27f, MathUtil.Pi);
                    Driver(m, c, new Vec3(0, 1.5f, 0.1f), open: false, jacket: new[] { 0.12f, 0.5f, 0.25f });
                    break;
                }
                case "food_truck":
                {
                    var body = m.Batch("paint", 0.96f, 0.95f, 0.92f);
                    var orange = m.Batch("paint", 1f, 0.5f, 0.1f);
                    m.Box(body, new Vec3(0, 1.0f, -0.25f), new Vec3(1.6f, 1.2f, 2.0f));
                    m.Box(orange, new Vec3(0, 1.0f, -0.25f), new Vec3(1.62f, 0.3f, 2.02f));
                    m.Frustum(orange, new Vec3(0.8f, 1.35f, -0.3f), new Vec3(0.1f, 0, 1.3f), new Vec3(0.9f, 0, 1.3f), 0.1f);   // awning
                    m.Box(glass, new Vec3(0, 1.1f, 0.78f), new Vec3(1.3f, 0.5f, 0.06f));
                    m.Box(light, new Vec3(-0.5f, 0.7f, 0.8f), new Vec3(0.3f, 0.2f, 0.06f)); m.Box(light, new Vec3(0.5f, 0.7f, 0.8f), new Vec3(0.3f, 0.2f, 0.06f));
                    t.signs.Add(new SignInstance { style = "bus", text = "JOLLOF EXPRESS", position = new Vec3(0.82f, 1.05f, -0.3f), yaw = MathUtil.Pi * 0.5f, width = 1.6f, height = 0.3f, background = new[] { 1f, 0.5f, 0.1f }, foreground = new[] { 1f, 1f, 1f }, template = t.name });
                    Plate(0.55f, -1.26f, MathUtil.Pi);
                    Driver(m, c, new Vec3(0, 1.65f, 0.3f), open: false, jacket: new[] { 0.9f, 0.2f, 0.2f });
                    break;
                }
                case "generator_kart":
                {
                    var blue = m.Batch("paint", 0.15f, 0.35f, 0.75f);
                    var gen = m.Batch("metal", 0.35f, 0.36f, 0.4f);
                    m.Frustum(blue, new Vec3(0, 0.45f, 0.3f), new Vec3(1.4f, 0, 1.4f), new Vec3(1.2f, 0, 1.2f), 0.45f);
                    m.Box(gen, new Vec3(0, 0.95f, -0.75f), new Vec3(1.3f, 0.8f, 0.9f));
                    m.Box(m.Batch("paint", 0.95f, 0.75f, 0.1f), new Vec3(0, 0.95f, -0.75f), new Vec3(1.32f, 0.2f, 0.92f));
                    m.Cylinder(gen, new Vec3(0.45f, 1.35f, -0.75f), 0.08f, 0.7f, 6);
                    m.Box(light, new Vec3(0, 0.75f, 1.0f), new Vec3(0.5f, 0.15f, 0.06f));
                    Plate(0.5f, -1.22f, MathUtil.Pi);
                    Driver(m, c, new Vec3(0, 1.05f, 0.3f), open: true, jacket: new[] { 0.9f, 0.85f, 0.2f });
                    break;
                }
                case "sports_car":
                {
                    var red = m.Batch("paint", 0.85f, 0.08f, 0.1f);
                    m.Frustum(red, new Vec3(0, 0.4f, 0f), new Vec3(1.7f, 0, 2.7f), new Vec3(1.6f, 0, 2.5f), 0.42f);
                    m.Frustum(red, new Vec3(0, 0.82f, -0.25f), new Vec3(1.45f, 0, 1.6f), new Vec3(1.15f, 0, 1.0f), 0.38f);
                    m.Frustum(glass, new Vec3(0, 0.84f, 0.5f), new Vec3(1.35f, 0, 0.08f), new Vec3(1.1f, 0, 0.08f), 0.34f, 0f, -0.3f);
                    m.Box(red, new Vec3(0, 1.05f, -1.2f), new Vec3(1.6f, 0.07f, 0.35f));                                      // spoiler
                    foreach (float s in new[] { -0.55f, 0.55f }) m.Box(red, new Vec3(s, 0.92f, -1.2f), new Vec3(0.08f, 0.2f, 0.25f));
                    m.Box(dark, new Vec3(0, 0.5f, 1.3f), new Vec3(1.2f, 0.18f, 0.1f));
                    m.Box(light, new Vec3(-0.55f, 0.62f, 1.36f), new Vec3(0.4f, 0.1f, 0.06f)); m.Box(light, new Vec3(0.55f, 0.62f, 1.36f), new Vec3(0.4f, 0.1f, 0.06f));
                    m.Box(tail, new Vec3(0, 0.66f, -1.36f), new Vec3(1.3f, 0.1f, 0.06f));
                    m.Cylinder(chrome, new Vec3(-0.45f, 0.42f, -1.4f), 0.08f, 0.02f, 8); m.Cylinder(chrome, new Vec3(0.45f, 0.42f, -1.4f), 0.08f, 0.02f, 8);
                    Mirrors(0.95f, 0.45f, 0.8f);
                    Plate(0.5f, -1.37f, MathUtil.Pi);
                    t.signs.Add(new SignInstance { style = "bus", text = "CAMRY", position = new Vec3(0, 0.78f, -1.36f), yaw = MathUtil.Pi, width = 0.9f, height = 0.16f, background = new[] { 0.85f, 0.08f, 0.1f }, foreground = new[] { 0.95f, 0.95f, 0.95f }, template = t.name });
                    Driver(m, c, new Vec3(0, 1.1f, 0.0f), open: true, jacket: new[] { 0.1f, 0.1f, 0.12f }, cap: true);
                    break;
                }
                case "executive_sedan":
                {
                    var black = m.Batch("paint", 0.08f, 0.08f, 0.1f);
                    m.Frustum(black, new Vec3(0, 0.42f, 0f), new Vec3(1.65f, 0, 2.7f), new Vec3(1.6f, 0, 2.6f), 0.5f);
                    m.Frustum(black, new Vec3(0, 0.92f, -0.2f), new Vec3(1.5f, 0, 1.7f), new Vec3(1.3f, 0, 1.2f), 0.45f);
                    m.Frustum(glass, new Vec3(0, 0.94f, 0.6f), new Vec3(1.35f, 0, 0.08f), new Vec3(1.15f, 0, 0.08f), 0.4f, 0f, -0.25f);
                    m.Box(glass, new Vec3(-0.72f, 1.15f, -0.2f), new Vec3(0.06f, 0.3f, 1.0f)); m.Box(glass, new Vec3(0.72f, 1.15f, -0.2f), new Vec3(0.06f, 0.3f, 1.0f));
                    m.Box(chrome, new Vec3(0, 0.6f, 1.33f), new Vec3(1.0f, 0.2f, 0.06f));
                    m.Box(light, new Vec3(-0.55f, 0.72f, 1.34f), new Vec3(0.35f, 0.15f, 0.06f)); m.Box(light, new Vec3(0.55f, 0.72f, 1.34f), new Vec3(0.35f, 0.15f, 0.06f));
                    m.Box(tail, new Vec3(-0.55f, 0.72f, -1.33f), new Vec3(0.35f, 0.15f, 0.06f)); m.Box(tail, new Vec3(0.55f, 0.72f, -1.33f), new Vec3(0.35f, 0.15f, 0.06f));
                    Mirrors(1.0f, 0.5f, 0.8f);
                    Plate(0.52f, -1.33f, MathUtil.Pi);
                    Driver(m, c, new Vec3(0, 1.3f, 0.05f), open: false, jacket: new[] { 0.95f, 0.95f, 0.95f });
                    break;
                }
                default: // compact sedan
                {
                    var silver = m.Batch("paint", 0.8f, 0.82f, 0.85f);
                    m.Frustum(silver, new Vec3(0, 0.42f, 0f), new Vec3(1.55f, 0, 2.5f), new Vec3(1.5f, 0, 2.4f), 0.48f);
                    m.Frustum(silver, new Vec3(0, 0.9f, -0.15f), new Vec3(1.4f, 0, 1.6f), new Vec3(1.2f, 0, 1.1f), 0.42f);
                    m.Frustum(glass, new Vec3(0, 0.92f, 0.6f), new Vec3(1.25f, 0, 0.08f), new Vec3(1.05f, 0, 0.08f), 0.36f, 0f, -0.25f);
                    m.Box(glass, new Vec3(-0.66f, 1.1f, -0.15f), new Vec3(0.06f, 0.28f, 0.9f)); m.Box(glass, new Vec3(0.66f, 1.1f, -0.15f), new Vec3(0.06f, 0.28f, 0.9f));
                    m.Box(dark, new Vec3(0, 0.58f, 1.23f), new Vec3(0.9f, 0.18f, 0.06f));
                    m.Box(light, new Vec3(-0.5f, 0.7f, 1.24f), new Vec3(0.32f, 0.15f, 0.06f)); m.Box(light, new Vec3(0.5f, 0.7f, 1.24f), new Vec3(0.32f, 0.15f, 0.06f));
                    m.Box(tail, new Vec3(-0.5f, 0.7f, -1.23f), new Vec3(0.3f, 0.14f, 0.06f)); m.Box(tail, new Vec3(0.5f, 0.7f, -1.23f), new Vec3(0.3f, 0.14f, 0.06f));
                    Mirrors(0.98f, 0.5f, 0.75f);
                    Plate(0.5f, -1.24f, MathUtil.Pi);
                    Driver(m, c, new Vec3(0, 1.25f, 0.05f), open: false, jacket: new[] { 0.12f, 0.5f, 0.25f });
                    break;
                }
            }
            t.batches.AddRange(m.Batches);
        }

        private static void Driver(MeshBuilder m, Ctx c, Vec3 seat, bool open, float[] jacket, bool cap = false, bool helmet = false)
        {
            // Visible driver (kart games show the character big): torso, head with afro or cap, arms to a steering wheel.
            const float S = 1.15f;
            Vec3 Off(float x, float y, float z) => seat + new Vec3(x * S, y * S, z * S);
            Vec3 Size(float x, float y, float z) => new Vec3(x * S, y * S, z * S);
            var skin = m.Batch("skin", 0.4f, 0.25f, 0.15f);
            var hair = m.Batch("paint", 0.06f, 0.05f, 0.05f);
            var cloth = m.Batch("cloth", jacket[0], jacket[1], jacket[2]);
            var white = m.Batch("paint", 0.95f, 0.95f, 0.95f);
            m.Frustum(cloth, Off(0, -0.08f, 0), Size(0.5f, 0, 0.32f), Size(0.64f, 0, 0.38f), 0.52f * S);
            m.Box(white, Off(0, 0.28f, 0.19f), Size(0.16f, 0.12f, 0.03f));                 // crown logo patch
            m.Cylinder(skin, Off(0, 0.44f, 0), 0.07f * S, 0.1f * S, 6);                     // neck
            m.Sphere(skin, Off(0, 0.66f, 0), 0.17f * S, 10, 7, 1.1f);
            if (helmet) m.Sphere(m.Batch("paint", 0.95f, 0.2f, 0.1f), Off(0, 0.7f, 0), 0.21f * S, 10, 7, 1f);
            else if (cap) { m.Sphere(m.Batch("paint", 0.9f, 0.15f, 0.12f), Off(0, 0.74f, 0), 0.19f * S, 10, 6, 0.75f); m.Box(m.Batch("paint", 0.9f, 0.15f, 0.12f), Off(0, 0.72f, 0.2f), Size(0.3f, 0.03f, 0.18f)); }
            else m.Sphere(hair, Off(0, 0.8f, -0.02f), 0.26f * S, 11, 7, 0.85f);                // afro
            m.Box(m.Batch("paint", 0.05f, 0.05f, 0.06f), Off(0, 0.68f, 0.15f), Size(0.27f, 0.06f, 0.04f));   // sunglasses
            Vec3 wheel = Off(0, 0.32f, 0.5f);
            m.Beam(cloth, Off(-0.32f, 0.3f, 0.05f), wheel + new Vec3(-0.19f * S, 0, -0.05f), 0.11f * S);
            m.Beam(cloth, Off(0.32f, 0.3f, 0.05f), wheel + new Vec3(0.19f * S, 0, -0.05f), 0.11f * S);
            m.Sphere(skin, wheel + new Vec3(-0.19f * S, 0, -0.03f), 0.065f * S, 6, 4); m.Sphere(skin, wheel + new Vec3(0.19f * S, 0, -0.03f), 0.065f * S, 6, 4);
            m.Wheel(m.Batch("paint", 0.08f, 0.08f, 0.09f), wheel, 0.2f * S, 0.04f, MathUtil.Pi * 0.5f, 12);
            if (open) m.Beam(m.Batch("metal", 0.2f, 0.2f, 0.22f), wheel + new Vec3(0, -0.1f, 0.05f), wheel + new Vec3(0, -0.4f, 0.3f), 0.05f);
        }

        private static void Hazards(Ctx c)
        {
            {
                var t = Template(c, "danfo_bus"); var m = new MeshBuilder();
                var yellow = m.Batch("paint", DanfoYellow[0], DanfoYellow[1], DanfoYellow[2]);
                var black = m.Batch("paint", 0.05f, 0.05f, 0.05f);
                var glass = m.Batch("glass", 0.3f, 0.5f, 0.65f);
                var rubber = m.Batch("rubber", 0.05f, 0.05f, 0.06f);
                m.Frustum(yellow, new Vec3(0, 0.45f, 0), new Vec3(2.0f, 0, 4.7f), new Vec3(2.0f, 0, 4.6f), 1.9f);
                m.Box(black, new Vec3(0, 1.0f, 0), new Vec3(2.04f, 0.25f, 4.64f));
                m.Frustum(glass, new Vec3(0, 1.4f, 2.33f), new Vec3(1.75f, 0, 0.06f), new Vec3(1.6f, 0, 0.06f), 0.7f, 0f, -0.08f);
                for (int k = -1; k <= 1; k++) { m.Box(glass, new Vec3(-1.02f, 1.7f, k * 1.4f), new Vec3(0.06f, 0.6f, 1.0f)); m.Box(glass, new Vec3(1.02f, 1.7f, k * 1.4f), new Vec3(0.06f, 0.6f, 1.0f)); }
                foreach (var w in new[] { new Vec3(-1f, 0.42f, 1.5f), new Vec3(1f, 0.42f, 1.5f), new Vec3(-1f, 0.42f, -1.5f), new Vec3(1f, 0.42f, -1.5f) }) { m.Wheel(rubber, w, 0.42f, 0.3f, 0f, 14); m.Wheel(m.Batch("chrome", Chrome[0], Chrome[1], Chrome[2]), w, 0.2f, 0.32f, 0f, 8); }
                m.Box(m.Batch("emissive", 1f, 0.95f, 0.7f, 1.2f), new Vec3(-0.6f, 0.9f, 2.35f), new Vec3(0.4f, 0.25f, 0.06f));
                m.Box(m.Batch("emissive", 1f, 0.95f, 0.7f, 1.2f), new Vec3(0.6f, 0.9f, 2.35f), new Vec3(0.4f, 0.25f, 0.06f));
                m.Box(m.Batch("metal", 0.16f, 0.16f, 0.18f), new Vec3(0, 2.4f, 0), new Vec3(1.6f, 0.1f, 3.6f));
                for (int k = 0; k < 4; k++) m.Box(m.Batch("cloth", ClothTones[k][0], ClothTones[k][1], ClothTones[k][2]), new Vec3((k % 2) * 0.8f - 0.4f, 2.7f, k * 0.9f - 1.3f), new Vec3(0.7f, 0.5f, 0.8f));
                t.signs.Add(new SignInstance { style = "bus", text = "NO FOOD FOR LAZY MAN", position = new Vec3(0, 1.55f, -2.33f), yaw = MathUtil.Pi, width = 1.7f, height = 0.32f, background = DanfoYellow, foreground = new[] { 0.05f, 0.05f, 0.05f }, template = t.name });
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "okada_bike"); var m = new MeshBuilder();
                var body = m.Batch("paint", 0.2f, 0.25f, 0.8f); var rubber = m.Batch("rubber", 0.05f, 0.05f, 0.06f);
                m.Frustum(body, new Vec3(0, 0.6f, 0.1f), new Vec3(0.45f, 0, 1.1f), new Vec3(0.35f, 0, 0.9f), 0.35f);
                m.Wheel(rubber, new Vec3(0, 0.4f, 0.95f), 0.4f, 0.14f, 0f, 14); m.Wheel(rubber, new Vec3(0, 0.4f, -0.85f), 0.4f, 0.16f, 0f, 14);
                Driver(m, c, new Vec3(0, 1.25f, -0.05f), open: true, jacket: new[] { 0.9f, 0.3f, 0.1f }, helmet: true);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "oil_patch"); var m = new MeshBuilder();
                m.Cylinder(m.Batch("glass", 0.03f, 0.03f, 0.05f, 0f, 0.92f), new Vec3(0, 0.01f, 0), 1.1f, 0.03f, 16);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "pothole"); var m = new MeshBuilder();
                m.Cylinder(m.Batch("road", 0.04f, 0.04f, 0.04f), new Vec3(0, 0.005f, 0), 1.2f, 0.02f, 12);
                m.Cylinder(m.Batch("water", 0.25f, 0.22f, 0.15f), new Vec3(0, 0.01f, 0), 0.85f, 0.02f, 12);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "spike_strip"); var m = new MeshBuilder();
                var metal = m.Batch("chrome", 0.7f, 0.7f, 0.72f);
                m.Box(metal, new Vec3(0, 0.05f, 0), new Vec3(3.6f, 0.08f, 0.4f));
                for (int k = -6; k <= 6; k++) m.Cone(metal, new Vec3(k * 0.28f, 0.08f, 0), 0.07f, 0.32f, 5);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "cone_row"); var m = new MeshBuilder();
                var orange = m.Batch("paint", 1f, 0.42f, 0.05f);
                for (int k = -2; k <= 2; k++) { m.Cone(orange, new Vec3(k * 2.5f, 0, 0), 0.3f, 0.8f, 10); m.Box(m.Batch("paint", 0.95f, 0.95f, 0.95f), new Vec3(k * 2.5f, 0.42f, 0), new Vec3(0.3f, 0.1f, 0.3f)); m.Box(orange, new Vec3(k * 2.5f, 0.03f, 0), new Vec3(0.7f, 0.06f, 0.7f)); }
                m.Box(m.Batch("metal", Steel[0], Steel[1], Steel[2]), new Vec3(0, 0.5f, -1.2f), new Vec3(0.1f, 1.0f, 0.1f));
                t.signs.Add(new SignInstance { style = "banner", text = "GO SLOW", subText = "WORK IN PROGRESS", position = new Vec3(0, 1.4f, -1.2f), yaw = MathUtil.Pi, width = 2.6f, height = 0.9f, background = LagosYellow, foreground = new[] { 0.05f, 0.05f, 0.05f }, template = t.name });
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "flood"); var m = new MeshBuilder();
                m.Cylinder(m.Batch("water", 0.35f, 0.3f, 0.2f, 0f, 0.8f), new Vec3(0, 0.02f, 0), 7f, 0.04f, 18);
                t.batches.AddRange(m.Batches);
            }
            {
                var t = Template(c, "checkpoint"); var m = new MeshBuilder();
                var white = m.Batch("paint", 0.95f, 0.95f, 0.95f); var red = m.Batch("paint", 0.85f, 0.12f, 0.18f);
                m.Box(white, new Vec3(0, 0.5f, 0), new Vec3(6f, 0.25f, 0.3f));
                for (int k = -2; k <= 2; k += 2) m.Box(red, new Vec3(k * 1.2f, 0.5f, 0), new Vec3(0.8f, 0.27f, 0.32f));
                m.Box(m.Batch("metal", 0.5f, 0.5f, 0.5f), new Vec3(-2.9f, 0.3f, 0), new Vec3(0.1f, 0.6f, 0.4f)); m.Box(m.Batch("metal", 0.5f, 0.5f, 0.5f), new Vec3(2.9f, 0.3f, 0), new Vec3(0.1f, 0.6f, 0.4f));
                var cloth = m.Batch("cloth", 0.1f, 0.1f, 0.12f);
                m.Frustum(cloth, new Vec3(3.6f, 0.0f, 0.4f), new Vec3(0.45f, 0, 0.3f), new Vec3(0.55f, 0, 0.34f), 1.3f);
                m.Sphere(m.Batch("skin", 0.4f, 0.25f, 0.15f), new Vec3(3.6f, 1.5f, 0.4f), 0.15f, 8, 5, 1.1f);
                m.Box(m.Batch("paint", 0.1f, 0.1f, 0.12f), new Vec3(3.6f, 1.68f, 0.4f), new Vec3(0.34f, 0.08f, 0.34f));
                t.signs.Add(new SignInstance { style = "banner", text = "LASTMA", subText = "CHECKPOINT", position = new Vec3(0, 0.55f, 0.17f), yaw = 0f, width = 2.2f, height = 0.24f, background = new[] { 0.95f, 0.8f, 0.1f }, foreground = new[] { 0.05f, 0.05f, 0.05f }, template = t.name });
                t.batches.AddRange(m.Batches);
            }
        }

        private static void BuildProps(Ctx c)
        {
            foreach (var box in c.Track.itemBoxes)
            {
                var p = c.Geo.Project(box.position);
                c.World.props.Add(new PropInstance { template = "item_box", position = new Vec3(box.position.X, p.Point.Y + 1.1f, box.position.Z), yaw = 0f, scale = 1f, label = box.id });
            }
        }
    }
}
