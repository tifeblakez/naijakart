using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NaijaKart.Unity.Input
{
    /// <summary>
    /// Wires the on-screen buttons (PRD §16) to the TiltInputProvider. Drift and item sit under the
    /// right thumb, optional look-back/horn on the left, mirrored for left-handed players. Buttons
    /// use pointer down/up so holding works with multi-touch. The screen stays clean: no steering
    /// wheel, no accelerator.
    /// </summary>
    public sealed class TouchControlsPresenter : MonoBehaviour
    {
        [SerializeField] private TiltInputProvider _input;
        [SerializeField] private HoldButton _drift;
        [SerializeField] private HoldButton _item;
        [SerializeField] private HoldButton _boost;
        [SerializeField] private HoldButton _lookBack;
        [SerializeField] private HoldButton _horn;
        [SerializeField] private HoldButton _steerLeft;
        [SerializeField] private HoldButton _steerRight;
        [SerializeField] private GameObject _touchZone;
        [SerializeField] private RectTransform _rightCluster;
        [SerializeField] private RectTransform _leftCluster;

        private void Awake()
        {
            if (_input == null) _input = FindFirstObjectByType<TiltInputProvider>();
            Bind(_drift, () => _input.DriftHeld = true, () => _input.DriftHeld = false);
            Bind(_item, () => _input.ItemPressed = true, null);
            Bind(_boost, () => _input.BoostPressed = true, null);
            Bind(_lookBack, () => _input.LookBackHeld = true, () => _input.LookBackHeld = false);
            Bind(_horn, () => _input.HornPressed = true, null);
            Bind(_steerLeft, () => _input.ButtonSteerDirection = -1, () => { if (_input.ButtonSteerDirection < 0) _input.ButtonSteerDirection = 0; });
            Bind(_steerRight, () => _input.ButtonSteerDirection = 1, () => { if (_input.ButtonSteerDirection > 0) _input.ButtonSteerDirection = 0; });
            ApplyLayout();
        }

        public void ApplyLayout()
        {
            SetSteeringMode(PlayerSettingsStore.SteeringMode);
            if (_rightCluster != null && _leftCluster != null && PlayerSettingsStore.LeftHandedLayout)
            {
                var r = _rightCluster.anchoredPosition;
                var l = _leftCluster.anchoredPosition;
                _rightCluster.anchoredPosition = new Vector2(-Mathf.Abs(r.x), r.y);
                _leftCluster.anchoredPosition = new Vector2(Mathf.Abs(l.x), l.y);
            }
        }

        private static void Bind(HoldButton b, System.Action down, System.Action up)
        {
            if (b == null) return;
            b.Down = down;
            b.Up = up;
        }

        public void SetBoostAvailable(bool available)
        {
            if (_boost != null) _boost.gameObject.SetActive(available);
        }

        public void SetItem(string itemId, bool ready)
        {
            if (_item != null) _item.SetHighlight(ready);
        }

        /// <summary>Touch steering shows the drag zone instead of steer buttons.</summary>
        public void SetSteeringMode(SteeringMode mode)
        {
            bool buttons = mode == SteeringMode.Buttons;
            if (_steerLeft != null) _steerLeft.gameObject.SetActive(buttons);
            if (_steerRight != null) _steerRight.gameObject.SetActive(buttons);
            if (_touchZone != null) _touchZone.SetActive(mode == SteeringMode.Touch);
        }
    }
}
