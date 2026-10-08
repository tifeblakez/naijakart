using System;
using System.Collections.Generic;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.World
{
    /// <summary>
    /// Engine-agnostic mesh data: triangles with a flat colour per batch. Unity builds Mesh objects from
    /// it; the web previewer builds BufferGeometry. Everything the world needs is generated from the
    /// track definition and a theme, so no hand-made art is required (and none is used).
    /// </summary>
    [Serializable]
    public sealed class MeshBatch
    {
        public string name;
        /// <summary>Material hint: road, concrete, barrier, metal, chrome, glass, tower, water, foliage, trunk, paint, rubber, sign, ground, sand, grass, skin, cloth, emissive.</summary>
        public string material = "paint";
        public float r = 0.5f, g = 0.5f, b = 0.5f;
        public float emissive;
        public float opacity = 1f;
        public List<float> vertices = new List<float>();
        public List<float> normals = new List<float>();
        public List<int> triangles = new List<int>();

        public int VertexCount => vertices.Count / 3;
    }

    /// <summary>A placed instance of a template (kart, hazard, item box, tree) with position and yaw.</summary>
    [Serializable]
    public sealed class PropInstance
    {
        public string template;
        public Vec3 position;
        public float yaw;
        public float scale = 1f;
        public string label;
    }

    /// <summary>
    /// Text that the renderer draws onto a quad (gantry boards, billboards, number plates, shop
    /// fronts). Text is content, not art: each renderer rasterises it with its own font.
    /// </summary>
    [Serializable]
    public sealed class SignInstance
    {
        /// <summary>gantry, billboard, shop, plate, banner, bus.</summary>
        public string style = "billboard";
        public string text = "";
        public string subText = "";
        public Vec3 position;
        public float yaw;
        public float width = 6f, height = 2f;
        public float[] background = { 0.05f, 0.45f, 0.22f };
        public float[] foreground = { 1f, 1f, 1f };
        /// <summary>Template the sign belongs to, or null for a world sign.</summary>
        public string template;
    }

    [Serializable]
    public sealed class MeshTemplate
    {
        public string name;
        public List<MeshBatch> batches = new List<MeshBatch>();
        public List<SignInstance> signs = new List<SignInstance>();
    }

    [Serializable]
    public sealed class WorldModel
    {
        public string trackId;
        public string displayName;
        public string city;
        public List<MeshBatch> batches = new List<MeshBatch>();
        public List<MeshTemplate> templates = new List<MeshTemplate>();
        public List<PropInstance> props = new List<PropInstance>();
        public List<SignInstance> signs = new List<SignInstance>();
        /// <summary>Centreline sampled every few metres for minimaps and overview cameras.</summary>
        public List<Vec3> minimap = new List<Vec3>();
        public Vec3 sunDirection = new Vec3(-0.4f, -0.8f, -0.3f);
        public float[] skyTop = { 0.35f, 0.6f, 0.95f };
        public float[] skyHorizon = { 0.95f, 0.8f, 0.6f };
        public float[] fogColor = { 0.85f, 0.8f, 0.72f };
        public float fogStart = 180f;
        public float fogEnd = 900f;
        public float waterLevel = -3f;
        public float[] ambient = { 0.55f, 0.6f, 0.7f };
        /// <summary>day or night (Day &amp; Night Cycles in the environment reference).</summary>
        public string theme = "day";
        public bool isNight;
        public float sunIntensity = 4f;
        public float[] sunColor = { 1f, 0.9f, 0.77f };
        /// <summary>Emissive strength of street lamps and lit signs; renderers scale lamp/sign glow by it.</summary>
        public float lampGlow = 0.6f;
        /// <summary>Fraction of tower windows that are lit.</summary>
        public float windowLitChance = 0.14f;

        public MeshTemplate Template(string name)
        {
            foreach (var t in templates) if (t.name == name) return t;
            return null;
        }
    }
}
