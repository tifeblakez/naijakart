using NaijaKart.Core.Race;
using NaijaKart.Unity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// The pulled-over dialog (PRD §31–§34): shows the fine, a countdown, PAY FINE (enabled only if
    /// the player can afford it) and CALL FOR BAIL; and the "X needs bail!" toast for other players
    /// with a one-tap BAIL button. The server validates everything; this only sends intents.
    /// </summary>
    public sealed class LastmaPromptPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _fineText;
        [SerializeField] private TMP_Text _countdown;
        [SerializeField] private Button _payButton;
        [SerializeField] private Button _bailButton;
        [SerializeField] private GameObject _bailToast;
        [SerializeField] private TMP_Text _bailToastText;
        [SerializeField] private Button _bailToastButton;
        [SerializeField] private GameObject _lockedUpPanel;

        private NetworkClient _net;
        private float _deadline;
        private string _bailTarget;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.RaceEventReceived += OnEvent;
            _net.BailRequested += OnBailRequested;
            if (_payButton != null) _payButton.onClick.AddListener(() => { _net.PayFine(); });
            if (_bailButton != null) _bailButton.onClick.AddListener(() => { _net.RequestBail(); _bailButton.interactable = false; });
            if (_bailToastButton != null) _bailToastButton.onClick.AddListener(() => { if (_bailTarget != null) _net.PayBail(_bailTarget); Hide(_bailToast); });
            Hide(_panel); Hide(_bailToast); Hide(_lockedUpPanel);
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.RaceEventReceived -= OnEvent;
            _net.BailRequested -= OnBailRequested;
        }

        private void Update()
        {
            if (_panel != null && _panel.activeSelf && _countdown != null)
            {
                _countdown.text = Mathf.CeilToInt(Mathf.Max(0f, _deadline - Time.time)).ToString();
            }
        }

        private void OnEvent(RaceEvent e)
        {
            bool mine = e.PlayerId == _net.PlayerId;
            switch (e.Type)
            {
                case RaceEventType.LastmaCaught when mine:
                    long fine = (long)e.FloatValue;
                    if (_title != null) _title.text = NaijaCopy.LastmaCaught;
                    if (_fineText != null) _fineText.text = $"FINE: {fine:N0} COINS   (YOU HAVE {_net.CoinBalance:N0})";
                    if (_payButton != null) _payButton.interactable = _net.CoinBalance >= fine;
                    if (_bailButton != null) _bailButton.interactable = true;
                    _deadline = Time.time + GameBootstrap.Content.Game.lastma.fineDecisionSeconds;
                    Show(_panel);
                    break;
                case RaceEventType.LastmaFinePaid when mine:
                case RaceEventType.LastmaBailed when mine:
                    Hide(_panel);
                    break;
                case RaceEventType.LastmaArrested when mine:
                    Hide(_panel);
                    Show(_lockedUpPanel);
                    break;
                case RaceEventType.LastmaBailed:
                case RaceEventType.LastmaFinePaid:
                case RaceEventType.LastmaArrested:
                    if (e.PlayerId == _bailTarget) Hide(_bailToast);
                    break;
            }
        }

        private void OnBailRequested(string playerId, long amount, string text)
        {
            _bailTarget = playerId;
            if (_bailToastText != null) _bailToastText.text = $"{text}  ({amount:N0} COINS)";
            if (_bailToastButton != null) _bailToastButton.interactable = _net.CoinBalance >= amount;
            Show(_bailToast);
        }

        private static void Show(GameObject go) { if (go != null) go.SetActive(true); }
        private static void Hide(GameObject go) { if (go != null) go.SetActive(false); }
    }
}
