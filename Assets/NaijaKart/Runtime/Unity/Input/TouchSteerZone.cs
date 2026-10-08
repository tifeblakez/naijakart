using UnityEngine;
using UnityEngine.EventSystems;

namespace NaijaKart.Unity.Input
{
    /// <summary>
    /// "Drag your left thumb to steer": a transparent zone over the left part of the screen. Steer is
    /// the horizontal drag distance from the touch-down point, normalised by a radius. Only used when
    /// SteeringMode is Touch; tilt remains the primary model (PRD §15).
    /// </summary>
    public sealed class TouchSteerZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private TiltInputProvider _input;
        [SerializeField] private float _radiusPixels = 140f;
        [SerializeField] private RectTransform _thumbVisual;

        private Vector2 _origin;
        private bool _active;

        private void Awake()
        {
            if (_input == null) _input = FindFirstObjectByType<TiltInputProvider>();
            if (_thumbVisual != null) _thumbVisual.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData e)
        {
            _origin = e.position;
            _active = true;
            if (_thumbVisual != null) { _thumbVisual.gameObject.SetActive(true); _thumbVisual.position = e.position; }
            _input.TouchSteer = 0f;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_active) return;
            float dx = (e.position.x - _origin.x) / Mathf.Max(1f, _radiusPixels * (Screen.dpi > 0 ? Screen.dpi / 160f : 1f));
            _input.TouchSteer = Mathf.Clamp(dx, -1f, 1f);
            if (_thumbVisual != null) _thumbVisual.position = new Vector2(_origin.x + _input.TouchSteer * _radiusPixels, _origin.y);
        }

        public void OnPointerUp(PointerEventData e)
        {
            _active = false;
            _input.TouchSteer = 0f;
            if (_thumbVisual != null) _thumbVisual.gameObject.SetActive(false);
        }
    }
}
