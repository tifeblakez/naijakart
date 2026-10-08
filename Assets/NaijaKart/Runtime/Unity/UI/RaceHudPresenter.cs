using System.Collections;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Unity.Input;
using NaijaKart.Unity.Net;
using NaijaKart.Unity.Race;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// Race HUD (PRD §74, §75): position, lap, timer, item slot, drift/boost state, LASTMA pressure and
    /// a non-blocking event ticker. Reads snapshots and events; never computes race state itself.
    /// </summary>
    public sealed class RaceHudPresenter : MonoBehaviour
    {
        [SerializeField] private RaceClientController _race;
        [SerializeField] private TouchControlsPresenter _controls;
        [SerializeField] private TMP_Text _position;
        [SerializeField] private TMP_Text _lap;
        [SerializeField] private TMP_Text _timer;
        [SerializeField] private TMP_Text _itemName;
        [SerializeField] private Image _itemIcon;
        [SerializeField] private Image _driftChargeFill;
        [SerializeField] private Image _lastmaPressureFill;
        [SerializeField] private GameObject _lastmaPanel;
        [SerializeField] private TMP_Text _eventTicker;
        [SerializeField] private CanvasGroup _blindOverlay;
        [SerializeField] private float _tickerSeconds = 1.6f;
        [SerializeField] private ItemIconSet _icons;

        private NetworkClient _net;
        private int _laps;
        private Coroutine _ticker;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.RaceEventReceived += OnEvent;
            _laps = _net.Room != null ? _net.Room.Laps : GameBootstrap.Content.Game.raceRules.defaultLaps;
            if (_lastmaPanel != null) _lastmaPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_net != null) _net.RaceEventReceived -= OnEvent;
        }

        private void Update()
        {
            if (_race == null || !_race.HasLocal) return;
            var snap = _race.LatestSnapshot;
            var me = _race.LocalSnapshot;
            if (_position != null) _position.text = NaijaCopy.Ordinal(me.RacePosition);
            if (_lap != null) _lap.text = $"LAP {Mathf.Min(me.Lap + 1, _laps)}/{_laps}";
            if (_timer != null) _timer.text = NaijaCopy.FormatTime(snap.Time);
            if (_itemName != null) _itemName.text = me.HeldItemId != null ? DisplayName(me.HeldItemId) : "";
            if (_itemIcon != null)
            {
                var sprite = _icons != null && me.HeldItemId != null ? _icons.SpriteFor(me.HeldItemId) : null;
                _itemIcon.sprite = sprite;
                _itemIcon.enabled = sprite != null;
                _itemIcon.color = me.ItemReady ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            }
            _controls?.SetItem(me.HeldItemId, me.ItemReady);
            var predicted = _race.PredictedLocalState;
            if (_driftChargeFill != null)
            {
                float max = GameBootstrap.Content.Game.drift.levelThresholds[2];
                _driftChargeFill.fillAmount = predicted.IsDrifting ? Mathf.Clamp01(predicted.DriftCharge / max) : 0f;
                _driftChargeFill.color = predicted.DriftLevel switch
                {
                    Core.Vehicle.DriftLevel.Blue => new Color(0.3f, 0.6f, 1f),
                    Core.Vehicle.DriftLevel.Orange => new Color(1f, 0.6f, 0.1f),
                    Core.Vehicle.DriftLevel.Purple => new Color(0.7f, 0.3f, 1f),
                    _ => Color.white
                };
            }
            bool lastmaActive = me.LastmaPhase == Core.Lastma.LastmaPhase.Warning || me.LastmaPhase == Core.Lastma.LastmaPhase.Pursuit;
            if (_lastmaPanel != null) _lastmaPanel.SetActive(lastmaActive);
            if (_lastmaPressureFill != null) _lastmaPressureFill.fillAmount = me.LastmaPressure;
            if (_blindOverlay != null) _blindOverlay.alpha = Mathf.Clamp01(me.BlindTimeRemaining / 0.5f) * 0.92f;
        }

        private string DisplayName(string itemId)
        {
            foreach (var i in GameBootstrap.Content.Items.items) if (i.id == itemId) return i.displayName;
            return itemId;
        }

        private void OnEvent(RaceEvent e)
        {
            string text = NaijaCopy.ForEvent(e, _net.PlayerId);
            if (text == null) return;
            if (_ticker != null) StopCoroutine(_ticker);
            _ticker = StartCoroutine(ShowTicker(text));
            if (e.Type == RaceEventType.Collision && e.PlayerId == _net.PlayerId && PlayerSettingsStore.Vibration)
            {
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
            }
        }

        private IEnumerator ShowTicker(string text)
        {
            if (_eventTicker == null) yield break;
            _eventTicker.text = text;
            _eventTicker.gameObject.SetActive(true);
            yield return new WaitForSeconds(_tickerSeconds);
            _eventTicker.gameObject.SetActive(false);
        }
    }
}
