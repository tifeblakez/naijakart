using System;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Net;
using NaijaKart.Core.Progression;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Util;

namespace NaijaKart.Server.Hosting
{
    /// <summary>
    /// Top-level authoritative server. Single-threaded game loop: PumpIncoming → handle messages →
    /// tick rooms. Owns sessions (connection ↔ player), rooms, the quick-race queue, ledger, profiles
    /// and settlement. Persistence stores are interfaces so a database can replace memory later.
    /// </summary>
    public sealed class GameServer
    {
        private sealed class Session
        {
            public string ConnectionId;
            public string PlayerId;
            public string DisplayName;
            public RaceRoom Room;
            public float QueuedAt = -1f;
            public RaceMode QueuedMode;
        }

        private readonly IServerTransport _transport;
        private readonly Dictionary<string, Session> _byConnection = new Dictionary<string, Session>();
        private readonly Dictionary<string, Session> _byPlayer = new Dictionary<string, Session>();
        private readonly Dictionary<string, RaceRoom> _rooms = new Dictionary<string, RaceRoom>();
        private readonly List<Session> _queue = new List<Session>();
        private readonly DeterministicRandom _codeRng;
        private readonly float _dt;
        private float _now;
        private int _roomCounter;

        public IConfigSource Content { get; }
        public ILogger Log { get; }
        public CoinLedger Ledger { get; }
        public IWallet Wallet { get; }
        public IProfileStore Profiles { get; }
        public IRivalryStore Rivalries { get; }
        public RaceSettlementService Settlement { get; }
        public RankLadder RankLadder { get; }
        public int RoomCount => _rooms.Count;
        public int SessionCount => _byPlayer.Count;
        public float Now => _now;
        public event Action<RaceRoom, RaceResults> RaceSettled;

        public GameServer(IConfigSource content, IServerTransport transport, ILogger log = null,
            ICoinStore coinStore = null, IProfileStore profiles = null, IRivalryStore rivalries = null, ulong seed = 12345)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Log = log ?? NullLogger.Instance;
            var errors = ConfigValidator.Validate(content);
            if (errors.Count > 0) throw new InvalidOperationException("Invalid configuration:\n  " + string.Join("\n  ", errors));
            Ledger = new CoinLedger(coinStore ?? new InMemoryCoinStore(), content.Game.economy);
            Wallet = new LedgerWallet(Ledger);
            Profiles = profiles ?? new InMemoryProfileStore(content.Game.progression);
            Rivalries = rivalries ?? new InMemoryRivalryStore();
            Settlement = new RaceSettlementService(content.Game, Ledger, Profiles, Rivalries);
            RankLadder = new RankLadder(content.Game.progression);
            _codeRng = new DeterministicRandom(seed);
            _dt = 1f / content.Game.simulation.tickRate;

            _transport.ClientConnected += OnConnected;
            _transport.ClientDisconnected += OnDisconnected;
            _transport.MessageReceived += OnMessage;
        }

        public float FixedDeltaTime => _dt;

        /// <summary>One server tick. Call at Content.Game.simulation.tickRate.</summary>
        public void Tick()
        {
            _now += _dt;
            _transport.PumpIncoming();
            TickMatchmaking();
            var finished = new List<string>();
            foreach (var kv in _rooms)
            {
                kv.Value.Tick(_dt);
                if (kv.Value.IsEmpty) finished.Add(kv.Key);
            }
            foreach (var code in finished)
            {
                Log.Info("room", "Closing empty room " + code);
                _rooms.Remove(code);
            }
        }

        // ---- transport callbacks ----
        private void OnConnected(string connectionId)
        {
            _byConnection[connectionId] = new Session { ConnectionId = connectionId };
        }

        private void OnDisconnected(string connectionId)
        {
            if (!_byConnection.TryGetValue(connectionId, out var s)) return;
            _byConnection.Remove(connectionId);
            if (s.PlayerId == null) return;
            _queue.Remove(s);
            s.Room?.OnPlayerDisconnected(s.PlayerId);
            // Keep the player session for reconnect while the room keeps them; drop it otherwise.
            if (s.Room == null || !s.Room.HasMember(s.PlayerId)) _byPlayer.Remove(s.PlayerId);
            else s.ConnectionId = null;
        }

        private void OnMessage(string connectionId, ClientEnvelope msg)
        {
            if (!_byConnection.TryGetValue(connectionId, out var s)) return;
            if (msg.Kind == ClientMessageKind.Hello)
            {
                HandleHello(s, msg);
                return;
            }
            if (s.PlayerId == null)
            {
                _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Error, Error = "Send Hello first" });
                return;
            }
            switch (msg.Kind)
            {
                case ClientMessageKind.Ping:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Pong, ClientTimeMs = msg.ClientTimeMs, ServerTimeMs = NowMs() });
                    break;
                case ClientMessageKind.JoinQueue:
                    if (s.Room != null) { SendError(s.PlayerId, "Leave your room first"); break; }
                    if (!_queue.Contains(s)) { s.QueuedAt = _now; s.QueuedMode = msg.Mode == RaceMode.Ranked ? RaceMode.Ranked : RaceMode.QuickRace; _queue.Add(s); }
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.QueueStatus, Text = "searching", Tick = _queue.Count });
                    break;
                case ClientMessageKind.LeaveQueue:
                    _queue.Remove(s);
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.QueueStatus, Text = "left" });
                    break;
                case ClientMessageKind.CreateRoom:
                {
                    if (s.Room != null) { SendError(s.PlayerId, "Already in a room"); break; }
                    string trackId = Content.GetTrack(msg.TrackId) != null ? msg.TrackId : Content.TrackIds[0];
                    var room = CreateRoom(msg.Mode == RaceMode.Practice ? RaceMode.Practice : RaceMode.PrivateRoom, trackId, msg.Laps, s.PlayerId);
                    room.Configure(trackId, msg.Laps, msg.ItemsEnabled, msg.LastmaEnabled);
                    JoinRoom(s, room, msg.VehicleId, msg.CharacterId);
                    break;
                }
                case ClientMessageKind.JoinRoom:
                {
                    if (s.Room != null) { SendError(s.PlayerId, "Already in a room"); break; }
                    if (msg.RoomCode == null || !_rooms.TryGetValue(msg.RoomCode.ToUpperInvariant(), out var room)) { SendError(s.PlayerId, "Room not found"); break; }
                    if (!JoinRoom(s, room, msg.VehicleId, msg.CharacterId)) SendError(s.PlayerId, "Room is full or already racing");
                    break;
                }
                case ClientMessageKind.LeaveRoom:
                    LeaveRoom(s);
                    break;
                default:
                    if (s.Room == null) { SendError(s.PlayerId, "Not in a room"); break; }
                    s.Room.HandleIntent(s.PlayerId, msg);
                    break;
            }
        }

        private void HandleHello(Session s, ClientEnvelope msg)
        {
            // Authentication is a backend concern (PRD §67); for now identity is the declared player id
            // and the token is accepted by IAuthenticator. See ADR-0004.
            if (string.IsNullOrWhiteSpace(msg.PlayerId)) { _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.Error, Error = "PlayerId required" }); return; }
            string playerId = msg.PlayerId.Trim();
            if (_byPlayer.TryGetValue(playerId, out var existing) && existing != s)
            {
                // Reconnect: take over the old session.
                if (existing.ConnectionId != null) _transport.Disconnect(existing.ConnectionId, "replaced");
                _byConnection.Remove(existing.ConnectionId ?? "");
                existing.ConnectionId = s.ConnectionId;
                _byConnection[s.ConnectionId] = existing;
                s = existing;
                s.Room?.OnPlayerReconnected(playerId);
            }
            s.PlayerId = playerId;
            s.DisplayName = string.IsNullOrWhiteSpace(msg.DisplayName) ? playerId : msg.DisplayName.Trim();
            _byPlayer[playerId] = s;
            Ledger.EnsureAccount(playerId);
            var profile = Profiles.Get(playerId);
            profile.DisplayName = s.DisplayName;
            Profiles.Save(profile);
            _transport.Send(s.ConnectionId, new ServerEnvelope
            {
                Kind = ServerMessageKind.Welcome,
                PlayerId = playerId,
                Amount = Ledger.GetBalance(playerId),
                Text = RankLadder.TierFor(profile.Rating).id,
                ServerTimeMs = NowMs()
            });
            if (s.Room != null) _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.RoomState, Room = s.Room.ToDto() });
        }

        // ---- rooms & matchmaking ----
        public RaceRoom CreateRoom(RaceMode mode, string trackId, int laps, string hostPlayerId)
        {
            string code;
            do { code = NewRoomCode(); } while (_rooms.ContainsKey(code));
            var room = new RaceRoom(this, code, mode, trackId, laps, hostPlayerId, (ulong)(++_roomCounter) * 104729UL + _codeRng.NextULong());
            _rooms[code] = room;
            Log.Info("room", $"Created room {code} mode={mode} track={trackId}");
            return room;
        }

        public RaceRoom GetRoom(string code) => _rooms.TryGetValue(code, out var r) ? r : null;

        private bool JoinRoom(Session s, RaceRoom room, string vehicleId, string characterId)
        {
            if (!room.Join(s.PlayerId, vehicleId, characterId)) return false;
            s.Room = room;
            _queue.Remove(s);
            return true;
        }

        private void LeaveRoom(Session s)
        {
            if (s.Room == null) return;
            var room = s.Room;
            s.Room = null;
            room.Leave(s.PlayerId);
        }

        internal void NotifyLeftRoom(string playerId)
        {
            if (_byPlayer.TryGetValue(playerId, out var s)) s.Room = null;
        }

        private void TickMatchmaking()
        {
            if (_queue.Count == 0) return;
            var rules = Content.Game.raceRules;
            int max = Content.Game.simulation.maxPlayersPerRace;
            foreach (RaceMode mode in new[] { RaceMode.QuickRace, RaceMode.Ranked })
            {
                var group = new List<Session>();
                float oldest = float.MaxValue;
                foreach (var s in _queue) if (s.QueuedMode == mode) { group.Add(s); if (s.QueuedAt < oldest) oldest = s.QueuedAt; }
                if (group.Count == 0) continue;
                bool full = group.Count >= max;
                bool waitedEnough = group.Count >= rules.minPlayersToStart && _now - oldest >= rules.matchmakingWaitSeconds;
                if (!full && !waitedEnough) continue;

                // Ranked: sort by rating so the grid is close in skill (simple first pass; see ADR-0005).
                if (mode == RaceMode.Ranked) group.Sort((a, b) => Profiles.Get(a.PlayerId).Rating.CompareTo(Profiles.Get(b.PlayerId).Rating));
                int take = Math.Min(max, group.Count);
                string trackId = Content.TrackIds[_codeRng.Range(0, Content.TrackIds.Length)];
                var room = CreateRoom(mode, trackId, rules.defaultLaps, null);
                // Seat everyone first; readying triggers auto-start, which must not lock out the rest.
                for (int i = 0; i < take; i++) JoinRoom(group[i], room, null, null);
                for (int i = 0; i < take; i++) room.SetReady(group[i].PlayerId, true);
            }
        }

        private string NewRoomCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[_codeRng.Range(0, alphabet.Length)];
            return new string(chars);
        }

        // ---- helpers used by rooms ----
        public string DisplayNameOf(string playerId) => _byPlayer.TryGetValue(playerId, out var s) ? s.DisplayName : playerId;

        public void SendTo(string playerId, ServerEnvelope env)
        {
            if (_byPlayer.TryGetValue(playerId, out var s) && s.ConnectionId != null) _transport.Send(s.ConnectionId, env);
        }

        public void SendError(string playerId, string error) =>
            SendTo(playerId, new ServerEnvelope { Kind = ServerMessageKind.Error, Error = error });

        /// <summary>Bail is allowed from racers in the same room and from friends anywhere on the server.</summary>
        public bool CanBail(string payerId, string targetId, RaceRoom room)
        {
            if (payerId == targetId || targetId == null) return false;
            if (room.HasMember(payerId)) return true;
            var profile = Profiles.Get(targetId);
            return profile.FriendIds.Contains(payerId);
        }

        public void BroadcastBailRequest(RaceRoom room, string playerId, long amount)
        {
            var env = new ServerEnvelope { Kind = ServerMessageKind.BailRequest, PlayerId = playerId, Amount = amount, Text = DisplayNameOf(playerId) + " needs bail!" };
            foreach (var m in room.Members) if (m != playerId) SendTo(m, env);
            foreach (var friend in Profiles.Get(playerId).FriendIds) if (!room.HasMember(friend)) SendTo(friend, env);
        }

        internal void OnRaceSettled(RaceRoom room, RaceResults results) => RaceSettled?.Invoke(room, results);

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
