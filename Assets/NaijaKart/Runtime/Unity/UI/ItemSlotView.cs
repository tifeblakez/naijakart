using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>One of the four HUD item slots: icon in a coloured ring, label below, tap to use.</summary>
    public sealed class ItemSlotView : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField] private Image _icon;
        [SerializeField] private Image _ring;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private float _emptyAlpha = 0.25f;
        [SerializeField] private float _rollingAlpha = 0.6f;

        public System.Action Tapped;
        private bool _hasItem;

        public void Set(string itemId, string label, Sprite icon, bool ready)
        {
            _hasItem = itemId != null;
            if (_icon != null) { _icon.sprite = icon; _icon.enabled = icon != null; }
            if (_label != null) _label.text = _hasItem ? label : "";
            if (_group != null) _group.alpha = !_hasItem ? _emptyAlpha : ready ? 1f : _rollingAlpha;
            if (_ring != null) _ring.enabled = _hasItem;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_hasItem) Tapped?.Invoke();
        }
    }
}
