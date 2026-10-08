using NaijaKart.Core.Simulation;
using NaijaKart.Unity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// Home screen (PRD §73) for the vertical slice: Play (quick race), Who Get Mouth (ranked),
    /// Private Room create/join, Practice (offline), settings entry. Full home IA lands in Phase 8–10.
    /// </summary>
    public sealed class HomePresenter : MonoBehaviour
    {
        [SerializeField] private Button _quickRace;
        [SerializeField] private Button _ranked;
        [SerializeField] private Button _createRoom;
        [SerializeField] private Button _joinRoom;
        [SerializeField] private TMP_InputField _roomCodeInput;
        [SerializeField] private Button _practice;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private TMP_Text _coins;
        [SerializeField] private TMP_Text _rank;
        [SerializeField] private TMP_InputField _displayName;
        [SerializeField] private LocalPracticeHost _practiceHost;
        [SerializeField] private string _lobbyScene = "Lobby";
        [SerializeField] private string _raceScene = "Race";

        private NetworkClient _net;

        private void Start()
        {
            _net = NetworkClient.Instance;
            if (_displayName != null)
            {
                _displayName.text = PlayerSettingsStore.DisplayName ?? "";
                _displayName.onEndEdit.AddListener(v => { PlayerSettingsStore.DisplayName = v; PlayerSettingsStore.Save(); });
            }
            if (_quickRace != null) _quickRace.onClick.AddListener(() => Online(() => { _net.JoinQueue(RaceMode.QuickRace); SetStatus(NaijaCopy.Searching); }));
            if (_ranked != null) _ranked.onClick.AddListener(() => Online(() => { _net.JoinQueue(RaceMode.Ranked); SetStatus(NaijaCopy.Searching); }));
            if (_createRoom != null) _createRoom.onClick.AddListener(() => Online(() => _net.CreateRoom(GameBootstrap.Content.TrackIds[0], GameBootstrap.Content.Game.raceRules.defaultLaps, true, true, null, null)));
            if (_joinRoom != null) _joinRoom.onClick.AddListener(() => Online(() => _net.JoinRoom(_roomCodeInput != null ? _roomCodeInput.text.Trim().ToUpperInvariant() : "", null, null)));
            if (_practice != null) _practice.onClick.AddListener(StartPractice);
            _net.Authenticated += OnAuthenticated;
            _net.RoomChanged += OnRoom;
            _net.ServerError += SetStatus;
            _net.ConnectionLost += r => SetStatus("CONNECTION LOST: " + r);
            Refresh();
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.Authenticated -= OnAuthenticated;
            _net.RoomChanged -= OnRoom;
            _net.ServerError -= SetStatus;
        }

        private void Online(System.Action action)
        {
            if (!_net.IsAuthenticated)
            {
                SetStatus("CONNECTING...");
                _net.ConnectToServer(PlayerSettingsStore.ServerHost, PlayerSettingsStore.ServerPort);
                _pending = action;
                return;
            }
            action();
        }

        private System.Action _pending;

        private void OnAuthenticated()
        {
            Refresh();
            var p = _pending;
            _pending = null;
            p?.Invoke();
        }

        private void OnRoom(Core.Net.RoomStateDto room)
        {
            if (room.Mode == RaceMode.Practice) return;
            if (!string.IsNullOrEmpty(_lobbyScene)) UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbyScene);
        }

        private void StartPractice()
        {
            if (_practiceHost == null) { SetStatus("No practice host in scene"); return; }
            _practiceHost.StartPractice();
            _net.Authenticated += StartPracticeRace;
        }

        private void StartPracticeRace()
        {
            _net.Authenticated -= StartPracticeRace;
            _net.CreateRoom(GameBootstrap.Content.TrackIds[0], 3, true, true, null, null, practice: true);
            UnityEngine.SceneManagement.SceneManager.LoadScene(_raceScene);
        }

        private void Refresh()
        {
            if (_coins != null) _coins.text = _net.IsAuthenticated ? $"{_net.CoinBalance:N0} COINS" : "";
            if (_rank != null) _rank.text = _net.IsAuthenticated ? (_net.RankId ?? "").ToUpperInvariant() : "";
        }

        private void SetStatus(string s)
        {
            if (_status != null) _status.text = s;
        }
    }
}
