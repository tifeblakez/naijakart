using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Unity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// "PULL OVER! LASTMA DON CATCH YOU": three cards on a countdown. TAKE PENALTY (default, free),
    /// PAY FINE (earned Coins only, disabled when unaffordable or in Ranked), CALL FOR BAIL (friends
    /// online count; disabled in Ranked). No choice when the timer ends = the server applies the
    /// penalty. Also the "X needs bail!" toast for other players.
    /// </summary>
    public sealed class LastmaPromptPresenter : MonoBehaviour
    {
        [Header("Dialog")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _subtitle;
        [SerializeField] private TMP_Text _instruction;
        [SerializeField] private Image _countdownFill;
        [SerializeField] private TMP_Text _countdownText;
        [Header("Take penalty")]
        [SerializeField] private Button _penaltyButton;
        [SerializeField] private TMP_Text _penaltyHint;
        [SerializeField] private TMP_Text _penaltyFooter;
        [Header("Pay fine")]
        [SerializeField] private Button _payButton;
        [SerializeField] private TMP_Text _payHint;
        [SerializeField] private TMP_Text _payBalance;
        [SerializeField] private TMP_Text _payFooter;
        [Header("Call for bail")]
        [SerializeField] private Button _bailButton;
        [SerializeField] private TMP_Text _bailHint;
        [SerializeField] private TMP_Text _bailOnline;
        [Header("Footer + penalty state")]
        [SerializeField] private TMP_Text _rulesFooter;
        [SerializeField] private GameObject _servingPanel;
        [SerializeField] private TMP_Text _servingText;
        [Header("Bail toast (other players)")]
        [SerializeField] private GameObject _bailToast;
        [SerializeField] private TMP_Text _bailToastText;
        [SerializeField] private Button _bailToastButton;
        [SerializeField] private GameObject _lockedUpPanel;

        private NetworkClient _net;
        private float _deadline;
        private float _decisionSeconds = 3f;
        private string _bailTarget;
        private bool _choiceMade;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.RaceEventReceived += OnEvent;
            _net.LastmaOptionsReceived += OnOptions;
            _net.BailRequested += OnBailRequested;
            if (_penaltyButton != null) _penaltyButton.onClick.AddListener(() => Choose(() => _net.TakePenalty()));
            if (_payButton != null) _payButton.onClick.AddListener(() => Choose(() => _net.PayFine()));
            if (_bailButton != null) _bailButton.onClick.AddListener(() => { _net.RequestBail(); _bailButton.interactable = false; });
            if (_bailToastButton != null) _bailToastButton.onClick.AddListener(() => { if (_bailTarget != null) _net.PayBail(_bailTarget); Hide(_bailToast); });
            if (_title != null) _title.text = NaijaCopy.LastmaWarning;
            if (_subtitle != null) _subtitle.text = NaijaCopy.LastmaDonCatchYou;
            Hide(_panel); Hide(_bailToast); Hide(_lockedUpPanel); Hide(_servingPanel);
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.RaceEventReceived -= OnEvent;
            _net.LastmaOptionsReceived -= OnOptions;
            _net.BailRequested -= OnBailRequested;
        }

        private void Choose(System.Action send)
        {
            if (_choiceMade) return;
            _choiceMade = true;
            send();
            if (_penaltyButton != null) _penaltyButton.interactable = false;
            if (_payButton != null) _payButton.interactable = false;
        }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            float remaining = Mathf.Max(0f, _deadline - Time.time);
            if (_countdownText != null) _countdownText.text = Mathf.CeilToInt(remaining).ToString();
            if (_countdownFill != null) _countdownFill.fillAmount = _decisionSeconds > 0f ? remaining / _decisionSeconds : 0f;
        }

        private void OnOptions(LastmaOptionsDto o)
        {
            _choiceMade = false;
            _decisionSeconds = o.DecisionSeconds;
            _deadline = Time.time + o.DecisionSeconds;
            if (_instruction != null) _instruction.text = string.Format(NaijaCopy.PickHowYouRecover, Mathf.CeilToInt(o.DecisionSeconds));
            if (_penaltyHint != null) _penaltyHint.text = string.Format(NaijaCopy.TakePenaltyHint, Mathf.CeilToInt(o.PenaltySeconds));
            if (_penaltyFooter != null) _penaltyFooter.text = NaijaCopy.AlwaysFree;
            if (_penaltyButton != null) _penaltyButton.interactable = true;
            if (_payHint != null) _payHint.text = string.Format(NaijaCopy.PayFineHint, o.FineAmount.ToString("N0"), Mathf.CeilToInt(o.FineResumeSeconds));
            if (_payBalance != null) _payBalance.text = _net.CoinBalance.ToString("N0");
            if (_payFooter != null) _payFooter.text = NaijaCopy.EarnedOnly;
            if (_payButton != null) _payButton.interactable = o.FinesAllowed && o.CanAffordFine;
            if (_bailHint != null) _bailHint.text = NaijaCopy.CallForBailHint;
            if (_bailOnline != null) _bailOnline.text = string.Format(NaijaCopy.Online, o.FriendsOnline);
            if (_bailButton != null) _bailButton.interactable = o.BailAllowed && o.FriendsOnline > 0;
            if (_rulesFooter != null) _rulesFooter.text = o.Ranked ? NaijaCopy.RankedSamePenalty : "";
            Hide(_servingPanel);
            Show(_panel);
        }

        private void OnEvent(RaceEvent e)
        {
            bool mine = e.PlayerId == _net.PlayerId;
            switch (e.Type)
            {
                case RaceEventType.LastmaPenaltyTaken when mine:
                    Hide(_panel);
                    if (_servingText != null) _servingText.text = NaijaCopy.TakePenalty;
                    Show(_servingPanel);
                    break;
                case RaceEventType.LastmaPenaltyServed when mine:
                case RaceEventType.LastmaFinePaid when mine:
                case RaceEventType.LastmaBailed when mine:
                    Hide(_panel);
                    Hide(_servingPanel);
                    break;
                case RaceEventType.LastmaArrested when mine:
                    Hide(_panel);
                    Hide(_servingPanel);
                    Show(_lockedUpPanel);
                    break;
                case RaceEventType.LastmaBailed:
                case RaceEventType.LastmaFinePaid:
                case RaceEventType.LastmaPenaltyServed:
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
