using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NaijaKart.Unity.Input
{
    /// <summary>Press/release button for racing controls (uGUI Button only fires on click).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action Down;
        public System.Action Up;
        [SerializeField] private Graphic _graphic;
        [SerializeField] private float _pressedAlpha = 1f;
        [SerializeField] private float _idleAlpha = 0.6f;
        private bool _held;
        private bool _highlight;

        private void Awake()
        {
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            SetAlpha(_idleAlpha);
        }

        public void OnPointerDown(PointerEventData e) { _held = true; SetAlpha(_pressedAlpha); Down?.Invoke(); }
        public void OnPointerUp(PointerEventData e) { Release(); }
        public void OnPointerExit(PointerEventData e) { Release(); }

        public void SetHighlight(bool on)
        {
            _highlight = on;
            if (!_held) SetAlpha(on ? _pressedAlpha : _idleAlpha);
        }

        private void Release()
        {
            if (!_held) return;
            _held = false;
            SetAlpha(_highlight ? _pressedAlpha : _idleAlpha);
            Up?.Invoke();
        }

        private void SetAlpha(float a)
        {
            if (_graphic == null) return;
            var c = _graphic.color;
            c.a = a;
            _graphic.color = c;
        }
    }
}
