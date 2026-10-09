using System;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Net;
using NaijaKart.Core.Progression;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;

namespace NaijaKart.Server.Hosting
{
    /// <summary>
    /// One room = a group of players who race together, possibly many times (rematch). Owns the live
    /// RaceSimulation, forwards snapshots/events to members, applies intents after validation, and
    /// hands finished races to settlement. Bots (explicitly requested) are driven here.
    /// </summary>
    public sealed class RaceRoom
    {
        private readonly GameServer _server;
        private readonly IConfigSource _content;
        private readonly GameConfig _cfg;
        private readonly ILogger _log;
        private readonly Dictionary<string, BotDriver> _bots = new Dictionary<string, BotDriver>();
        /// <summary>AI stand-ins driving for disconnected humans (design 11: "AI driving · holding your place").</summary>
        private readonly Dictionary<string, BotDriver> _standIns = new Dictionary<string, BotDriver>();
        /// <summary>Invited friends and their state: Invited, Joining.</summary>
        private readonly Dictionary<string, string> _invited = new Dictionary<string, string>();
        private readonly List<RaceEvent> _eventBuffer = new List<RaceEvent>();
        private readonly List<string> _members = new List<string>();
        private readonly Dictionary<string, (string vehicle, string character)> _loadouts = new Dictionary<string, (string, string)>();
        private float _snapshotAccumulator;
        private float _resultsTime;
        private float _resultsResendTimer;
        private SettledRewardDto[] _lastRewards = System.Array.Empty<SettledRewardDto>();
        private int _raceCounter;
        private ulong _seed;
        private WeekdayRule _liveEvent;

        public string Code { get; }
        public string HostPlayerId { get; private set; }
        public RaceMode Mode { get; }
        public string TrackId { get; private set; }
        public int Laps { get; private set; }
        public bool ItemsEnabled { get; private set; } = true;
        public bool LastmaEnabled { get; private set; } = true;
        /// <summary>Off | On | Madness (design 03.2).</summary>
        public string LastmaMode { get; private set; } = "On";
        /// <summary>Off | BoostsOnly | On.</summary>
        public string ItemsMode { get; private set; } = "On";
        public bool FillWithAi { get; private set; }
        /// <summary>Set for rooms created by a tournament.</summary>
        public string TournamentId { get; set; }
        public RaceSimulation Race { get; private set; }
        public IReadOnlyList<string> Members => _members;
        public bool HasMember(string playerId) => _members.Contains(playerId);
        public bool IsEmpty => _members.Count == 0;
        public bool Settled { get; private set; }
        /// <summary>Raised with each tick's drained race events (replays, analytics).</summary>
        public event System.Action<List<RaceEvent>> EventsDrained;

        public RaceRoom(GameServer server, string code, RaceMode mode, string trackId, int laps, string hostPlayerId, ulong seed)
        {
            _server = server;
            _content = server.Content;
            _cfg = server.Content.Game;
            _log = server.Log;
            Code = code;
            Mode = mode;
            TrackId = trackId;
            Laps = laps <= 0 ? _cfg.raceRules.defaultLaps : laps;
            HostPlayerId = hostPlayerId;
            _seed = seed;
            CreateRace();
        }

        private void CreateRace()
        {
            _raceCounter++;
            var setup = new RaceSetup
            {
                RaceId = Code + "-r" + _raceCounter,
                TrackId = TrackId,
                Mode = Mode,
                Laps = Laps,
                Seed = _seed + (ulong)_raceCounter * 7919UL,
                ItemsEnabled = ItemsEnabled,
                LastmaEnabled = LastmaEnabled,
                RoadEventsEnabled = true,
                MaxPlayers = _cfg.simulation.maxPlayersPerRace,
                LiveEventId = _liveEvent?.id,
                AllowedItemIds = AllowedItems(),
                LastmaIntervalMultiplier = (_liveEvent?.lastmaIntervalMultiplier ?? 1f) * (LastmaMode == "Madness" ? _cfg.social.lastmaMadnessIntervalMultiplier : 1f),
                DriftBoostMultiplier = _liveEvent?.driftBoostMultiplier ?? 1f
            };
            Race = new RaceSimulation(setup, _content, _server.Wallet, _log);
            Settled = false;
            _snapshotAccumulator = 0f;
            _resultsTime = 0f;
            foreach (var id in _members)
            {
                var lo = _loadouts.TryGetValue(id, out var l) ? l : (null, null);
                Race.AddParticipant(id, _server.DisplayNameOf(id), lo.vehicle, lo.character, _bots.ContainsKey(id));
            }
            BroadcastRoomState();
        }

        /// <summary>Items allowed by the live event and the room's items mode ("Boosts only" keeps the Boost category).</summary>
        private List<string> AllowedItems()
        {
            List<string> list = _liveEvent?.allowedItemIds != null && _liveEvent.allowedItemIds.Length > 0 ? new List<string>(_liveEvent.allowedItemIds) : null;
            if (ItemsMode != "BoostsOnly") return list;
            var boosts = new List<string>();
            foreach (var it in _content.Items.items)
                if (it.category == ItemCategory.Boost && (list == null || list.Contains(it.id))) boosts.Add(it.id);
            return boosts;
        }

        public bool Join(string playerId, string vehicleId, string characterId)
        {
            if (_members.Contains(playerId)) return true;
            if (Race.State != RaceState.Lobby && Race.State != RaceState.Waiting) return false;
            _server.ResolveLoadout(playerId, ref vehicleId, ref characterId);
            if (!Race.AddParticipant(playerId, _server.DisplayNameOf(playerId), vehicleId, characterId)) return false;
            _members.Add(playerId);
            _loadouts[playerId] = (vehicleId, characterId);
            _invited.Remove(playerId);
            if (HostPlayerId == null) HostPlayerId = playerId;
            BroadcastRoomState();
            return true;
        }

        public void Invite(string playerId, string status = "Invited")
        {
            if (_members.Contains(playerId)) return;
            _invited[playerId] = status;
            BroadcastRoomState();
        }

        public bool IsInvited(string playerId) => _invited.ContainsKey(playerId);

        public void AddBot(string botId, float skill, string vehicleId = null)
        {
            if (_members.Contains(botId)) return;
            var vehicles = _content.Vehicles.vehicles;
            string vehicle = vehicleId ?? vehicles[(int)((_seed + (ulong)_members.Count) % (ulong)vehicles.Length)].id;
            if (!Race.AddParticipant(botId, botId, vehicle, null, isBot: true)) return;
            _server.Ledger.EnsureAccount(botId);
            _members.Add(botId);
            _loadouts[botId] = (vehicle, null);
            _bots[botId] = new BotDriver(Race.Track, _seed + (ulong)_members.Count * 31UL, skill, _cfg.bots);
            Race.SetReady(botId, true);
        }

        public void Leave(string playerId)
        {
            if (!_members.Remove(playerId)) return;
            _loadouts.Remove(playerId);
            _bots.Remove(playerId);
            Race.RemoveParticipant(playerId);
            if (HostPlayerId == playerId) HostPlayerId = FirstHuman();
            BroadcastRoomState();
        }

        private string FirstHuman()
        {
            foreach (var m in _members) if (!_bots.ContainsKey(m)) return m;
            return null;
        }

        public void SetLoadout(string playerId, string vehicleId, string characterId)
        {
            if (!_members.Contains(playerId) || Race.State != RaceState.Lobby) return;
            if (!_server.ResolveLoadout(playerId, ref vehicleId, ref characterId)) { _server.SendError(playerId, "You don't own that kart or racer"); return; }
            _loadouts[playerId] = (vehicleId, characterId);
            Race.RemoveParticipant(playerId);
            Race.AddParticipant(playerId, _server.DisplayNameOf(playerId), vehicleId, characterId);
            BroadcastRoomState();
        }

        public void SetReady(string playerId, bool ready)
        {
            Race.SetReady(playerId, ready);
            BroadcastRoomState();
            TryAutoStart();
        }

        public bool StartByHost(string playerId, bool fillWithBots)
        {
            if (playerId != HostPlayerId || Race.State != RaceState.Lobby) return false;
            if ((fillWithBots || FillWithAi) && Mode != RaceMode.Ranked)
            {
                int i = 0;
                while (_members.Count < _cfg.simulation.maxPlayersPerRace) AddBot("bot_" + Code.ToLowerInvariant().Replace("-", "") + "_" + (++i), 0.45f + 0.07f * i);
            }
            if (!Race.CanStart) return false;
            Race.BeginCountdown();
            BroadcastRoomState();
            return true;
        }

        /// <summary>Applies a Wahala Calendar rule (public races only). Must be called before the race starts.</summary>
        public void ApplyLiveEvent(WeekdayRule rule)
        {
            if (rule == null || (Race.State != RaceState.Lobby && Race.State != RaceState.Waiting)) return;
            _liveEvent = rule;
            ItemsEnabled = rule.itemsEnabled;
            LastmaEnabled = rule.lastmaEnabled;
            CreateRace();
        }

        public WeekdayRule LiveEvent => _liveEvent;

        public void Configure(string trackId, int laps, bool items, bool lastma) => Configure(trackId, laps, items ? null : "Off", lastma ? null : "Off", null);

        /// <summary>Private room options (design 03.2). Null leaves an option unchanged.</summary>
        public void Configure(string trackId, int laps, string itemsMode, string lastmaMode, bool? fillWithAi)
        {
            if (Race.State != RaceState.Lobby) return;
            if (!string.IsNullOrEmpty(trackId) && _content.GetTrack(trackId) != null) TrackId = trackId;
            if (laps > 0) Laps = laps;
            if (itemsMode == "Off" || itemsMode == "BoostsOnly" || itemsMode == "On") ItemsMode = itemsMode;
            if (lastmaMode == "Off" || lastmaMode == "On" || lastmaMode == "Madness") LastmaMode = lastmaMode;
            if (fillWithAi.HasValue) FillWithAi = fillWithAi.Value;
            ItemsEnabled = ItemsMode != "Off";
            LastmaEnabled = LastmaMode != "Off";
            CreateRace();
        }

        private void TryAutoStart()
        {
            if (Race.State != RaceState.Lobby) return;
            if (Mode == RaceMode.PrivateRoom) return; // host starts explicitly
            if (Race.AllReady && Race.CanStart) Race.BeginCountdown();
        }

        public void Tick(float dt)
        {
            if (Race.State == RaceState.Lobby && Mode != RaceMode.PrivateRoom && Race.CanStart &&
                Race.StateMachine.TimeInState >= _cfg.raceRules.lobbyReadyTimeoutSeconds)
            {
                Race.BeginCountdown();
            }

            if (Race.StateMachine.IsRacing)
            {
                // AI stand-ins hold a disconnected racer's place: their kart keeps racing until they return
                // or the reconnect window closes (design 11).
                if (_cfg.raceRules.aiStandInWhileDisconnected)
                    foreach (var p in Race.Participants)
                    {
                        if (p.IsBot || p.Status != ParticipantStatus.Disconnected) continue;
                        if (!_standIns.TryGetValue(p.PlayerId, out var driver)) _standIns[p.PlayerId] = driver = new BotDriver(Race.Track, _seed ^ 0xA11EUL, 0.5f, _cfg.bots);
                        Race.SubmitStandInInput(p.PlayerId, driver.Think(p, Race.Hazards.All, Race.Participants, dt));
                        var le = Race.Lastma.EventFor(p.PlayerId);
                        if (le != null && le.Phase == Core.Lastma.LastmaPhase.FinePending && !le.BailRequested) Race.TakePenalty(p.PlayerId);
                    }
                foreach (var kv in _bots)
                {
                    var p = Race.Find(kv.Key);
                    if (p == null || !p.IsActiveRacer) continue;
                    Race.SubmitInput(kv.Key, kv.Value.Think(p, Race.Hazards.All, Race.Participants, dt));
                    var le = Race.Lastma.EventFor(kv.Key);
                    if (le != null && le.Phase == Core.Lastma.LastmaPhase.FinePending && !le.BailRequested)
                    {
                        if (!Race.PayFine(kv.Key)) Race.TakePenalty(kv.Key);
                    }
                }
            }

            Race.Step();
            FlushEvents();

            if (Race.State == RaceState.Results && !Settled)
            {
                Settled = true;
                float xpMul = _liveEvent != null && Mode == RaceMode.Ranked ? _liveEvent.rankedXpMultiplier : 1f;
                var rewards = _server.Settlement.Settle(Race.Results, xpMul);
                _lastRewards = rewards.ToArray();
                SendResults();
                _server.OnRaceSettled(this, Race.Results);
            }

            if (Race.State == RaceState.Results)
            {
                _resultsTime += dt;
                // Results are the one message a client must not miss; re-send while the room idles here
                // so a lossy transport (or a late reconnect) still delivers them. Clients de-duplicate by RaceId.
                _resultsResendTimer -= dt;
                if (_resultsResendTimer <= 0f)
                {
                    _resultsResendTimer = _cfg.simulation.resultsResendSeconds;
                    SendResults();
                }
                if (Race.RematchRequested || (_resultsTime >= _cfg.raceRules.rematchVoteSeconds && AnyRematchVote()))
                {
                    _log.Info("room", $"Room {Code}: RUN AM BACK → new race");
                    PruneNonVoters();
                    CreateRace();
                    foreach (var id in _members) Race.SetReady(id, true);
                    if (Race.CanStart) Race.BeginCountdown();
                }
            }

            if (Race.StateMachine.IsRacing || Race.State == RaceState.Countdown)
            {
                _snapshotAccumulator += dt;
                float interval = 1f / _cfg.simulation.snapshotRate;
                if (_snapshotAccumulator >= interval)
                {
                    _snapshotAccumulator -= interval;
                    SendToMembers(new ServerEnvelope { Kind = ServerMessageKind.RaceSnapshot, Snapshot = Race.BuildSnapshot(), Tick = Race.Tick });
                }
            }
        }

        private bool AnyRematchVote()
        {
            foreach (var p in Race.Participants) if (p.VotedRematch) return true;
            return false;
        }

        private void PruneNonVoters()
        {
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                var p = Race.Find(_members[i]);
                if (p == null) continue;
                if (!p.IsBot && !p.VotedRematch)
                {
                    _server.NotifyLeftRoom(_members[i]);
                    _loadouts.Remove(_members[i]);
                    _members.RemoveAt(i);
                }
            }
            if (HostPlayerId != null && !_members.Contains(HostPlayerId)) HostPlayerId = FirstHuman();
        }

        private void FlushEvents()
        {
            _eventBuffer.Clear();
            Race.DrainEvents(_eventBuffer);
            if (_eventBuffer.Count > 0) EventsDrained?.Invoke(_eventBuffer);
            foreach (var e in _eventBuffer)
            {
                SendToMembers(new ServerEnvelope { Kind = ServerMessageKind.RaceEvent, Event = e, Tick = Race.Tick });
                if (e.Type == RaceEventType.LastmaBailRequested)
                {
                    _server.BroadcastBailRequest(this, e.PlayerId, (long)e.FloatValue);
                }
                if (e.Type == RaceEventType.LastmaCaught)
                {
                    _server.SendTo(e.PlayerId, new ServerEnvelope
                    {
                        Kind = ServerMessageKind.LastmaOptions,
                        PlayerId = e.PlayerId,
                        LastmaOptions = new LastmaOptionsDto
                        {
                            FineAmount = (long)e.FloatValue,
                            DecisionSeconds = _cfg.lastma.fineDecisionSeconds,
                            PenaltySeconds = _cfg.lastma.penaltySeconds,
                            FineResumeSeconds = _cfg.lastma.resumeStunSeconds,
                            FinesAllowed = Race.Lastma.FinesAllowed,
                            BailAllowed = Race.Lastma.BailAllowed,
                            CanAffordFine = _server.Ledger.GetBalance(e.PlayerId) >= (long)e.FloatValue,
                            FriendsOnline = _server.FriendsOnline(e.PlayerId, this),
                            Ranked = Mode == RaceMode.Ranked || Mode == RaceMode.Tournament
                        }
                    });
                }
                if (e.Type == RaceEventType.StateChanged) BroadcastRoomState();
            }
        }

        public void HandleIntent(string playerId, ClientEnvelope msg)
        {
            switch (msg.Kind)
            {
                case ClientMessageKind.Input:
                    Race.SubmitInput(playerId, msg.Input);
                    break;
                case ClientMessageKind.PayFine:
                    if (!Race.PayFine(playerId)) _server.SendError(playerId, "Cannot pay fine (not allowed here, nothing pending, or not enough Coins)");
                    break;
                case ClientMessageKind.TakePenalty:
                    if (!Race.TakePenalty(playerId)) _server.SendError(playerId, "No penalty to take");
                    break;
                case ClientMessageKind.RequestBail:
                    if (!Race.RequestBail(playerId)) _server.SendError(playerId, "No fine pending, or bail already requested");
                    break;
                case ClientMessageKind.PayBail:
                    if (!_server.CanBail(playerId, msg.TargetPlayerId, this)) { _server.SendError(playerId, "You can only bail friends or racers in your room"); break; }
                    if (!Race.PayBail(playerId, msg.TargetPlayerId)) _server.SendError(playerId, "Bail failed (no request, or not enough Coins)");
                    else _server.RecordBail(playerId, msg.TargetPlayerId);
                    break;
                case ClientMessageKind.VoteRematch:
                    Race.VoteRematch(playerId);
                    break;
                case ClientMessageKind.Ready:
                    SetReady(playerId, msg.Flag);
                    break;
                case ClientMessageKind.SelectLoadout:
                    SetLoadout(playerId, msg.VehicleId, msg.CharacterId);
                    break;
                case ClientMessageKind.SetRoomOptions:
                    if (playerId != HostPlayerId) { _server.SendError(playerId, "Only the host can change options"); break; }
                    Configure(msg.TrackId, msg.Laps, msg.ItemsMode, msg.LastmaMode, msg.FillWithAi);
                    break;
                case ClientMessageKind.StartRoom:
                    if (Mode == RaceMode.PrivateRoom || Mode == RaceMode.Practice)
                    {
                        if (msg.TrackId != null || msg.Laps > 0) Configure(msg.TrackId, msg.Laps, msg.ItemsEnabled, msg.LastmaEnabled);
                        if (!StartByHost(playerId, msg.Flag)) _server.SendError(playerId, "Only the host can start, and the room needs enough players");
                    }
                    break;
            }
        }

        public void OnPlayerDisconnected(string playerId)
        {
            if (Race.StateMachine.IsRacing || Race.State == RaceState.Countdown)
            {
                Race.MarkDisconnected(playerId);
                if (_cfg.raceRules.aiStandInWhileDisconnected && !_bots.ContainsKey(playerId) && !_standIns.ContainsKey(playerId))
                    _standIns[playerId] = new BotDriver(Race.Track, _seed ^ 0xA11EUL, 0.5f, _cfg.bots);
                BroadcastRoomState();   // members see "AI driving · holding their place"
            }
            else Leave(playerId);
        }

        public void OnPlayerReconnected(string playerId)
        {
            _standIns.Remove(playerId);
            BroadcastRoomState();
            Race.MarkReconnected(playerId);
            BroadcastRoomState();
            _server.SendTo(playerId, new ServerEnvelope { Kind = ServerMessageKind.RaceSnapshot, Snapshot = Race.BuildSnapshot(), Tick = Race.Tick });
        }

        public RoomStateDto ToDto()
        {
            var dto = new RoomStateDto
            {
                RoomCode = Code,
                RaceId = Race.Setup.RaceId,
                HostPlayerId = HostPlayerId,
                TrackId = TrackId,
                Mode = Mode,
                Laps = Laps,
                ItemsEnabled = ItemsEnabled,
                LastmaEnabled = LastmaEnabled,
                LastmaMode = LastmaMode,
                ItemsMode = ItemsMode,
                FillWithAi = FillWithAi,
                ShareUrl = (_cfg.social.shareLinkBase ?? "") + Code,
                StartsInSeconds = Race.State == RaceState.Lobby && Mode != RaceMode.PrivateRoom && Race.CanStart
                    ? (int)System.Math.Ceiling(System.Math.Max(0f, _cfg.raceRules.lobbyReadyTimeoutSeconds - Race.StateMachine.TimeInState)) : -1,
                AiSeats = _bots.Count,
                ReconnectWindowSeconds = _cfg.raceRules.reconnectWindowSeconds,
                ReconnectAttempts = _cfg.raceRules.reconnectAttempts,
                State = Race.State,
                Members = new RoomMemberDto[Race.ParticipantCount],
                Invited = new RoomMemberDto[_invited.Count]
            };
            int inv = 0;
            foreach (var kv in _invited)
                dto.Invited[inv++] = new RoomMemberDto { PlayerId = kv.Key, DisplayName = _server.DisplayNameOf(kv.Key), Status = kv.Value };
            for (int i = 0; i < Race.Participants.Count; i++)
            {
                var p = Race.Participants[i];
                var profile = _server.Profiles.Get(p.PlayerId);
                dto.Members[i] = new RoomMemberDto
                {
                    PlayerId = p.PlayerId,
                    DisplayName = p.DisplayName,
                    VehicleId = p.Vehicle.id,
                    CharacterId = p.Character?.id,
                    Ready = p.IsReady,
                    IsBot = p.IsBot,
                    Status = p.IsBot ? "AI" : p.Status == ParticipantStatus.Disconnected ? (_standIns.ContainsKey(p.PlayerId) ? "AiDriving" : "Disconnected")
                        : p.PlayerId == HostPlayerId ? "Host" : p.IsReady ? "Ready" : "Waiting",
                    Level = XpCurve.LevelForXp(profile.TotalXp, _cfg.progression),
                    RankId = _server.RankLadder.TierFor(profile.RankedPoints).id,
                    Title = profile.Title
                };
            }
            return dto;
        }

        private void SendResults()
        {
            SendToMembers(new ServerEnvelope { Kind = ServerMessageKind.RaceResults, Results = Race.Results, Rewards = _lastRewards, Tick = Race.Tick });
            _resultsResendTimer = _cfg.simulation.resultsResendSeconds;
        }

        private void BroadcastRoomState() => SendToMembers(new ServerEnvelope { Kind = ServerMessageKind.RoomState, Room = ToDto(), Tick = Race.Tick });

        private void SendToMembers(ServerEnvelope env)
        {
            foreach (var id in _members)
            {
                if (_bots.ContainsKey(id)) continue;
                _server.SendTo(id, env);
            }
        }
    }
}
