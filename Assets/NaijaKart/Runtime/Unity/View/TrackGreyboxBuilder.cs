using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Track;
using UnityEngine;

namespace NaijaKart.Unity.View
{
    /// <summary>
    /// Builds a drivable greybox mesh (road ribbon, shortcut ribbons, kerbs, checkpoint gates) from a
    /// TrackDefinition at runtime or in the editor. This is the Phase 2 greybox; production art
    /// replaces the visuals (Step 12) while the TrackDefinition stays the source of truth for gameplay.
    /// </summary>
    public sealed class TrackGreyboxBuilder : MonoBehaviour
    {
        [SerializeField] private Material _roadMaterial;
        [SerializeField] private Material _shortcutMaterial;
        [SerializeField] private Material _kerbMaterial;
        [SerializeField] private Material _groundMaterial;
        [SerializeField] private bool _buildGroundPlane = true;
        [SerializeField] private bool _showCheckpointGizmos = true;
        [SerializeField] private float _samplesPerMetre = 0.25f;

        private TrackDefinition _def;
        private readonly List<GameObject> _built = new List<GameObject>();

        public TrackDefinition Definition => _def;

        public void Build(TrackDefinition def)
        {
            Clear();
            _def = def;
            var geo = new TrackGeometry(def);
            _built.Add(BuildRibbon("Road", ClosedSamples(geo), def.roadHalfWidth, _roadMaterial, closed: true));
            _built.Add(BuildRibbon("Kerb_L", ClosedSamples(geo), def.roadHalfWidth + 0.6f, _kerbMaterial, closed: true, innerHalfWidth: def.roadHalfWidth));
            foreach (var sc in def.shortcutRoads ?? System.Array.Empty<ShortcutRoad>())
            {
                _built.Add(BuildRibbon("Shortcut_" + sc.id, OpenSamples(sc.points), sc.halfWidth, _shortcutMaterial != null ? _shortcutMaterial : _roadMaterial, closed: false));
            }
            if (_buildGroundPlane)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.SetParent(transform, false);
                Bounds b = new Bounds(def.centreline[0].ToUnity(), Vector3.zero);
                foreach (var p in def.centreline) b.Encapsulate(p.ToUnity());
                ground.transform.position = new Vector3(b.center.x, -0.05f, b.center.z);
                ground.transform.localScale = new Vector3(b.size.x / 10f + 20f, 1f, b.size.z / 10f + 20f);
                if (_groundMaterial != null) ground.GetComponent<Renderer>().sharedMaterial = _groundMaterial;
                _built.Add(ground);
            }
        }

        public void Clear()
        {
            foreach (var go in _built) if (go != null) DestroyImmediateSafe(go);
            _built.Clear();
        }

        private static void DestroyImmediateSafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private List<(Vector3 p, Vector3 dir)> ClosedSamples(TrackGeometry geo)
        {
            var list = new List<(Vector3, Vector3)>();
            int n = Mathf.Max(8, Mathf.RoundToInt(geo.LapLength * _samplesPerMetre));
            for (int i = 0; i < n; i++)
            {
                geo.Sample(geo.LapLength * i / n, out Vec3 p, out Vec3 d);
                list.Add((p.ToUnity(), d.ToUnity()));
            }
            return list;
        }

        private static List<(Vector3 p, Vector3 dir)> OpenSamples(Vec3[] pts)
        {
            var list = new List<(Vector3, Vector3)>();
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 prev = pts[Mathf.Max(0, i - 1)].ToUnity();
                Vector3 next = pts[Mathf.Min(pts.Length - 1, i + 1)].ToUnity();
                Vector3 dir = (next - prev); dir.y = 0f;
                list.Add((pts[i].ToUnity(), dir.normalized));
            }
            return list;
        }

        private GameObject BuildRibbon(string name, List<(Vector3 p, Vector3 dir)> samples, float halfWidth, Material mat, bool closed, float innerHalfWidth = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int count = samples.Count;
            float v = 0f;
            for (int i = 0; i < count; i++)
            {
                var (p, dir) = samples[i];
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                if (innerHalfWidth > 0f)
                {
                    verts.Add(p - right * halfWidth); verts.Add(p - right * innerHalfWidth);
                    verts.Add(p + right * innerHalfWidth); verts.Add(p + right * halfWidth);
                    uvs.Add(new Vector2(0, v)); uvs.Add(new Vector2(1, v)); uvs.Add(new Vector2(0, v)); uvs.Add(new Vector2(1, v));
                }
                else
                {
                    verts.Add(p - right * halfWidth); verts.Add(p + right * halfWidth);
                    uvs.Add(new Vector2(0, v)); uvs.Add(new Vector2(1, v));
                }
                if (i + 1 < count) v += Vector3.Distance(p, samples[i + 1].p) / (halfWidth * 2f);
            }
            int stride = innerHalfWidth > 0f ? 4 : 2;
            int segs = closed ? count : count - 1;
            for (int i = 0; i < segs; i++)
            {
                int a = i * stride, b = ((i + 1) % count) * stride;
                if (innerHalfWidth > 0f)
                {
                    Quad(tris, a, a + 1, b, b + 1);
                    Quad(tris, a + 2, a + 3, b + 2, b + 3);
                }
                else
                {
                    Quad(tris, a, a + 1, b, b + 1);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            return go;
        }

        private static void Quad(List<int> tris, int a0, int a1, int b0, int b1)
        {
            tris.Add(a0); tris.Add(b0); tris.Add(a1);
            tris.Add(a1); tris.Add(b0); tris.Add(b1);
        }

        private void OnDrawGizmos()
        {
            if (!_showCheckpointGizmos || _def == null) return;
            for (int i = 0; i < _def.checkpoints.Length; i++)
            {
                foreach (var g in _def.checkpoints[i].gates)
                {
                    Gizmos.color = g.isShortcut ? new Color(1f, 0.5f, 0f, 0.6f) : (i == 0 ? Color.green : new Color(0.2f, 0.6f, 1f, 0.6f));
                    Gizmos.DrawWireSphere(g.position.ToUnity(), g.radius);
                }
            }
            Gizmos.color = Color.yellow;
            foreach (var b in _def.itemBoxes) Gizmos.DrawWireCube(b.position.ToUnity() + Vector3.up * 0.8f, Vector3.one);
        }
    }
}
