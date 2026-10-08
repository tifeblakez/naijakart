using System;
using System.Collections.Generic;
using NaijaKart.Core.Accounts;
using NaijaKart.Core.Challenges;
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
            Settlement = new RaceSettlementService(content.Game, Ledger, Profiles, Rivalries);
            RankLadder = new RankLadder(content.Game.progression);
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
                case ClientMessageKind.AddFriend:
                    HandleAddFriend(s, msg.TargetPlayerId);
                    break;
                case ClientMessageKind.RemoveFriend:
                    HandleRemoveFriend(s, msg.TargetPlayerId);
                    break;
                case ClientMessageKind.GetChallenges:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Challenges, Challenges = BuildChallenges(s.PlayerId) });
                    break;
                case ClientMessageKind.GetLeaderboard:
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Leaderboard, Text = msg.Text ?? "rating", Leaderboard = BuildLeaderboard(msg.Text, msg.Flag ? s.PlayerId : null) });
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
            Profiles.Save(profile);
            _transport.Send(s.ConnectionId, new ServerEnvelope
            {
                Kind = ServerMessageKind.Welcome,
                PlayerId = playerId,
                Amount = Ledger.GetBalance(playerId),
                PremiumBalance = Ledger.GetBalance(playerId, Core.Economy.Currency.Premium),
                Text = RankLadder.TierFor(profile.Rating).id,
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
            RaceSettled?.Invoke(room, results);
        }

        public WeekdayRule TodayRule() => Content.Game.liveEvents?.RuleFor((int)UtcNow().DayOfWeek);

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
            // First pass: symmetric friendship on request (ADR-0006: request/accept flow comes with accounts).
            if (string.IsNullOrWhiteSpace(targetId) || targetId == s.PlayerId) { SendError(s.PlayerId, "Invalid friend id"); return; }
            var me = Profiles.Get(s.PlayerId);
            var them = Profiles.Get(targetId);
            if (!me.FriendIds.Contains(targetId)) me.FriendIds.Add(targetId);
            if (!them.FriendIds.Contains(s.PlayerId)) them.FriendIds.Add(s.PlayerId);
            Profiles.Save(me); Profiles.Save(them);
            _transport.Send(s.ConnectionId, new ServerEnvelope { Kind = ServerMessageKind.Profile, Profile = BuildProfile(s.PlayerId) });
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
        private LeaderboardRowDto[] BuildLeaderboard(string metric, string friendsOf, int limit = 50)
        {
            metric = (metric ?? "rating").ToLowerInvariant();
            HashSet<string> filter = null;
            if (friendsOf != null)
            {
                filter = new HashSet<string>(Profiles.Get(friendsOf).FriendIds) { friendsOf };
            }
            var rows = new List<LeaderboardRowDto>();
            foreach (var p in Profiles.All())
            {
                if (filter != null && !filter.Contains(p.PlayerId)) continue;
                long value = metric switch
                {
                    "wins" => p.Wins,
                    "streak" => p.BestWinStreak,
                    "lastma" => p.LastmaEscapes,
                    "level" => p.TotalXp,
                    _ => p.Rating
                };
                rows.Add(new LeaderboardRowDto { PlayerId = p.PlayerId, DisplayName = p.DisplayName, Value = value, RankId = RankLadder.TierFor(p.Rating).id });
            }
            rows.Sort((a, b) => b.Value.CompareTo(a.Value) != 0 ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.PlayerId, b.PlayerId));
            if (rows.Count > limit) rows.RemoveRange(limit, rows.Count - limit);
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            return rows.ToArray();
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
                    TheirStreak = r.StreakFor(other)
                });
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
                RankId = RankLadder.TierFor(p.Rating).id,
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
