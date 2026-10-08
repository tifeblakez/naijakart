using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Unity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>Lobby (PRD §76): members with vehicle/character/level/rank/ready, loadout, invite code, start.</summary>
    public sealed class LobbyPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _roomCode;
        [SerializeField] private TMP_Text _trackName;
        [SerializeField] private Transform _memberList;
        [SerializeField] private LobbyMemberRow _rowPrefab;
        [SerializeField] private Button _readyButton;
        [SerializeField] private Button _startButton;
        [SerializeField] private Toggle _fillWithBots;
        [SerializeField] private Button _shareButton;
        [SerializeField] private TMP_Dropdown _vehicleDropdown;
        [SerializeField] private TMP_Dropdown _characterDropdown;
        [SerializeField] private string _raceScene = "Race";

        private NetworkClient _net;
        private bool _ready;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.RoomChanged += OnRoom;
            if (_readyButton != null) _readyButton.onClick.AddListener(() => { _ready = !_ready; _net.SetReady(_ready); });
            if (_startButton != null) _startButton.onClick.AddListener(() => _net.StartRoom(_fillWithBots != null && _fillWithBots.isOn));
            if (_shareButton != null) _shareButton.onClick.AddListener(Share);
            PopulateDropdowns();
            if (_net.Room != null) OnRoom(_net.Room);
        }

        private void OnDestroy()
        {
            if (_net != null) _net.RoomChanged -= OnRoom;
        }

        private void PopulateDropdowns()
        {
            var content = GameBootstrap.Content;
            if (_vehicleDropdown != null)
            {
                _vehicleDropdown.ClearOptions();
                foreach (var v in content.Vehicles.vehicles) _vehicleDropdown.options.Add(new TMP_Dropdown.OptionData(v.displayName));
                _vehicleDropdown.onValueChanged.AddListener(_ => SendLoadout());
            }
            if (_characterDropdown != null)
            {
                _characterDropdown.ClearOptions();
                foreach (var c in content.Characters.characters) _characterDropdown.options.Add(new TMP_Dropdown.OptionData(c.displayName));
                _characterDropdown.onValueChanged.AddListener(_ => SendLoadout());
            }
        }

        private void SendLoadout()
        {
            var content = GameBootstrap.Content;
            string v = _vehicleDropdown != null ? content.Vehicles.vehicles[_vehicleDropdown.value].id : null;
            string c = _characterDropdown != null ? content.Characters.characters[_characterDropdown.value].id : null;
            _net.SelectLoadout(v, c);
        }

        private void OnRoom(RoomStateDto room)
        {
            if (_panel != null) _panel.SetActive(room.State == RaceState.Lobby || room.State == RaceState.Waiting);
            if (_roomCode != null) _roomCode.text = room.RoomCode;
            if (_trackName != null) _trackName.text = GameBootstrap.Content.GetTrack(room.TrackId)?.displayName ?? room.TrackId;
            if (_startButton != null) _startButton.gameObject.SetActive(room.HostPlayerId == _net.PlayerId && room.Mode == Core.Simulation.RaceMode.PrivateRoom);
            if (_memberList != null && _rowPrefab != null)
            {
                foreach (Transform child in _memberList) Destroy(child.gameObject);
                foreach (var m in room.Members) Instantiate(_rowPrefab, _memberList).Bind(m, m.PlayerId == _net.PlayerId);
            }
            if ((room.State == RaceState.Loading || room.State == RaceState.Countdown) && !string.IsNullOrEmpty(_raceScene)
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != _raceScene)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_raceScene);
            }
        }

        /// <summary>WhatsApp-friendly invite (PRD §8). Uses the native share sheet where a plugin is present; falls back to clipboard.</summary>
        private void Share()
        {
            if (_net.Room == null) return;
            string text = NaijaCopy.RoomInvite(PlayerSettingsStore.DisplayName ?? "Your friend", _net.Room.RoomCode);
            GUIUtility.systemCopyBuffer = text;
            Debug.Log("[NK:share] " + text);
        }
    }
}
