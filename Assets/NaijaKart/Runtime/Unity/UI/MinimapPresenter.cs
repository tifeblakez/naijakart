using System.Collections.Generic;
using NaijaKart.Core.Math;
using NaijaKart.Core.Track;
using NaijaKart.Unity.Race;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// Circular minimap (HUD bottom-left): the track centreline drawn as a UI line, racer dots on top,
    /// local player highlighted. Built once from the TrackDefinition; dots update from snapshots.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class MinimapPresenter : MonoBehaviour
    {
        [SerializeField] private RaceClientController _race;
        [SerializeField] private TrackLineGraphic _line;
        [SerializeField] private RectTransform _dotsRoot;
        [SerializeField] private Image _dotPrefab;
        [SerializeField] private float _padding = 12f;
        [SerializeField] private Color _localColor = new Color(1f, 0.78f, 0.1f);
        [SerializeField] private Color _otherColor = Color.white;

        private readonly Dictionary<string, Image> _dots = new Dictionary<string, Image>();
        private Vector2 _min, _max;
        private bool _built;

        private void Update()
        {
            if (_race == null || _race.Track == null) return;
            if (!_built) Build(_race.Track);
            var snap = _race.LatestSnapshot;
            if (snap == null) return;
            foreach (var p in snap.Participants)
            {
                if (!_dots.TryGetValue(p.PlayerId, out var dot))
                {
                    if (_dotPrefab == null || _dotsRoot == null) continue;
                    dot = Instantiate(_dotPrefab, _dotsRoot);
                    dot.color = p.PlayerId == _race.LocalPlayerId ? _localColor : _otherColor;
                    if (p.PlayerId == _race.LocalPlayerId) dot.transform.SetAsLastSibling();
                    _dots[p.PlayerId] = dot;
                }
                dot.rectTransform.anchoredPosition = ToMap(p.Position);
            }
        }

        private void Build(TrackGeometry geo)
        {
            _built = true;
            var pts = geo.Definition.centreline;
            _min = new Vector2(float.MaxValue, float.MaxValue);
            _max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in pts)
            {
                _min = Vector2.Min(_min, new Vector2(p.X, p.Z));
                _max = Vector2.Max(_max, new Vector2(p.X, p.Z));
            }
            if (_line != null)
            {
                var mapped = new List<Vector2>();
                foreach (var p in pts) mapped.Add(ToMap(p));
                mapped.Add(mapped[0]);
                _line.SetPoints(mapped);
            }
        }

        private Vector2 ToMap(Vec3 world)
        {
            var rt = (RectTransform)transform;
            float size = Mathf.Min(rt.rect.width, rt.rect.height) - _padding * 2f;
            Vector2 span = _max - _min;
            float scale = size / Mathf.Max(span.x, span.y, 1f);
            Vector2 centre = (_min + _max) * 0.5f;
            return new Vector2((world.X - centre.x) * scale, (world.Z - centre.y) * scale);
        }
    }

    /// <summary>Minimal UI polyline (OnPopulateMesh) so the minimap needs no textures.</summary>
    public sealed class TrackLineGraphic : MaskableGraphic
    {
        [SerializeField] private float _thickness = 3f;
        private readonly List<Vector2> _points = new List<Vector2>();

        public void SetPoints(List<Vector2> points)
        {
            _points.Clear();
            _points.AddRange(points);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i + 1 < _points.Count; i++)
            {
                Vector2 a = _points[i], b = _points[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 n = new Vector2(-dir.y, dir.x) * (_thickness * 0.5f);
                int v = vh.currentVertCount;
                vh.AddVert(a - n, color, Vector2.zero);
                vh.AddVert(a + n, color, Vector2.zero);
                vh.AddVert(b + n, color, Vector2.zero);
                vh.AddVert(b - n, color, Vector2.zero);
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v, v + 2, v + 3);
            }
        }
    }
}
