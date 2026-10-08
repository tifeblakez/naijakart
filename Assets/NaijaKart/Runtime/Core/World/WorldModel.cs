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
        /// <summary>Material hint: road, kerb, concrete, metal, glass, water, foliage, trunk, paint, rubber, sign, ground, emissive.</summary>
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

    [Serializable]
    public sealed class MeshTemplate
    {
        public string name;
        public List<MeshBatch> batches = new List<MeshBatch>();
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
        public Vec3 sunDirection = new Vec3(-0.4f, -0.8f, -0.3f);
        public float[] skyTop = { 0.35f, 0.6f, 0.95f };
        public float[] skyHorizon = { 0.95f, 0.8f, 0.6f };
        public float[] fogColor = { 0.85f, 0.8f, 0.72f };
        public float fogStart = 180f;
        public float fogEnd = 900f;
        public float waterLevel = -3f;
        public float[] ambient = { 0.55f, 0.6f, 0.7f };

        public MeshTemplate Template(string name)
        {
            foreach (var t in templates) if (t.name == name) return t;
            return null;
        }
    }
}
