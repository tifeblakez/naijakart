using System;
using System.Collections.Generic;
using NaijaKart.Core.Accounts;
using NaijaKart.Core.Challenges;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Net;
using NaijaKart.Core.Progression;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Tournaments;
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
        public ChallengeEvaluator Challenges { get; }
        public IChallengeProgressStore ChallengeProgress { get; }
        public AccountService Accounts { get; }
        public IOtpSender OtpSender { get; }
        /// <summary>Overrides the UTC clock (tests, Wahala Calendar).</summary>
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
        public int RoomCount => _rooms.Count;
        public int SessionCount => _byPlayer.Count;
        public float Now => _now;
        public event Action<RaceRoom, RaceResults> RaceSettled;

        public GameServer(IConfigSource content, IServerTransport transport, ILogger log = null,
            ICoinStore coinStore = null, IProfileStore profiles = null, IRivalryStore rivalries = null, ulong seed = 12345,
            IChallengeProgressStore challengeProgress = null, IAccountStore accounts = null, IOtpSender otpSender = null)
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
            Settlement = new RaceSettlementService(content.Game, Ledger, Profiles, Rivalries, () => UtcNow());
            RankLadder = new RankLadder(content.Game.progression, content.Game.ranked);
            ChallengeProgress = challengeProgress ?? new InMemoryChallengeProgressStore();
            Challenges = new ChallengeEvaluator(content.Challenges, ChallengeProgress, Ledger, () => UtcNow());
            OtpSender = otpSender ?? new RecordingOtpSender();
            Accounts = new AccountService(accounts ?? new InMemoryAccountStore(), OtpSender, content.Game.accounts, Ledger, () => new DateTimeOffset(UtcNow()).ToUnixTimeMilliseconds(), seed ^ 0x5EEDUL);
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
            TickTournaments();
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
            var seen = Profiles.Get(s.PlayerId); seen.LastSeenUnixMs = new DateTimeOffset(UtcNow()).ToUnixTimeMilliseconds(); Profiles.Save(seen);
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
                    if (msg.Mode == RaceMode.Ranked && !Accounts.RankedAllowed(s.PlayerId)) { SendError(s.PlayerId, "Save your progress to play Ranked"); break; }
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
                    room.Configure(trackId, msg.Laps, msg.ItemsMode ?? (msg.ItemsEnabled ? "On" : "Off"), msg.LastmaMode ?? (msg.LastmaEnabled ? "On" : "Off"), msg.FillWithAi);
                    JoinRoom(s, room, msg.VehicleId, msg.CharacterId);
                    break;
                }
                case ClientMessageKind.JoinRoom:
                {
                    if (s.Room != null) { SendError(s.PlayerId, "Already in a room"); break; }
                    if (msg.RoomCode == null || !_rooms.TryGetValue(NormalizeRoomCode(msg.RoomCode), out var room)) { SendError(s.PlayerId, "Room not found"); break; }
                    if (!JoinRoom(s, room, msg.VehicleId, msg.CharacterId)) SendError(s.PlayerId, "Room is full or already racing");
                    break;
                }
                case ClientMessageKind.LeaveRoom:
                    LeaveRoom(s);
                    break;
                case ClientMessageKind.InviteFriend:
                {
                    if (s.Room == null) { SendError(s.PlayerId, "Not in a room"); break; }
                    if (string.IsNullOrWhiteSpace(msg.TargetPlayerId) || msg.TargetPlayerId == s.PlayerId) { SendError(s.PlayerId, "Invalid friend id"); break; }
                    s.Room.Invite(msg.TargetPlayerId);
                    SendTo(msg.TargetPlayerId, new ServerEnvelope { Kind = ServerMessageKind.RoomInvite, Room = s.Room.ToDto(), PlayerId = s.PlayerId, Text = s.DisplayName });
                    break;
                }
                case ClientMessageKind.AddFriend:
                    HandleAddFriend(s, msg.TargetPlayerId);
                    break;
                case ClientMessageKind.RemoveFriend:
                    HandleRemoveFriend(s, msg.TargetPlayerId);
                    break;
                case ClientMessageKind.AcceptFriend:
                    HandleAcceptFriend(s, msg.TargetPlayerId);
                    break;
                case ClientMessageKind.DeclineFriend:
                {
                    var me = Profiles.Get(s.PlayerId); me.FriendRequestsIn.Remove(msg.TargetPlayerId ?? ""); Profiles.Save(me);
                    if (msg.TargetPlayerId != null) { var them = Profiles.Get(msg.TargetPlayerId); them.FriendRequestsOut.Remove(s.PlayerId); Profiles.Save(them); }
                    SendFriends(s);
                    break;
                }
                case ClientMessageKind.GetFriends:
                    SendFriends(s);
                    break;
                case ClientMessageKind.GetShop:
                    SendShop(s);
                    break;
                case ClientMessageKind.PurchaseCosmetic:
                    HandlePurchaseCosmetic(s, msg.Text);
                    break;
                case ClientMessageKind.EquipCosmetic:
                    HandleEquipCosmetic(s, msg.Text);
                    break;
                case ClientMessageKind.GetSeasonPass:
                    SendSeasonPass(s);
                    break;
                case ClientMessageKind.ClaimPassTier:
                    HandleClaimPassTier(s, msg.Laps, msg.Flag);
                    break;
                case ClientMessageKind.BuyPremiumPass:
                    HandleBuyPremiumPass(s);
                    break;
                case ClientMessageKind.GetTournament:
                {
                    var t = Tournament(msg.Text);
                    if (t == null) SendError(s.PlayerId, "No tournament right now"); else SendTournament(s, t);
                    break;
                }
                case ClientMessageKind.EnterTournament:
                {
                    var t = Tournament(msg.Text);
                    if (t == null) { SendError(s.PlayerId, "No tournament right now"); break; }
                    if (!t.Enter(s.PlayerId)) { SendError(s.PlayerId, t.State.Started ? "The tournament has started" : "The tournament is full"); SendTournament(s, t); break; }
                    BroadcastTournament(t);   // everyone in the lobby sees the entrant count move
                    break;
                }
                case ClientMessageKind.LeaveTournament:
                {
                    var t = Tournament(msg.Text);
                    if (t != null) { t.Leave(s.PlayerId); SendTournament(s, t); BroadcastTournament(t); }
                    break;
                }
                case ClientMessageKind.GetChallenges:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Challenges, Challenges = BuildChallenges(s.PlayerId) });
                    break;
                case ClientMessageKind.GetLeaderboard:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Leaderboard, Text = msg.Text ?? "rp", Leaderboard = BuildLeaderboard(msg.Text, msg.Flag ? "friends" : msg.Scope, s.PlayerId, msg.TrackId) });
                    break;
                case ClientMessageKind.GetRivalries:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Rivalries, Rivalries = BuildRivalries(s.PlayerId) });
                    break;
                case ClientMessageKind.GetProfile:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Profile, Profile = BuildProfile(msg.TargetPlayerId ?? s.PlayerId) });
                    break;
                case ClientMessageKind.GetGarage:
                    SendGarage(s);
                    break;
                case ClientMessageKind.ClaimStart:
                {
                    var r = Accounts.StartClaim(s.PlayerId, msg.Text, msg.Flag ? OtpChannel.WhatsApp : OtpChannel.Sms);
                    SendAccount(s, r.ToString());
                    break;
                }
                case ClientMessageKind.ClaimVerify:
                {
                    var r = Accounts.VerifyClaim(s.PlayerId, msg.Text, out string signIn);
                    SendAccount(s, r.ToString(), signIn);
                    break;
                }
                case ClientMessageKind.ClaimWithProvider:
                {
                    var r = Accounts.ClaimWithProvider(s.PlayerId, msg.Text, msg.AuthToken, out string signIn);
                    SendAccount(s, r.ToString(), signIn);
                    break;
                }
                case ClientMessageKind.SetRacer:
                {
                    if (!Accounts.SetRacer(s.PlayerId, msg.DisplayName, msg.CharacterId, msg.Text, out string err)) { SendError(s.PlayerId, err); SendAccount(s, "Invalid"); break; }
                    var acc = Accounts.Get(s.PlayerId);
                    if (acc.RacerName != null)
                    {
                        s.DisplayName = acc.RacerName;
                        var profile = Profiles.Get(s.PlayerId); profile.DisplayName = acc.RacerName; Profiles.Save(profile);
                    }
                    if (acc.LookId != null) { string v = null, c = acc.LookId; ResolveLoadout(s.PlayerId, ref v, ref c); }
                    SendAccount(s, "Ok");
                    break;
                }
                case ClientMessageKind.CheckName:
                    SendAccount(s, "Ok", null, Accounts.CheckName(msg.Text, s.PlayerId).ToString());
                    break;
                case ClientMessageKind.ApplyReferral:
                {
                    if (!Accounts.ApplyReferral(s.PlayerId, msg.Text, out string err)) { SendError(s.PlayerId, err); SendAccount(s, "Invalid"); break; }
                    SendAccount(s, "Ok");
                    break;
                }
                case ClientMessageKind.GetAccount:
                    SendAccount(s, "Ok");
                    break;
                case ClientMessageKind.GetSeason:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Season, Season = BuildSeason() });
                    break;
                case ClientMessageKind.PurchaseVehicle:
                    HandlePurchase(s, msg.VehicleId, isVehicle: true);
                    break;
                case ClientMessageKind.PurchaseCharacter:
                    HandlePurchase(s, msg.CharacterId, isVehicle: false);
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
            ApplySeasonReset(profile);
            Profiles.Save(profile);
            _transport.Send(s.ConnectionId, new ServerEnvelope
            {
                Kind = ServerMessageKind.Welcome,
                PlayerId = playerId,
                Amount = Ledger.GetBalance(playerId),
                PremiumBalance = Ledger.GetBalance(playerId, Core.Economy.Currency.Premium),
                Text = RankLadder.TierFor(profile.RankedPoints).id,
                ServerTimeMs = NowMs()
            });
            var live = TodayRule();
            if (live != null) _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.LiveEvent, Text = live.displayName, PlayerId = live.id });
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

        private float _queueStatusTimer;
        private float _tournamentTimer;
        private readonly Dictionary<string, TournamentEngine> _tournaments = new Dictionary<string, TournamentEngine>();
        private int _tournamentRaceCounter;

        private void TickMatchmaking()
        {
            if (_queue.Count == 0) return;
            var rules = Content.Game.raceRules;
            int max = Content.Game.simulation.maxPlayersPerRace;
            // "Finding racers... 5 / 8 · AI racers join in 0:10" (design 04): one status a second to everyone queued.
            _queueStatusTimer -= _dt;
            if (_queueStatusTimer <= 0f)
            {
                _queueStatusTimer = 1f;
                foreach (RaceMode mode in new[] { RaceMode.QuickRace, RaceMode.Ranked })
                {
                    int found = 0; float oldestAt = float.MaxValue;
                    foreach (var s in _queue) if (s.QueuedMode == mode) { found++; if (s.QueuedAt < oldestAt) oldestAt = s.QueuedAt; }
                    if (found == 0) continue;
                    int toAi = (int)Math.Ceiling(Math.Max(0f, rules.matchmakingWaitSeconds - (_now - oldestAt)));
                    foreach (var s in _queue)
                        if (s.QueuedMode == mode && s.ConnectionId != null)
                            _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.QueueStatus, Text = "searching", Tick = found, QueueFound = found, QueueMax = max, QueueSecondsToAi = toAi });
                }
            }
            foreach (RaceMode mode in new[] { RaceMode.QuickRace, RaceMode.Ranked })
            {
                var group = new List<Session>();
                float oldest = float.MaxValue;
                foreach (var s in _queue) if (s.QueuedMode == mode) { group.Add(s); if (s.QueuedAt < oldest) oldest = s.QueuedAt; }
                if (group.Count == 0) continue;
                bool full = group.Count >= max;
                int minToStart = mode == RaceMode.Ranked ? Math.Max(rules.minPlayersToStart, Content.Game.ranked.minRealPlayers) : rules.minPlayersToStart;
                bool waitedEnough = group.Count >= minToStart && _now - oldest >= rules.matchmakingWaitSeconds;
                if (!full && !waitedEnough) continue;

                // Ranked: sort by rating so the grid is close in skill (simple first pass; see ADR-0005).
                if (mode == RaceMode.Ranked) group.Sort((a, b) => Profiles.Get(a.PlayerId).Rating.CompareTo(Profiles.Get(b.PlayerId).Rating));
                int take = Math.Min(max, group.Count);
                var live = TodayRule();
                string trackId = live != null && live.preferredTrackId != null && Content.GetTrack(live.preferredTrackId) != null
                    ? live.preferredTrackId
                    : Content.TrackIds[_codeRng.Range(0, Content.TrackIds.Length)];
                var room = CreateRoom(mode, trackId, rules.defaultLaps, null);
                if (live != null) room.ApplyLiveEvent(live);
                // Seat everyone first; readying triggers auto-start, which must not lock out the rest.
                for (int i = 0; i < take; i++) JoinRoom(group[i], room, null, null);
                for (int i = 0; i < take; i++) room.SetReady(group[i].PlayerId, true);
            }
        }

        /// <summary>Room codes read like "EKO-427" (design 03.2): letters, dash, digits; easy to say over WhatsApp.</summary>
        private string NewRoomCode()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            var cfg = Content.Game.social;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Math.Max(1, cfg.roomCodeLetters); i++) sb.Append(letters[_codeRng.Range(0, letters.Length)]);
            sb.Append('-');
            for (int i = 0; i < Math.Max(1, cfg.roomCodeDigits); i++) sb.Append((char)('0' + _codeRng.Range(0, 10)));
            return sb.ToString();
        }

        /// <summary>Accepts "eko427", "EKO 427", "eko-427" and the share link.</summary>
        public string NormalizeRoomCode(string raw)
        {
            if (raw == null) return "";
            int slash = raw.LastIndexOf('/');
            if (slash >= 0) raw = raw.Substring(slash + 1);
            var letters = new System.Text.StringBuilder(); var digits = new System.Text.StringBuilder();
            foreach (char ch in raw.ToUpperInvariant()) { if (char.IsLetter(ch)) letters.Append(ch); else if (char.IsDigit(ch)) digits.Append(ch); }
            return letters.Length > 0 && digits.Length > 0 ? letters + "-" + digits : raw.ToUpperInvariant().Trim();
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

        internal void OnRaceSettled(RaceRoom room, RaceResults results)
        {
            // Challenges and achievements (PRD §41–§43) are evaluated once per race, after settlement.
            var completions = Challenges.Apply(results,
                friendsOf: id => Profiles.Get(id).FriendIds,
                tracksWonBefore: id => new HashSet<string>(Profiles.Get(id).TracksWon));
            foreach (var c in completions)
            {
                var profile = Profiles.Get(c.PlayerId);
                if (c.Xp > 0) profile.TotalXp += c.Xp;
                if (!string.IsNullOrEmpty(c.Title) && !profile.Achievements.Contains(c.Challenge.id))
                {
                    profile.Achievements.Add(c.Challenge.id);
                    if (string.IsNullOrEmpty(profile.Title)) profile.Title = c.Title;
                }
                Profiles.Save(profile);
                SendTo(c.PlayerId, new ServerEnvelope
                {
                    Kind = ServerMessageKind.ChallengeCompleted,
                    PlayerId = c.PlayerId,
                    Text = c.Challenge.displayName,
                    Amount = c.Coins,
                    Challenges = new[] { ToDto(c.Challenge, null) }
                });
            }
            foreach (var e in results.Entries)
            {
                if (e.IsBot) continue;
                var profile = Profiles.Get(e.PlayerId);
                if (e.Finished && e.FinishPosition == 1 && !profile.TracksWon.Contains(results.TrackId)) { profile.TracksWon.Add(results.TrackId); Profiles.Save(profile); }
                // "Bring your guys": the referral pays both sides after the invited racer's first race.
                if (profile.Races == 1 && Accounts.RewardReferralAfterFirstRace(e.PlayerId, out string referrer))
                {
                    foreach (string id in new[] { e.PlayerId, referrer })
                        if (_byPlayer.TryGetValue(id, out var sess)) SendAccount(sess, "ReferralRewarded");
                }
            }
            SettleTournamentRace(room, results);
            RaceSettled?.Invoke(room, results);
        }

        public WeekdayRule TodayRule() => Content.Game.liveEvents?.RuleFor((int)UtcNow().DayOfWeek);

        // ---- ranked seasons ----
        /// <summary>Current season id: the configured one, or configured id + "+N" once it has rolled over N times.</summary>
        public string CurrentSeasonId()
        {
            var cfg = Content.Game.ranked;
            var now = UtcNow();
            if (!SeasonClock.SeasonEnded(cfg, now)) return cfg.seasonId;
            int rolls = (int)Math.Floor((now - SeasonClock.SeasonStart(cfg)).TotalDays / Math.Max(1, cfg.seasonLengthDays));
            return cfg.seasonId + "+" + rolls;
        }

        /// <summary>Applies the season reset lazily when a profile is seen in a new season (everyone drops a tier).</summary>
        public void ApplySeasonReset(PlayerProfile profile)
        {
            string season = CurrentSeasonId();
            if (profile.SeasonId == season) return;
            if (profile.SeasonId != null) profile.RankedPoints = RankLadder.AfterSeasonReset(profile.RankedPoints);
            profile.SeasonId = season;
            profile.RecentRanked.Clear();
            profile.SeasonXp = 0; profile.PremiumPass = false; profile.ClaimedPassTiers.Clear();
        }

        private SeasonDto BuildSeason()
        {
            var cfg = Content.Game.ranked; var now = UtcNow();
            var table = cfg.rpByPosition ?? Array.Empty<int>();
            return new SeasonDto
            {
                Id = CurrentSeasonId(), Name = cfg.seasonName, DaysLeft = SeasonClock.DaysLeft(cfg, now), EndsUtc = SeasonClock.SeasonEnd(cfg).ToString("o"),
                RushHourActive = SeasonClock.IsRushHour(cfg, now), RushHourMultiplier = cfg.rushHourMultiplier, RushHourSecondsTo = SeasonClock.RushHourSecondsTo(cfg, now),
                RushHourStartHour = cfg.rushHourStartHour, RushHourEndHour = cfg.rushHourEndHour, MinRealPlayers = cfg.minRealPlayers,
                RpForWin = table.Length > 0 ? table[0] : 0, RpForLast = table.Length > 0 ? table[table.Length - 1] : 0,
                RpPerDivision = cfg.rpPerDivision, DivisionsPerTier = cfg.divisionsPerTier
            };
        }

        // ---- accounts ----
        private void SendAccount(Session s, string result, string signInPlayerId = null, string nameStatus = null)
        {
            var a = Accounts.Get(s.PlayerId);
            var cfg = Content.Game.accounts;
            _transport.Send(s.ConnectionId, new ServerEnvelope
            {
                Kind = ServerMessageKind.Account,
                Amount = Ledger.GetBalance(s.PlayerId),
                Account = new AccountDto
                {
                    PlayerId = s.PlayerId, Status = a.Status.ToString(), Provider = a.Provider,
                    PhoneMasked = a.Provider == "phone" ? AccountService.MaskPhone(a.ProviderSubject) : (a.PendingPhone != null ? AccountService.MaskPhone(a.PendingPhone) : null),
                    RacerName = a.RacerName, HomeCity = a.HomeCity, LookId = a.LookId, ReferralCode = a.ReferralCode, ReferredBy = a.ReferredBy,
                    Result = result, NameStatus = nameStatus, SignInPlayerId = signInPlayerId, ResendInSeconds = Accounts.ResendInSeconds(s.PlayerId),
                    RankedUnlocked = Accounts.RankedAllowed(s.PlayerId), AccountBonusCoins = cfg.accountBonusCoins, ReferralBonusCoins = cfg.referralBonusCoins, Cities = cfg.cities ?? Array.Empty<string>()
                }
            });
        }

        // ---- tournaments (PRD §8, design 17) ----
        /// <summary>Engines are created from content on first use, so tests and live ops can swap definitions.</summary>
        public TournamentEngine Tournament(string id = null)
        {
            foreach (var def in Content.Tournaments?.tournaments ?? Array.Empty<TournamentDefinition>())
                if (!_tournaments.ContainsKey(def.id)) _tournaments[def.id] = new TournamentEngine(def);
            if (id != null) return _tournaments.TryGetValue(id, out var e) ? e : null;
            TournamentEngine first = null;
            foreach (var e in _tournaments.Values) { if (!e.State.Finished) return e; first ??= e; }
            return first;
        }

        private long NowUnixMs() => new DateTimeOffset(UtcNow()).ToUnixTimeMilliseconds();

        private int OnlineEntrants(TournamentEngine t)
        {
            int n = 0;
            foreach (var e in t.State.Entrants) if (e.Alive && _byPlayer.TryGetValue(e.PlayerId, out var s) && s.ConnectionId != null) n++;
            return n;
        }

        private void TickTournaments()
        {
            _tournamentTimer -= _dt;
            if (_tournamentTimer > 0f) return;
            _tournamentTimer = 1f;
            Tournament();   // materialise engines
            long now = NowUnixMs();
            foreach (var t in _tournaments.Values)
            {
                if (!t.CanStartRace(now, OnlineEntrants(t))) continue;
                var def = t.Definition;
                int grid = Content.Game.simulation.maxPlayersPerRace;
                string trackId = def.trackIds != null && def.trackIds.Length > 0 ? def.trackIds[_tournamentRaceCounter % def.trackIds.Length] : Content.TrackIds[0];
                if (Content.GetTrack(trackId) == null) trackId = Content.TrackIds[0];
                _tournamentRaceCounter++;
                foreach (var ids in t.Grids(grid, id => _byPlayer.TryGetValue(id, out var ss) && ss.ConnectionId != null && (ss.Room == null || ss.Room.Mode != RaceMode.Tournament)))
                {
                    if (ids.Count == 0) continue;
                    var room = CreateRoom(RaceMode.Tournament, trackId, def.laps, null);
                    room.TournamentId = def.id;
                    foreach (var id in ids)
                    {
                        var sess = _byPlayer[id];
                        if (sess.Room != null) LeaveRoom(sess);
                        _queue.Remove(sess);
                        JoinRoom(sess, room, null, null);
                    }
                    if (def.fillWithAi) { int i = 0; while (room.Members.Count < grid) room.AddBot("bot_" + room.Code.ToLowerInvariant().Replace("-", "") + "_" + (++i), 0.45f + 0.07f * i); }
                    foreach (var id in ids) room.SetReady(id, true);
                    t.RaceStarted(room.Race.Setup.RaceId);
                    Log.Info("tournament", $"{def.name}: {t.CurrentRound?.name} race {t.State.RaceIndex + 1} in room {room.Code} ({ids.Count} racers)");
                    foreach (var id in ids) SendTournament(_byPlayer[id], t);
                }
            }
        }

        private void SettleTournamentRace(RaceRoom room, RaceResults results)
        {
            if (room.TournamentId == null || !_tournaments.TryGetValue(room.TournamentId, out var t)) return;
            var def = t.Definition;
            foreach (var e in results.Entries)
                if (!e.IsBot && t.State.Find(e.PlayerId) != null && def.coinsPerRace > 0)
                    Ledger.Credit(e.PlayerId, def.coinsPerRace, "tournament_race", results.RaceId + ":tournament:" + e.PlayerId, out _);
            bool slotDone = t.ApplyRaceResult(results, NowUnixMs(), out _, out bool finished);
            if (finished)
            {
                var standings = t.State.Standings();
                for (int i = 0; i < standings.Count; i++)
                {
                    var p = Profiles.Get(standings[i].PlayerId);
                    bool champion = standings[i].PlayerId == t.State.ChampionId;
                    if (champion) p.TournamentWins++;
                    string cosmetic = champion ? def.championCosmeticId : i < def.finalistsCount ? def.finalistCosmeticId : null;
                    if (cosmetic != null && FindCosmetic(cosmetic) != null && !p.OwnedCosmeticIds.Contains(cosmetic)) p.OwnedCosmeticIds.Add(cosmetic);
                    Profiles.Save(p);
                }
                Log.Info("tournament", $"{def.name}: champion {t.State.ChampionId}");
            }
            if (slotDone)
                foreach (var e in t.State.Entrants) if (_byPlayer.TryGetValue(e.PlayerId, out var s) && s.ConnectionId != null) SendTournament(s, t);
        }

        private TournamentDto BuildTournament(TournamentEngine t, string viewerId)
        {
            var def = t.Definition; var st = t.State;
            var me = st.Find(viewerId);
            var rounds = new List<TournamentRoundDto>();
            for (int i = 0; i < def.rounds.Length; i++)
            {
                var r = def.rounds[i];
                string state = st.Finished || i < st.RoundIndex ? (me != null && !me.Alive && i >= RoundOut(st, me) ? "Out" : "Advanced") : i == st.RoundIndex ? (st.Started ? "Now" : "Upcoming") : "Upcoming";
                if (st.Finished && i == def.rounds.Length - 1) state = "Done";
                rounds.Add(new TournamentRoundDto { Name = r.name, State = state, StartsUtc = r.startsUtc, Races = r.races, RaceIndex = i == st.RoundIndex ? st.RaceIndex : 0, Advance = r.advance });
            }
            var standings = new List<TournamentStandingDto>();
            int pos = 0;
            foreach (var e in st.Standings()) standings.Add(new TournamentStandingDto { Position = ++pos, PlayerId = e.PlayerId, DisplayName = Profiles.Get(e.PlayerId).DisplayName, Points = e.Points, Note = t.NoteFor(e), Alive = e.Alive });
            long next = t.NextRaceAtUnixMs();
            string roomCode = null;
            if (_byPlayer.TryGetValue(viewerId, out var vs) && vs.Room != null && vs.Room.TournamentId == def.id) roomCode = vs.Room.Code;
            return new TournamentDto
            {
                Id = def.id, Name = def.name, Entrants = st.Entrants.Count, MaxEntrants = def.maxEntrants, Entered = me != null, Started = st.Started, Finished = st.Finished, ChampionId = st.ChampionId,
                CurrentRound = st.RoundIndex, Rounds = rounds.ToArray(), Standings = standings.ToArray(), MyPoints = me?.Points ?? 0, MyAlive = me?.Alive ?? false,
                NextRaceInSeconds = next < 0 ? -1 : (int)Math.Max(0, (next - NowUnixMs()) / 1000), NextTrackId = def.trackIds != null && def.trackIds.Length > 0 ? def.trackIds[_tournamentRaceCounter % def.trackIds.Length] : Content.TrackIds[0],
                Laps = def.laps, CoinsPerRace = def.coinsPerRace, ChampionRewardName = FindCosmetic(def.championCosmeticId ?? "")?.displayName, FinalistRewardName = FindCosmetic(def.finalistCosmeticId ?? "")?.displayName,
                FinalistsCount = def.finalistsCount, RoomCode = roomCode
            };
            static int RoundOut(TournamentState s, TournamentEntrant e) => s.RoundIndex;   // eliminated entrants went out at the last completed round
        }

        private void BroadcastTournament(TournamentEngine t)
        {
            foreach (var e in t.State.Entrants) if (_byPlayer.TryGetValue(e.PlayerId, out var s) && s.ConnectionId != null) SendTournament(s, t);
        }

        private void SendTournament(Session s, TournamentEngine t) =>
            _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.Tournament, Tournament = BuildTournament(t, s.PlayerId) });

        // ---- cosmetics & shop (looks only, PRD §37) ----
        private CosmeticDefinition FindCosmetic(string id) { foreach (var c in Content.Cosmetics.cosmetics) if (c.id == id) return c; return null; }

        public bool OwnsCosmetic(PlayerProfile p, CosmeticDefinition c) =>
            c != null && (p.OwnedCosmeticIds.Contains(c.id) || (c.priceCoins <= 0 && c.pricePremium <= 0 && c.unlockLevel <= 0 && c.passTier <= 0));

        private CosmeticDto CosmeticDtoFor(PlayerProfile p, CosmeticDefinition c, int level)
        {
            bool owned = OwnsCosmetic(p, c);
            bool equipped = p.EquippedCosmetics.TryGetValue(c.kind, out var eq) ? eq == c.id : owned && IsDefaultFor(p, c);
            int daysLeft = 0;
            if (c.featured && DateTime.TryParse(c.featuredUntilUtc ?? "", null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var until))
                daysLeft = Math.Max(0, (int)Math.Ceiling((until - UtcNow()).TotalDays));
            string state = equipped ? "Equipped" : owned ? "Owned" : c.passTier > 0 ? "Pass" : c.unlockLevel > level ? "Level" : c.pricePremium > 0 ? "Premium" : "Coins";
            return new CosmeticDto
            {
                Id = c.id, Kind = c.kind, DisplayName = c.displayName, Description = c.description, AppliesTo = c.appliesTo, PriceCoins = c.priceCoins, PricePremium = c.pricePremium,
                UnlockLevel = c.unlockLevel, PassTier = c.passTier, PassPremium = c.passPremium, ColorHex = c.colorHex, Owned = owned, Equipped = equipped, LevelReached = level >= c.unlockLevel,
                IsNew = c.isNew, Featured = c.featured && (daysLeft > 0 || c.featuredUntilUtc == null), FeaturedDaysLeft = daysLeft, NairaPrice = c.nairaPrice, State = state
            };
        }

        /// <summary>The first free cosmetic of a kind is the default look until something else is equipped.</summary>
        private bool IsDefaultFor(PlayerProfile p, CosmeticDefinition c)
        {
            foreach (var other in Content.Cosmetics.cosmetics)
                if (other.kind == c.kind && other.appliesTo == c.appliesTo && OwnsCosmetic(p, other)) return other.id == c.id;
            return false;
        }

        private void SendShop(Session s)
        {
            var p = Profiles.Get(s.PlayerId);
            int level = XpCurve.LevelForXp(p.TotalXp, Content.Game.progression);
            var list = new List<CosmeticDto>();
            foreach (var c in Content.Cosmetics.cosmetics) list.Add(CosmeticDtoFor(p, c, level));
            _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.Shop, Cosmetics = list.ToArray(), Amount = Ledger.GetBalance(s.PlayerId), PremiumBalance = Ledger.GetBalance(s.PlayerId, Core.Economy.Currency.Premium) });
        }

        private void HandlePurchaseCosmetic(Session s, string id)
        {
            var c = FindCosmetic(id);
            if (c == null) { SendError(s.PlayerId, "Unknown item"); return; }
            var p = Profiles.Get(s.PlayerId);
            int level = XpCurve.LevelForXp(p.TotalXp, Content.Game.progression);
            if (OwnsCosmetic(p, c)) { SendError(s.PlayerId, c.displayName + " is already yours"); return; }
            if (c.passTier > 0 && c.priceCoins <= 0 && c.pricePremium <= 0) { SendError(s.PlayerId, c.displayName + " comes with the Season Pass"); return; }
            if (level < c.unlockLevel) { SendError(s.PlayerId, $"{c.displayName} unlocks at level {c.unlockLevel}"); return; }
            string key = $"cosmetic:{s.PlayerId}:{id}";
            if (c.pricePremium > 0)
            {
                if (Ledger.Debit(s.PlayerId, Core.Economy.Currency.Premium, c.pricePremium, "cosmetic:" + id, key, out _) != Core.Economy.TransactionResult.Ok) { SendError(s.PlayerId, $"Not enough P for {c.displayName} ({c.pricePremium} P)"); return; }
            }
            else if (c.priceCoins > 0 && Ledger.Debit(s.PlayerId, c.priceCoins, "cosmetic:" + id, key, out _) != Core.Economy.TransactionResult.Ok)
            {
                SendError(s.PlayerId, $"Not enough Coins for {c.displayName} ({c.priceCoins:N0} C)"); return;
            }
            p.OwnedCosmeticIds.Add(id);
            p.EquippedCosmetics[c.kind] = id;
            Profiles.Save(p);
            Log.Info("shop", $"{s.PlayerId} bought {id}");
            SendShop(s);
        }

        private void HandleEquipCosmetic(Session s, string id)
        {
            var c = FindCosmetic(id);
            var p = Profiles.Get(s.PlayerId);
            if (c == null || !OwnsCosmetic(p, c)) { SendError(s.PlayerId, "You don't own that"); return; }
            p.EquippedCosmetics[c.kind] = id;
            Profiles.Save(p);
            SendShop(s);
        }

        // ---- season pass (design 18) ----
        private SeasonPassDto BuildSeasonPass(PlayerProfile p)
        {
            var def = Content.SeasonPass;
            int level = XpCurve.LevelForXp(p.TotalXp, Content.Game.progression);
            int xpPerTier = Math.Max(1, def.xpPerTier);
            int current = (int)Math.Min(def.tiers.Length, p.SeasonXp / xpPerTier);
            var tiers = new List<PassTierDto>();
            foreach (var t in def.tiers)
            {
                bool reached = t.tier <= current;
                string free = p.ClaimedPassTiers.Contains("free:" + t.tier) ? "Claimed" : reached ? "Claimable" : "Locked";
                string prem = p.ClaimedPassTiers.Contains("premium:" + t.tier) ? "Claimed" : reached && p.PremiumPass ? "Claimable" : "Locked";
                var fc = t.freeCosmeticId != null ? FindCosmetic(t.freeCosmeticId) : null;
                var pc = t.premiumCosmeticId != null ? FindCosmetic(t.premiumCosmeticId) : null;
                tiers.Add(new PassTierDto { Tier = t.tier, FreeCoins = t.freeCoins, PremiumCoins = t.premiumCoins, FreeCosmetic = fc != null ? CosmeticDtoFor(p, fc, level) : null, PremiumCosmetic = pc != null ? CosmeticDtoFor(p, pc, level) : null, FreeState = free, PremiumState = prem });
            }
            var cfg = Content.Game.ranked;
            return new SeasonPassDto { SeasonId = CurrentSeasonId(), SeasonName = cfg.seasonName, DaysLeft = SeasonClock.DaysLeft(cfg, UtcNow()), SeasonXp = p.SeasonXp, XpPerTier = xpPerTier, CurrentTier = current, PremiumOwned = p.PremiumPass, PremiumPricePremium = def.premiumPricePremium, Tiers = tiers.ToArray() };
        }

        private void SendSeasonPass(Session s) =>
            _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.SeasonPass, SeasonPass = BuildSeasonPass(Profiles.Get(s.PlayerId)), Amount = Ledger.GetBalance(s.PlayerId), PremiumBalance = Ledger.GetBalance(s.PlayerId, Core.Economy.Currency.Premium) });

        private void HandleClaimPassTier(Session s, int tier, bool premium)
        {
            var p = Profiles.Get(s.PlayerId);
            var def = Content.SeasonPass;
            SeasonPassTier t = null; foreach (var x in def.tiers) if (x.tier == tier) t = x;
            if (t == null) { SendError(s.PlayerId, "No such tier"); return; }
            int current = (int)Math.Min(def.tiers.Length, p.SeasonXp / Math.Max(1, def.xpPerTier));
            if (tier > current) { SendError(s.PlayerId, $"Tier {tier} needs more season XP"); return; }
            if (premium && !p.PremiumPass) { SendError(s.PlayerId, "Get the Premium Pass first"); return; }
            string key = (premium ? "premium:" : "free:") + tier;
            if (p.ClaimedPassTiers.Contains(key)) { SendError(s.PlayerId, "Already claimed"); return; }
            long coins = premium ? t.premiumCoins : t.freeCoins;
            string cosmeticId = premium ? t.premiumCosmeticId : t.freeCosmeticId;
            if (coins > 0) Ledger.Credit(s.PlayerId, coins, "season_pass:" + key, $"pass:{CurrentSeasonId()}:{s.PlayerId}:{key}", out _);
            if (cosmeticId != null && !p.OwnedCosmeticIds.Contains(cosmeticId)) p.OwnedCosmeticIds.Add(cosmeticId);
            p.ClaimedPassTiers.Add(key);
            Profiles.Save(p);
            SendSeasonPass(s);
        }

        private void HandleBuyPremiumPass(Session s)
        {
            var p = Profiles.Get(s.PlayerId);
            if (p.PremiumPass) { SendError(s.PlayerId, "You already have the Premium Pass"); return; }
            long price = Content.SeasonPass.premiumPricePremium;
            if (price > 0 && Ledger.Debit(s.PlayerId, Core.Economy.Currency.Premium, price, "premium_pass", $"pass:{CurrentSeasonId()}:{s.PlayerId}:premium", out _) != Core.Economy.TransactionResult.Ok)
            { SendError(s.PlayerId, $"Not enough P for the Premium Pass ({price} P)"); return; }
            p.PremiumPass = true;
            Profiles.Save(p);
            SendSeasonPass(s);
        }

        // ---- garage ----
        public bool OwnsVehicle(PlayerProfile p, VehicleDefinition v) =>
            v != null && ((v.priceCoins <= 0 && v.unlockLevel <= 0) || p.UnlockedVehicleIds.Contains(v.id)
                          || (v.priceCoins <= 0 && XpCurve.LevelForXp(p.TotalXp, Content.Game.progression) >= v.unlockLevel));

        public bool OwnsCharacter(PlayerProfile p, CharacterDefinition c) =>
            c != null && ((c.priceCoins <= 0 && c.unlockLevel <= 0) || p.UnlockedCharacterIds.Contains(c.id)
                          || (c.priceCoins <= 0 && XpCurve.LevelForXp(p.TotalXp, Content.Game.progression) >= c.unlockLevel));

        private VehicleDefinition FindVehicle(string id) { foreach (var v in Content.Vehicles.vehicles) if (v.id == id) return v; return null; }
        private CharacterDefinition FindCharacter(string id) { foreach (var c in Content.Characters.characters) if (c.id == id) return c; return null; }

        /// <summary>
        /// Resolves a requested loadout against ownership. Null ids fall back to the profile's selection,
        /// then to the first starter. Returns false if a named vehicle/character is not owned (ids are
        /// then replaced with owned fallbacks so the racer can still race).
        /// </summary>
        public bool ResolveLoadout(string playerId, ref string vehicleId, ref string characterId)
        {
            var p = Profiles.Get(playerId);
            bool ok = true;
            var v = FindVehicle(vehicleId ?? p.SelectedVehicleId);
            if (vehicleId != null && (v == null || !OwnsVehicle(p, v))) ok = false;
            if (v == null || !OwnsVehicle(p, v)) v = FirstOwnedVehicle(p);
            var c = FindCharacter(characterId ?? p.SelectedCharacterId);
            if (characterId != null && c != null && !OwnsCharacter(p, c)) ok = false;
            if (c != null && !OwnsCharacter(p, c)) c = null;
            vehicleId = v?.id;
            characterId = c?.id;
            if (ok)
            {
                p.SelectedVehicleId = vehicleId;
                if (characterId != null) p.SelectedCharacterId = characterId;
                Profiles.Save(p);
            }
            return ok;
        }

        private VehicleDefinition FirstOwnedVehicle(PlayerProfile p)
        {
            foreach (var v in Content.Vehicles.vehicles) if (OwnsVehicle(p, v)) return v;
            return Content.Vehicles.vehicles[0];
        }

        private void HandlePurchase(Session s, string id, bool isVehicle)
        {
            var p = Profiles.Get(s.PlayerId);
            int level = XpCurve.LevelForXp(p.TotalXp, Content.Game.progression);
            long price; int unlockLevel; bool owned; string name;
            if (isVehicle)
            {
                var v = FindVehicle(id);
                if (v == null) { SendError(s.PlayerId, "Unknown kart"); return; }
                price = v.priceCoins; unlockLevel = v.unlockLevel; owned = OwnsVehicle(p, v); name = v.displayName;
            }
            else
            {
                var c = FindCharacter(id);
                if (c == null) { SendError(s.PlayerId, "Unknown racer"); return; }
                price = c.priceCoins; unlockLevel = c.unlockLevel; owned = OwnsCharacter(p, c); name = c.displayName;
            }
            if (owned) { SendError(s.PlayerId, name + " is already yours"); return; }
            if (level < unlockLevel) { SendError(s.PlayerId, $"{name} unlocks at level {unlockLevel}"); return; }
            // Earned Coins only: premium currency never buys anything that affects a race (PRD §37).
            string key = $"purchase:{s.PlayerId}:{(isVehicle ? "vehicle" : "character")}:{id}";
            if (price > 0 && Ledger.Debit(s.PlayerId, price, "purchase:" + id, key, out _) != Core.Economy.TransactionResult.Ok)
            {
                SendError(s.PlayerId, $"Not enough Coins for {name} ({price:N0} C)");
                return;
            }
            if (isVehicle) { p.UnlockedVehicleIds.Add(id); p.SelectedVehicleId = id; }
            else { p.UnlockedCharacterIds.Add(id); p.SelectedCharacterId = id; }
            Profiles.Save(p);
            Log.Info("garage", $"{s.PlayerId} bought {id} for {price}");
            SendGarage(s);
        }

        private void SendGarage(Session s)
        {
            var p = Profiles.Get(s.PlayerId);
            int level = XpCurve.LevelForXp(p.TotalXp, Content.Game.progression);
            var vehicles = new List<GarageItemDto>();
            foreach (var v in Content.Vehicles.vehicles)
            {
                vehicles.Add(new GarageItemDto
                {
                    Id = v.id, DisplayName = v.displayName, Tagline = v.tagline ?? v.description, Owned = OwnsVehicle(p, v),
                    Selected = v.id == (p.SelectedVehicleId ?? FirstOwnedVehicle(p).id), PriceCoins = v.priceCoins, UnlockLevel = v.unlockLevel,
                    LevelReached = level >= v.unlockLevel,
                    Speed = v.speed, Acceleration = v.acceleration, Handling = v.handling, Drift = v.drift, Weight = v.weight, Boost = v.boost, Traction = v.traction
                });
            }
            var characters = new List<GarageItemDto>();
            foreach (var c in Content.Characters.characters)
            {
                characters.Add(new GarageItemDto
                {
                    Id = c.id, DisplayName = c.displayName, Tagline = c.archetype, Owned = OwnsCharacter(p, c),
                    Selected = c.id == p.SelectedCharacterId, PriceCoins = c.priceCoins, UnlockLevel = c.unlockLevel, LevelReached = level >= c.unlockLevel
                });
            }
            _transport.Send(s.ConnectionId, new ServerEnvelope
            {
                Kind = ServerMessageKind.Garage,
                Vehicles = vehicles.ToArray(),
                Characters = characters.ToArray(),
                Amount = Ledger.GetBalance(s.PlayerId),
                PremiumBalance = Ledger.GetBalance(s.PlayerId, Core.Economy.Currency.Premium)
            });
        }

        /// <summary>Friends currently connected who could pay bail (shown on the PULL OVER dialog).</summary>
        public int FriendsOnline(string playerId, RaceRoom room)
        {
            int n = 0;
            var seen = new HashSet<string>();
            foreach (var f in Profiles.Get(playerId).FriendIds)
                if (seen.Add(f) && _byPlayer.TryGetValue(f, out var s) && s.ConnectionId != null) n++;
            foreach (var m in room.Members)
                if (m != playerId && seen.Add(m) && _byPlayer.TryGetValue(m, out var s) && s.ConnectionId != null) n++;
            return n;
        }

        private void HandleAddFriend(Session s, string targetId)
        {
            // Request/accept flow (design 16, ADR-0006 follow-up). A request answered by a request is an accept.
            if (string.IsNullOrWhiteSpace(targetId) || targetId == s.PlayerId) { SendError(s.PlayerId, "Invalid friend id"); return; }
            var me = Profiles.Get(s.PlayerId);
            var them = Profiles.Get(targetId);
            if (me.FriendIds.Contains(targetId)) { SendFriends(s); return; }
            if (!Content.Game.social.friendRequestsRequireAccept || me.FriendRequestsIn.Contains(targetId))
            {
                Befriend(me, them);
                SendFriends(s);
                if (_byPlayer.TryGetValue(targetId, out var ts) && ts.ConnectionId != null) SendFriends(ts);
                return;
            }
            if (!me.FriendRequestsOut.Contains(targetId)) me.FriendRequestsOut.Add(targetId);
            if (!them.FriendRequestsIn.Contains(s.PlayerId)) them.FriendRequestsIn.Add(s.PlayerId);
            Profiles.Save(me); Profiles.Save(them);
            SendTo(targetId, new ServerEnvelope { Kind = ServerMessageKind.FriendRequest, PlayerId = s.PlayerId, Text = s.DisplayName });
            SendFriends(s);
        }

        private void HandleAcceptFriend(Session s, string targetId)
        {
            var me = Profiles.Get(s.PlayerId);
            if (targetId == null || !me.FriendRequestsIn.Contains(targetId)) { SendError(s.PlayerId, "No request from that racer"); return; }
            Befriend(me, Profiles.Get(targetId));
            SendFriends(s);
            if (_byPlayer.TryGetValue(targetId, out var ts) && ts.ConnectionId != null) SendFriends(ts);
        }

        private void Befriend(PlayerProfile me, PlayerProfile them)
        {
            if (!me.FriendIds.Contains(them.PlayerId)) me.FriendIds.Add(them.PlayerId);
            if (!them.FriendIds.Contains(me.PlayerId)) them.FriendIds.Add(me.PlayerId);
            me.FriendRequestsIn.Remove(them.PlayerId); me.FriendRequestsOut.Remove(them.PlayerId);
            them.FriendRequestsIn.Remove(me.PlayerId); them.FriendRequestsOut.Remove(me.PlayerId);
            Profiles.Save(me); Profiles.Save(them);
        }

        /// <summary>Presence for the Social screen: Online, Racing · lap N, Private Room, Seen 2h ago.</summary>
        public FriendDto FriendDtoFor(string playerId)
        {
            var p = Profiles.Get(playerId);
            var dto = new FriendDto
            {
                PlayerId = playerId, DisplayName = p.DisplayName, RankId = RankLadder.TierFor(p.RankedPoints).id, RankLabel = RankLadder.Label(p.RankedPoints),
                Level = XpCurve.LevelForXp(p.TotalXp, Content.Game.progression), LastSeenUnixMs = p.LastSeenUnixMs, BailCost = Content.Game.lastma.bailAmount > 0 ? Content.Game.lastma.bailAmount : Content.Game.lastma.fineAmount
            };
            bool online = _byPlayer.TryGetValue(playerId, out var s) && s.ConnectionId != null;
            if (!online)
            {
                dto.Presence = "Offline";
                long ago = p.LastSeenUnixMs > 0 ? new DateTimeOffset(UtcNow()).ToUnixTimeMilliseconds() - p.LastSeenUnixMs : -1;
                dto.PresenceText = ago < 0 ? "Seen a while ago" : ago < 3600_000 ? $"Seen {Math.Max(1, ago / 60_000)}m ago" : ago < 86_400_000 ? $"Seen {ago / 3600_000}h ago" : ago < 2 * 86_400_000 ? "Seen yesterday" : $"Seen {ago / 86_400_000} days ago";
                return dto;
            }
            var room = s.Room;
            if (room != null && (room.Race.StateMachine.IsRacing || room.Race.State == Core.Race.RaceState.Countdown))
            {
                var part = room.Race.Find(playerId);
                int lap = Math.Min(room.Laps, (part?.Checkpoints?.LapsCompleted ?? 0) + 1);
                dto.Presence = "Racing"; dto.PresenceText = $"Racing · lap {lap}";
                var le = room.Race.LastmaFor(playerId);
                dto.NeedsBail = le != null && le.BailRequested && le.BailPaidBy == null && (le.Phase == Core.Lastma.LastmaPhase.FinePending || le.Phase == Core.Lastma.LastmaPhase.Penalty);
            }
            else if (room != null && room.Mode == RaceMode.PrivateRoom)
            {
                dto.Presence = "PrivateRoom"; dto.PresenceText = "Private Room"; dto.RoomCode = room.Code;
            }
            else { dto.Presence = "Online"; dto.PresenceText = "Online"; }
            return dto;
        }

        private void SendFriends(Session s)
        {
            var me = Profiles.Get(s.PlayerId);
            var friends = me.FriendIds.ConvertAll(FriendDtoFor);
            friends.Sort((a, b) => Rank(a).CompareTo(Rank(b)) != 0 ? Rank(a).CompareTo(Rank(b)) : string.CompareOrdinal(a.DisplayName, b.DisplayName));
            _transport.Send(s.ConnectionId, new ServerEnvelope
            {
                Kind = ServerMessageKind.Friends,
                Friends = friends.ToArray(),
                FriendRequests = me.FriendRequestsIn.ConvertAll(FriendDtoFor).ToArray(),
                SentRequests = me.FriendRequestsOut.ConvertAll(FriendDtoFor).ToArray()
            });
            static int Rank(FriendDto f) => f.NeedsBail ? 0 : f.Presence == "Racing" ? 1 : f.Presence == "PrivateRoom" ? 2 : f.Presence == "Online" ? 3 : 4;
        }

        /// <summary>A friend paid bail: count it on the rivalry ("He bailed you 4 times").</summary>
        public void RecordBail(string payerId, string targetId)
        {
            var r = Rivalries.GetOrCreate(payerId, targetId);
            r.RecordBail(payerId);
            Rivalries.Save(r);
        }

        private void HandleRemoveFriend(Session s, string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return;
            var me = Profiles.Get(s.PlayerId);
            var them = Profiles.Get(targetId);
            me.FriendIds.Remove(targetId);
            them.FriendIds.Remove(s.PlayerId);
            Profiles.Save(me); Profiles.Save(them);
            _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.Profile, Profile = BuildProfile(s.PlayerId) });
        }

        private ChallengeProgressDto ToDto(ChallengeDefinition rule, ChallengeProgress p) => new ChallengeProgressDto
        {
            ChallengeId = rule.id,
            DisplayName = rule.displayName,
            Description = rule.description,
            Cadence = rule.cadence.ToString(),
            Value = p?.value ?? rule.target,
            Target = rule.target,
            Completed = p?.completed ?? true,
            RewardCoins = rule.rewardCoins,
            RewardXp = rule.rewardXp
        };

        private ChallengeProgressDto[] BuildChallenges(string playerId)
        {
            var progress = Challenges.CurrentProgress(playerId);
            var list = new List<ChallengeProgressDto>();
            foreach (var p in progress)
            {
                var rule = Challenges.Rule(p.challengeId);
                if (rule != null) list.Add(ToDto(rule, p));
            }
            return list.ToArray();
        }

        /// <summary>Leaderboard metrics: rating (default), wins, streak, lastma. friendsOf restricts to a player's friends (+ self).</summary>
        /// <summary>Leaderboards (design 15): metric rp|wins|streak|lastma|level|track, scope friends|city|nigeria|global|track.</summary>
        private LeaderboardRowDto[] BuildLeaderboard(string metric, string scope, string requester, string trackId, int limit = 50)
        {
            metric = (metric ?? "rp").ToLowerInvariant();
            scope = (scope ?? "global").ToLowerInvariant();
            if (scope == "track" || metric == "track") { metric = "track"; scope = "track"; trackId ??= Content.TrackIds[0]; }
            HashSet<string> filter = null;
            string city = null;
            if (scope == "friends") filter = new HashSet<string>(Profiles.Get(requester).FriendIds) { requester };
            if (scope == "city") city = Accounts.Get(requester).HomeCity ?? "Other";
            var rows = new List<LeaderboardRowDto>();
            foreach (var p in Profiles.All())
            {
                if (filter != null && !filter.Contains(p.PlayerId)) continue;
                var acc = Accounts.Get(p.PlayerId);
                if (city != null && !string.Equals(acc.HomeCity, city, StringComparison.OrdinalIgnoreCase)) continue;
                long value;
                float seconds = 0f;
                if (metric == "track")
                {
                    if (!p.TrackBestTimes.TryGetValue(trackId, out seconds)) continue;
                    value = (long)Math.Round(seconds * 1000f);
                }
                else value = metric switch
                {
                    "wins" => p.Wins,
                    "streak" => p.BestWinStreak,
                    "lastma" => p.LastmaEscapes,
                    "level" => p.TotalXp,
                    _ => p.RankedPoints
                };
                rows.Add(new LeaderboardRowDto { PlayerId = p.PlayerId, DisplayName = p.DisplayName, Value = value, Seconds = seconds, City = acc.HomeCity, RankId = RankLadder.TierFor(p.RankedPoints).id, RankLabel = RankLadder.Label(p.RankedPoints) });
            }
            if (metric == "track") rows.Sort((a, b) => a.Value.CompareTo(b.Value) != 0 ? a.Value.CompareTo(b.Value) : string.CompareOrdinal(a.PlayerId, b.PlayerId));
            else rows.Sort((a, b) => b.Value.CompareTo(a.Value) != 0 ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.PlayerId, b.PlayerId));
            if (rows.Count > limit) rows.RemoveRange(limit, rows.Count - limit);
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            return rows.ToArray();
        }

        private int CountAchievements()
        {
            int n = 0;
            foreach (var c in Content.Challenges.challenges) if (c.cadence == ChallengeCadence.Achievement) n++;
            return n;
        }

        private RivalryDto[] TopRivalries(string playerId, int n)
        {
            var all = BuildRivalries(playerId);
            if (all.Length > n) Array.Resize(ref all, n);
            return all;
        }

        private RivalryDto[] BuildRivalries(string playerId)
        {
            var list = new List<RivalryDto>();
            foreach (var r in Rivalries.For(playerId))
            {
                string other = r.PlayerA == playerId ? r.PlayerB : r.PlayerA;
                bool iAmA = r.PlayerA == playerId;
                list.Add(new RivalryDto
                {
                    OpponentId = other,
                    OpponentName = Profiles.Get(other).DisplayName,
                    MyWins = r.WinsFor(playerId),
                    TheirWins = r.WinsFor(other),
                    TotalRaces = r.TotalRaces,
                    MyFastestLap = iAmA ? r.FastestLapA : r.FastestLapB,
                    TheirFastestLap = iAmA ? r.FastestLapB : r.FastestLapA,
                    LastWinner = r.LastWinner,
                    MyStreak = r.StreakFor(playerId),
                    TheirStreak = r.StreakFor(other),
                    RecentIWon = r.RecentWinners.ConvertAll(w => w == playerId).ToArray(),
                    BailsTheyGaveMe = r.BailsGivenBy(other),
                    BailsIGaveThem = r.BailsGivenBy(playerId),
                    SinceUnixMs = r.SinceUnixMs,
                    OpponentRankLabel = RankLadder.Label(Profiles.Get(other).RankedPoints)
                });
                // The track you two race most, with both best times
                var mine = r.BestTimesFor(playerId); var theirs = r.BestTimesFor(other);
                foreach (var kv in mine)
                    if (theirs.TryGetValue(kv.Key, out float t)) { var dto = list[list.Count - 1]; dto.BestTrackId = kv.Key; dto.MyBestTime = kv.Value; dto.TheirBestTime = t; break; }
            }
            list.Sort((a, b) => b.TotalRaces.CompareTo(a.TotalRaces));
            return list.ToArray();
        }

        private ProfileDto BuildProfile(string playerId)
        {
            var p = Profiles.Get(playerId);
            var prog = Content.Game.progression;
            return new ProfileDto
            {
                PlayerId = p.PlayerId,
                DisplayName = p.DisplayName,
                Level = XpCurve.LevelForXp(p.TotalXp, prog),
                LevelProgress = XpCurve.LevelProgress(p.TotalXp, prog),
                Rating = p.Rating,
                RankId = RankLadder.TierFor(p.RankedPoints).id,
                RankLabel = RankLadder.Label(p.RankedPoints),
                RankedPoints = p.RankedPoints,
                Division = RankLadder.DivisionFor(p.RankedPoints),
                RpInDivision = RankLadder.RpInDivision(p.RankedPoints),
                RpToNextDivision = RankLadder.RpToNextDivision(p.RankedPoints),
                RecentPositions = p.RecentRanked.ConvertAll(r => r.Position).ToArray(),
                RecentRpDeltas = p.RecentRanked.ConvertAll(r => r.RpDelta).ToArray(),
                HomeCity = Accounts.Get(playerId).HomeCity,
                RacerCode = Accounts.Get(playerId).ReferralCode,
                AchievementsTotal = CountAchievements(),
                LastSeenUnixMs = p.LastSeenUnixMs,
                BestTrackIds = new List<string>(p.TrackBestTimes.Keys).ToArray(),
                BestTrackTimes = new List<float>(p.TrackBestTimes.Values).ToArray(),
                TopRivalries = TopRivalries(playerId, 2),
                Title = p.Title,
                Races = p.Races,
                Wins = p.Wins,
                Podiums = p.Podiums,
                WinRate = p.WinRate,
                LastmaEscapes = p.LastmaEscapes,
                CurrentWinStreak = p.CurrentWinStreak,
                BestWinStreak = p.BestWinStreak,
                Coins = Ledger.GetBalance(playerId),
                Premium = Ledger.GetBalance(playerId, Core.Economy.Currency.Premium),
                Achievements = p.Achievements.ToArray(),
                FriendIds = p.FriendIds.ToArray(),
                UnlockedVehicleIds = p.UnlockedVehicleIds.ToArray(),
                UnlockedCharacterIds = p.UnlockedCharacterIds.ToArray(),
                SelectedVehicleId = p.SelectedVehicleId,
                SelectedCharacterId = p.SelectedCharacterId
            };
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
