using System;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using UnityEngine;

namespace NaijaKart.Unity.Net
{
    /// <summary>
    /// The client's single connection to the authoritative server. Owns the transport, the session
    /// handshake (Hello/Welcome with retry), reconnect with the same identity, and fans server
    /// messages out to the race/lobby presenters. Lives for the whole app session.
    /// </summary>
    public sealed class NetworkClient : MonoBehaviour
    {
        public static NetworkClient Instance { get; private set; }

        [SerializeField] private float _helloRetrySeconds = 1.5f;
        [SerializeField] private float _reconnectDelaySeconds = 2f;
        [SerializeField] private int _maxReconnectAttempts = 10;

        private IClientTransport _transport;
        private float _sinceHello;
        private float _reconnectTimer;
        private int _reconnectAttempts;
        private bool _wantConnected;

        public bool IsConnected => _transport != null && _transport.IsConnected;
        public bool IsAuthenticated { get; private set; }
        public long CoinBalance { get; private set; }
        public string RankId { get; private set; }
        public RoomStateDto Room { get; private set; }
        public RaceSnapshot LatestSnapshot { get; private set; }
        public RaceResults LatestResults { get; private set; }
        public SettledRewardDto[] LatestRewards { get; private set; }
        public float LastRttMs { get; private set; }
        public string PlayerId => PlayerSettingsStore.PlayerId;

        public event Action Authenticated;
        public event Action<string> ConnectionLost;
        public event Action<RoomStateDto> RoomChanged;
        public event Action<RaceSnapshot> SnapshotReceived;
        public event Action<RaceEvent> RaceEventReceived;
        public event Action<RaceResults, SettledRewardDto[]> ResultsReceived;
        public event Action<string, long, string> BailRequested;
        public event Action<LastmaOptionsDto> LastmaOptionsReceived;
        public event Action<ServerEnvelope> GarageReceived;
        public event Action<ServerEnvelope> ChallengeCompleted;
        public event Action<string, string> LiveEvent;
        public long PremiumBalance { get; private set; }
        public event Action<string> ServerError;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Connects with the given transport (TCP to a server, or loopback for local practice).</summary>
        public void Connect(IClientTransport transport)
        {
            if (_transport != null) Teardown();
            _transport = transport;
            _transport.Connected += OnConnected;
            _transport.Disconnected += OnDisconnected;
            _transport.MessageReceived += OnMessage;
            _wantConnected = true;
            _reconnectAttempts = 0;
            _transport.Connect();
        }

        public void ConnectToServer(string host, int port) => Connect(new TcpJsonClientTransport(host, port));

        public void Disconnect()
        {
            _wantConnected = false;
            Teardown();
        }

        private void Teardown()
        {
            if (_transport == null) return;
            _transport.Connected -= OnConnected;
            _transport.Disconnected -= OnDisconnected;
            _transport.MessageReceived -= OnMessage;
            _transport.Disconnect();
            (_transport as IDisposable)?.Dispose();
            _transport = null;
            IsAuthenticated = false;
        }

        private void Update()
        {
            if (_transport == null) return;
            _transport.PumpIncoming();
            if (_transport.IsConnected && !IsAuthenticated)
            {
                _sinceHello += Time.unscaledDeltaTime;
                if (_sinceHello >= _helloRetrySeconds) SendHello();
            }
            else if (!_transport.IsConnected && _wantConnected && _reconnectAttempts < _maxReconnectAttempts)
            {
                _reconnectTimer -= Time.unscaledDeltaTime;
                if (_reconnectTimer <= 0f)
                {
                    _reconnectTimer = _reconnectDelaySeconds;
                    _reconnectAttempts++;
                    Debug.Log($"[NK:net] reconnect attempt {_reconnectAttempts}");
                    _transport.Connect();
                }
            }
        }

        private void OnConnected()
        {
            _reconnectAttempts = 0;
            SendHello();
        }

        private void SendHello()
        {
            _sinceHello = 0f;
            Send(new ClientEnvelope { Kind = ClientMessageKind.Hello, DisplayName = PlayerSettingsStore.DisplayName ?? PlayerSettingsStore.PlayerId });
        }

        private void OnDisconnected(string reason)
        {
            IsAuthenticated = false;
            _reconnectTimer = 0f;
            ConnectionLost?.Invoke(reason);
        }

        public void Send(ClientEnvelope env)
        {
            if (_transport == null || !_transport.IsConnected) return;
            env.PlayerId = PlayerId;
            env.ClientTimeMs = NowMs();
            _transport.Send(env);
        }

        public void SendInput(Core.Input.PlayerInputFrame frame) => Send(new ClientEnvelope { Kind = ClientMessageKind.Input, Input = frame });
        public void JoinQueue(RaceMode mode) => Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = mode });
        public void LeaveQueue() => Send(new ClientEnvelope { Kind = ClientMessageKind.LeaveQueue });
        public void CreateRoom(string trackId, int laps, bool items, bool lastma, string vehicleId, string characterId, bool practice = false) =>
            Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = practice ? RaceMode.Practice : RaceMode.PrivateRoom, TrackId = trackId, Laps = laps, ItemsEnabled = items, LastmaEnabled = lastma, VehicleId = vehicleId, CharacterId = characterId });
        public void JoinRoom(string code, string vehicleId, string characterId) => Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = code, VehicleId = vehicleId, CharacterId = characterId });
        public void LeaveRoom() => Send(new ClientEnvelope { Kind = ClientMessageKind.LeaveRoom });
        public void SelectLoadout(string vehicleId, string characterId) => Send(new ClientEnvelope { Kind = ClientMessageKind.SelectLoadout, VehicleId = vehicleId, CharacterId = characterId });
        public void SetReady(bool ready) => Send(new ClientEnvelope { Kind = ClientMessageKind.Ready, Flag = ready });
        public void StartRoom(bool fillWithBots) => Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = fillWithBots });
        public void PayFine() => Send(new ClientEnvelope { Kind = ClientMessageKind.PayFine });
        public void TakePenalty() => Send(new ClientEnvelope { Kind = ClientMessageKind.TakePenalty });
        public void GetGarage() => Send(new ClientEnvelope { Kind = ClientMessageKind.GetGarage });
        public void PurchaseVehicle(string id) => Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseVehicle, VehicleId = id });
        public void PurchaseCharacter(string id) => Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseCharacter, CharacterId = id });
        public void GetChallenges() => Send(new ClientEnvelope { Kind = ClientMessageKind.GetChallenges });
        public void GetProfile(string playerId = null) => Send(new ClientEnvelope { Kind = ClientMessageKind.GetProfile, TargetPlayerId = playerId });
        public void GetLeaderboard(string metric, bool friendsOnly) => Send(new ClientEnvelope { Kind = ClientMessageKind.GetLeaderboard, Text = metric, Flag = friendsOnly });
        public void GetRivalries() => Send(new ClientEnvelope { Kind = ClientMessageKind.GetRivalries });
        public void AddFriend(string playerId) => Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = playerId });
        public void RequestBail() => Send(new ClientEnvelope { Kind = ClientMessageKind.RequestBail });
        public void PayBail(string targetPlayerId) => Send(new ClientEnvelope { Kind = ClientMessageKind.PayBail, TargetPlayerId = targetPlayerId });
        public void VoteRematch() => Send(new ClientEnvelope { Kind = ClientMessageKind.VoteRematch });
        public void Ping() => Send(new ClientEnvelope { Kind = ClientMessageKind.Ping });

        private void OnMessage(ServerEnvelope m)
        {
            switch (m.Kind)
            {
                case ServerMessageKind.Welcome:
                    IsAuthenticated = true;
                    CoinBalance = m.Amount;
                    PremiumBalance = m.PremiumBalance;
                    RankId = m.Text;
                    Authenticated?.Invoke();
                    break;
                case ServerMessageKind.Error:
                    Debug.LogWarning("[NK:net] server error: " + m.Error);
                    ServerError?.Invoke(m.Error);
                    break;
                case ServerMessageKind.RoomState:
                    Room = m.Room;
                    RoomChanged?.Invoke(m.Room);
                    break;
                case ServerMessageKind.RaceSnapshot:
                    LatestSnapshot = m.Snapshot;
                    SnapshotReceived?.Invoke(m.Snapshot);
                    break;
                case ServerMessageKind.RaceEvent:
                    RaceEventReceived?.Invoke(m.Event);
                    break;
                case ServerMessageKind.RaceResults:
                    // The server re-sends results while the room idles; only the first copy matters.
                    if (LatestResults != null && m.Results != null && LatestResults.RaceId == m.Results.RaceId) break;
                    LatestResults = m.Results;
                    LatestRewards = m.Rewards;
                    foreach (var r in m.Rewards ?? Array.Empty<SettledRewardDto>())
                        if (r.PlayerId == PlayerId) { CoinBalance = r.CoinBalance; RankId = r.RankIdAfter; }
                    ResultsReceived?.Invoke(m.Results, m.Rewards);
                    break;
                case ServerMessageKind.BailRequest:
                    BailRequested?.Invoke(m.PlayerId, m.Amount, m.Text);
                    break;
                case ServerMessageKind.LastmaOptions:
                    LastmaOptionsReceived?.Invoke(m.LastmaOptions);
                    break;
                case ServerMessageKind.Garage:
                    CoinBalance = m.Amount;
                    PremiumBalance = m.PremiumBalance;
                    GarageReceived?.Invoke(m);
                    break;
                case ServerMessageKind.ChallengeCompleted:
                    ChallengeCompleted?.Invoke(m);
                    break;
                case ServerMessageKind.LiveEvent:
                    LiveEvent?.Invoke(m.PlayerId, m.Text);
                    break;
                case ServerMessageKind.Pong:
                    LastRttMs = NowMs() - m.ClientTimeMs;
                    break;
            }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void OnDestroy() => Teardown();
    }
}
