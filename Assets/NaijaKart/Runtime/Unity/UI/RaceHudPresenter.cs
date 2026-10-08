using System.Collections;
using System.Collections.Generic;
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
    /// Race HUD matching the design: position "4TH /8" with a standings strip of the five racers around
    /// you, lap "2/3" + timer, shortcut banner, speed gauge in km/h with banked boost charges, four
    /// item slots (tap to use), drift button, and the event ticker with the racer's portrait. Reads
    /// snapshots and events only; never computes race state.
    /// </summary>
    public sealed class RaceHudPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RaceClientController _race;
        [SerializeField] private TiltInputProvider _input;
        [SerializeField] private TouchControlsPresenter _controls;
        [SerializeField] private ItemIconSet _icons;

        [Header("Top-left: position + standings")]
        [SerializeField] private TMP_Text _position;
        [SerializeField] private TMP_Text _positionTotal;
        [SerializeField] private StandingsRow[] _standings = new StandingsRow[5];

        [Header("Top: shortcut banner")]
        [SerializeField] private GameObject _shortcutBanner;
        [SerializeField] private TMP_Text _shortcutTitle;
        [SerializeField] private TMP_Text _shortcutHint;
        [SerializeField] private float _shortcutBannerSeconds = 3f;

        [Header("Top-right: lap + timer")]
        [SerializeField] private TMP_Text _lap;
        [SerializeField] private TMP_Text _timer;

        [Header("Bottom-right: speed gauge + charges")]
        [SerializeField] private TMP_Text _speedKmh;
        [SerializeField] private Image _speedArc;
        [SerializeField] private TMP_Text _boostCharges;
        [SerializeField] private GameObject _boostChargeBadge;

        [Header("Bottom: items + drift")]
        [SerializeField] private ItemSlotView[] _itemSlots = new ItemSlotView[4];
        [SerializeField] private Image _driftRing;
        [SerializeField] private Gradient _driftRingByLevel;

        [Header("Feedback")]
        [SerializeField] private GameObject _ticker;
        [SerializeField] private TMP_Text _tickerText;
        [SerializeField] private Image _tickerPortrait;
        [SerializeField] private CanvasGroup _blindOverlay;
        [SerializeField] private Image _lastmaPressureFill;
        [SerializeField] private GameObject _lastmaPanel;
        [SerializeField] private float _tickerSeconds = 1.6f;

        private NetworkClient _net;
        private int _laps;
        private Coroutine _tickerRoutine;
        private Coroutine _bannerRoutine;
        private float _topSpeedKmh;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.RaceEventReceived += OnEvent;
            _laps = _net.Room != null ? _net.Room.Laps : GameBootstrap.Content.Game.raceRules.defaultLaps;
            _topSpeedKmh = GameBootstrap.Content.Game.driving.maxTopSpeed * GameBootstrap.Content.Game.boost.maxMultiplier * 3.6f;
            if (_shortcutBanner != null) _shortcutBanner.SetActive(false);
            if (_lastmaPanel != null) _lastmaPanel.SetActive(false);
            if (_ticker != null) _ticker.SetActive(false);
            for (int i = 0; i < _itemSlots.Length; i++)
            {
                int slot = i;
                if (_itemSlots[i] != null) _itemSlots[i].Tapped = () => UseSlot(slot);
            }
        }

        private void OnDestroy()
        {
            if (_net != null) _net.RaceEventReceived -= OnEvent;
        }

        private void UseSlot(int slot)
        {
            if (_input == null) return;
            _input.ItemSlot = slot;
            _input.ItemPressed = true;
        }

        private void Update()
        {
            if (_race == null || !_race.HasLocal) return;
            var snap = _race.LatestSnapshot;
            var me = _race.LocalSnapshot;
            var predicted = _race.PredictedLocalState;

            if (_position != null) _position.text = NaijaCopy.Ordinal(me.RacePosition);
            if (_positionTotal != null) _positionTotal.text = "/" + snap.Participants.Length;
            UpdateStandings(snap, me);

            if (_lap != null) _lap.text = $"LAP {Mathf.Min(me.Lap + 1, _laps)}/{_laps}";
            if (_timer != null) _timer.text = NaijaCopy.FormatTime(snap.Time);

            float kmh = predicted.Speed * 3.6f;
            if (_speedKmh != null) _speedKmh.text = Mathf.RoundToInt(kmh).ToString();
            if (_speedArc != null) _speedArc.fillAmount = Mathf.Clamp01(kmh / _topSpeedKmh);
            if (_boostCharges != null) _boostCharges.text = predicted.BoostCharges.ToString();
            if (_boostChargeBadge != null) _boostChargeBadge.SetActive(predicted.BoostCharges > 0);
            _controls?.SetBoostAvailable(predicted.BoostCharges > 0);

            var ids = me.HeldItemIds;
            var ready = me.ItemsReady;
            for (int i = 0; i < _itemSlots.Length; i++)
            {
                if (_itemSlots[i] == null) continue;
                string id = ids != null && i < ids.Length ? ids[i] : null;
                bool r = ready != null && i < ready.Length && ready[i];
                _itemSlots[i].Set(id, DisplayName(id), _icons != null && id != null ? _icons.SpriteFor(id) : null, r);
            }

            if (_driftRing != null)
            {
                float max = GameBootstrap.Content.Game.drift.levelThresholds[2];
                _driftRing.fillAmount = predicted.IsDrifting ? Mathf.Clamp01(predicted.DriftCharge / max) : 0f;
                if (_driftRingByLevel != null) _driftRing.color = _driftRingByLevel.Evaluate(predicted.IsDrifting ? Mathf.Clamp01(predicted.DriftCharge / max) : 0f);
            }

            bool lastmaActive = me.LastmaPhase == Core.Lastma.LastmaPhase.Warning || me.LastmaPhase == Core.Lastma.LastmaPhase.Pursuit;
            if (_lastmaPanel != null) _lastmaPanel.SetActive(lastmaActive);
            if (_lastmaPressureFill != null) _lastmaPressureFill.fillAmount = me.LastmaPressure;
            if (_blindOverlay != null) _blindOverlay.alpha = Mathf.Clamp01(me.BlindTimeRemaining / 0.5f) * 0.92f;
        }

        /// <summary>Five rows: the racers around you by live position, you highlighted.</summary>
        private void UpdateStandings(RaceSnapshot snap, ParticipantSnapshot me)
        {
            if (_standings == null || _standings.Length == 0) return;
            var ordered = new List<ParticipantSnapshot>(snap.Participants);
            ordered.Sort((a, b) => a.RacePosition.CompareTo(b.RacePosition));
            int myIndex = ordered.FindIndex(p => p.PlayerId == me.PlayerId);
            int rows = Mathf.Min(_standings.Length, ordered.Count);
            int start = Mathf.Clamp(myIndex - rows / 2, 0, Mathf.Max(0, ordered.Count - rows));
            for (int i = 0; i < _standings.Length; i++)
            {
                var row = _standings[i];
                if (row == null) continue;
                int idx = start + i;
                if (idx >= ordered.Count) { row.gameObject.SetActive(false); continue; }
                row.gameObject.SetActive(true);
                var p = ordered[idx];
                row.Set(p.RacePosition, p.PlayerId == me.PlayerId ? "You" : _race.DisplayNameOf(p.PlayerId), p.PlayerId == me.PlayerId, _race.ColorOf(p.PlayerId));
            }
        }

        private static string DisplayName(string itemId)
        {
            if (itemId == null) return "";
            foreach (var i in GameBootstrap.Content.Items.items) if (i.id == itemId) return i.displayName;
            return itemId;
        }

        private void OnEvent(RaceEvent e)
        {
            if (e.Type == RaceEventType.GatePassed && e.PlayerId == _net.PlayerId && e.Payload == "shortcut")
            {
                ShowShortcutBanner(e);
                return;
            }
            string text = NaijaCopy.ForEvent(e, _net.PlayerId);
            if (text == null) return;
            if (_tickerRoutine != null) StopCoroutine(_tickerRoutine);
            _tickerRoutine = StartCoroutine(ShowTicker(text));
            if (e.Type == RaceEventType.Collision && e.PlayerId == _net.PlayerId && PlayerSettingsStore.Vibration)
            {
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
            }
        }

        private void ShowShortcutBanner(RaceEvent e)
        {
            if (_shortcutBanner == null) return;
            string label = _race.ShortcutLabel(e.IntValue) ?? "SHORTCUT";
            if (_shortcutTitle != null) _shortcutTitle.text = string.Format(NaijaCopy.Shortcut, label.ToUpperInvariant());
            if (_shortcutHint != null) _shortcutHint.text = NaijaCopy.ShortcutHint;
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(ShowFor(_shortcutBanner, _shortcutBannerSeconds));
        }

        private IEnumerator ShowFor(GameObject go, float seconds)
        {
            go.SetActive(true);
            yield return new WaitForSeconds(seconds);
            go.SetActive(false);
        }

        private IEnumerator ShowTicker(string text)
        {
            if (_ticker == null || _tickerText == null) yield break;
            _tickerText.text = text;
            _ticker.SetActive(true);
            yield return new WaitForSeconds(_tickerSeconds);
            _ticker.SetActive(false);
        }
    }
}
