using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.World;
using UnityEngine;

namespace NaijaKart.Unity.View
{
    /// <summary>
    /// Builds the generated world (WorldBuilder in Core) as Unity meshes: one GameObject per batch
    /// (shared material per material/colour), plus template instances for props. The same generator
    /// output drives the web previewer, so what you preview is what the client renders. No imported art.
    /// </summary>
    public sealed class TrackWorldBuilder : MonoBehaviour
    {
        [SerializeField] private Shader _shader;
        [SerializeField] private bool _castShadows = true;
        private readonly List<GameObject> _built = new List<GameObject>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private WorldModel _world;

        public WorldModel World => _world;

        public void Build(TrackDefinition track, ulong seed = 1)
        {
            Clear();
            _world = WorldBuilder.Build(track, seed);
            foreach (var b in _world.batches) _built.Add(CreateBatch(b, transform, Vector3.zero, 0f, 1f));
            foreach (var p in _world.props)
            {
                var t = _world.Template(p.template);
                if (t == null) continue;
                var go = new GameObject("Prop_" + p.template);
                go.transform.SetParent(transform, false);
                go.transform.position = p.position.ToUnity();
                go.transform.rotation = Quaternion.Euler(0f, p.yaw * Mathf.Rad2Deg, 0f);
                go.transform.localScale = Vector3.one * p.scale;
                foreach (var b in t.batches) CreateBatch(b, go.transform, Vector3.zero, 0f, 1f);
                foreach (var sign in t.signs) CreateSign(sign, go.transform);
                _built.Add(go);
            }
            foreach (var sign in _world.signs) _built.Add(CreateSign(sign, transform));
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = _world.fogStart;
            RenderSettings.fogEndDistance = _world.fogEnd;
            RenderSettings.fogColor = new Color(_world.fogColor[0], _world.fogColor[1], _world.fogColor[2]);
            RenderSettings.ambientLight = new Color(_world.ambient[0], _world.ambient[1], _world.ambient[2]);
        }

        /// <summary>Instantiates a kart/hazard template (used by VehicleView and HazardView when no art prefab is assigned).</summary>
        public GameObject Instantiate(string templateName, Transform parent)
        {
            if (_world == null) return null;
            var t = _world.Template(templateName) ?? _world.Template("kart_sedan");
            if (t == null) return null;
            var go = new GameObject(templateName);
            go.transform.SetParent(parent, false);
            foreach (var b in t.batches) CreateBatch(b, go.transform, Vector3.zero, 0f, 1f);
            foreach (var sign in t.signs) CreateSign(sign, go.transform);
            return go;
        }

        /// <summary>
        /// Signs are text content, not textures: a backing quad plus a TextMesh. The web previewer
        /// rasterises the same SignInstance to a canvas; both read identical copy from the model.
        /// </summary>
        private GameObject CreateSign(SignInstance sign, Transform parent)
        {
            var go = new GameObject("Sign_" + sign.style);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = sign.position.ToUnity();
            go.transform.localRotation = Quaternion.Euler(0f, sign.yaw * Mathf.Rad2Deg, 0f);
            if (sign.style != "hologram")
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Board";
                quad.transform.SetParent(go.transform, false);
                quad.transform.localScale = new Vector3(sign.width, sign.height, 1f);
                quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Unity quads face -Z; signs face +Z (their yaw direction)
                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialFor(new MeshBatch { material = "sign", r = sign.background[0], g = sign.background[1], b = sign.background[2] });
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (quad.TryGetComponent<Collider>(out var col)) Destroy(col);
            }
            bool twoLines = !string.IsNullOrEmpty(sign.subText);
            var text = new GameObject("Text").AddComponent<TextMesh>();
            text.transform.SetParent(go.transform, false);
            text.transform.localPosition = new Vector3(0f, twoLines ? sign.height * 0.14f : 0f, 0.01f);
            text.text = sign.text;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.fontStyle = FontStyle.Bold;
            text.characterSize = sign.height * (twoLines ? 0.045f : 0.065f); text.fontSize = 64;
            text.color = new Color(sign.foreground[0], sign.foreground[1], sign.foreground[2]);
            if (twoLines)
            {
                var sub = new GameObject("Sub").AddComponent<TextMesh>();
                sub.transform.SetParent(go.transform, false);
                sub.transform.localPosition = new Vector3(0f, -sign.height * 0.24f, 0.01f);
                sub.text = sign.subText; sub.anchor = TextAnchor.MiddleCenter; sub.alignment = TextAlignment.Center; sub.fontStyle = FontStyle.Bold;
                sub.characterSize = sign.height * 0.03f; sub.fontSize = 64; sub.color = text.color;
            }
            return go;
        }

        private GameObject CreateBatch(MeshBatch b, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var go = new GameObject(b.name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = b.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new Vector3[b.VertexCount];
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(b.vertices[i * 3], b.vertices[i * 3 + 1], b.vertices[i * 3 + 2]);
            mesh.vertices = verts;
            if (b.normals.Count == b.vertices.Count)
            {
                var norms = new Vector3[verts.Length];
                for (int i = 0; i < norms.Length; i++) norms[i] = new Vector3(b.normals[i * 3], b.normals[i * 3 + 1], b.normals[i * 3 + 2]);
                mesh.normals = norms;
            }
            mesh.triangles = b.triangles.ToArray();
            if (b.normals.Count != b.vertices.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFor(b);
            bool noShadow = b.material == "water" || b.material == "ground" || b.material == "sand" || b.material == "grass" || b.material == "road";
            mr.shadowCastingMode = _castShadows && !noShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (b.material == "road" || b.material == "concrete" || b.material == "ground" || b.material == "barrier") go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private Material MaterialFor(MeshBatch b)
        {
            string key = $"{b.material}|{b.r:0.00}|{b.g:0.00}|{b.b:0.00}|{b.emissive:0.0}|{b.opacity:0.00}";
            if (_materials.TryGetValue(key, out var m)) return m;
            var shader = _shader != null ? _shader : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { name = key };
            var color = new Color(b.r, b.g, b.b, b.opacity);
            m.SetColor("_BaseColor", color); m.SetColor("_Color", color);
            // Material hints → URP Lit parameters. Procedural surface detail (asphalt grain, tower windows,
            // water waves) lives in shader graphs keyed by the same hint names; the previewer's GLSL is the reference.
            float smooth, metallic;
            switch (b.material)
            {
                case "glass": smooth = 0.95f; metallic = 0.1f; break;
                case "water": smooth = 0.92f; metallic = 0f; break;
                case "chrome": smooth = 0.9f; metallic = 1f; break;
                case "metal": smooth = 0.6f; metallic = 0.85f; break;
                case "paint": smooth = 0.65f; metallic = 0.15f; break;
                case "tower": smooth = 0.5f; metallic = 0.2f; break;
                case "hologram": smooth = 0.9f; metallic = 0.3f; break;
                case "skin": smooth = 0.45f; metallic = 0f; break;
                case "road": smooth = 0.15f; metallic = 0f; break;
                default: smooth = 0.1f; metallic = 0f; break;
            }
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (b.emissive > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * b.emissive); }
            if (b.opacity < 1f || b.material == "water" || b.material == "hologram")
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.renderQueue = 3000;
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            _materials[key] = m;
            return m;
        }

        public void Clear()
        {
            foreach (var go in _built) if (go != null) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            _built.Clear();
        }
    }
}
