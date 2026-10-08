using System;
using System.Collections.Generic;
using NaijaKart.Core.AntiCheat;
using NaijaKart.Core.Chaos;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Input;
using NaijaKart.Core.Items;
using NaijaKart.Core.Lastma;
using NaijaKart.Core.Math;
using NaijaKart.Core.Race;
using NaijaKart.Core.Social;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Simulation
{
    /// <summary>
    /// The authoritative race (PRD §13, §14, §65). One instance per race, ticked at a fixed rate by the
    /// server. Clients feed it PlayerInputFrames and intents (use item, pay fine, bail, rematch); it
    /// owns everything else: membership, countdown, vehicles, checkpoints, laps, positions, items,
    /// hazards, LASTMA, finish order and results. Deterministic for a given seed and input stream.
    /// </summary>
    public sealed class RaceSimulation : IRaceContext
    {
        private readonly RaceSetup _setup;
        private readonly IConfigSource _content;
        private readonly GameConfig _cfg;
        private readonly TrackDefinition _trackDef;
        private readonly TrackGeometry _track;
        private readonly ArcadeVehicleModel _vehicleModel;
        private readonly HazardSystem _hazards;
        private readonly RoadEventScheduler _roadEvents;
        private readonly ItemEffectSystem _itemEffects;
        private readonly ItemRoller _itemRoller;
        private readonly LastmaSystem _lastma;
        private readonly DeterministicRandom _rng;
        private readonly ILogger _log;
        private readonly RaceStateMachine _sm = new RaceStateMachine();
        private readonly List<RaceParticipant> _participants = new List<RaceParticipant>();
        private readonly List<RaceParticipant> _activeSorted = new List<RaceParticipant>();
        private readonly Dictionary<string, RaceParticipant> _byId = new Dictionary<string, RaceParticipant>();
        private readonly Dictionary<string, ItemInventory> _inventories = new Dictionary<string, ItemInventory>();
        private readonly Dictionary<string, bool> _prevUseItem = new Dictionary<string, bool>();
        private readonly Dictionary<string, InputRateLimiter> _rateLimiters = new Dictionary<string, InputRateLimiter>();
        private readonly float[] _boxRespawn;
        private readonly List<RaceEvent> _events = new List<RaceEvent>();
        private readonly float _minLapSeconds;
        private readonly float _dt;
        private int _finishCount;
        private float _firstFinishTime = -1f;
        private int _lastCountdownValue = -1;
        private RaceResults _results;

        public RaceSimulation(RaceSetup setup, IConfigSource content, IWallet wallet, ILogger log = null)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _cfg = content.Game;
            _log = log ?? NullLogger.Instance;
            _trackDef = content.GetTrack(setup.TrackId) ?? throw new ArgumentException("Unknown track " + setup.TrackId);
            _track = new TrackGeometry(_trackDef);
            _vehicleModel = new ArcadeVehicleModel(_cfg);
            _hazards = new HazardSystem(_cfg.items);
            _rng = new DeterministicRandom(setup.Seed);
            _dt = 1f / _cfg.simulation.tickRate;
            _minLapSeconds = LapValidator.MinimumLapSeconds(_trackDef, _cfg.antiCheat);
            _roadEvents = new RoadEventScheduler(this);
            _itemEffects = new ItemEffectSystem(this, _roadEvents);
            _itemRoller = new ItemRoller(content.Items, setup.AllowedItemIds);
            _lastma = new LastmaSystem(this, wallet ?? new PracticeWallet());
            _boxRespawn = new float[_trackDef.itemBoxes.Length];
            _sm.Transitioned += (from, to) => Emit(RaceEventType.StateChanged, null, null, to.ToString(), (int)to);
        }

        // ---- IRaceContext ----
        public string RaceId => _setup.RaceId;
        public GameConfig Config => _cfg;
        public IConfigSource Content => _content;
        public TrackGeometry Track => _track;
        public ArcadeVehicleModel VehicleModel => _vehicleModel;
        public HazardSystem Hazards => _hazards;
        public DeterministicRandom Rng => _rng;
        public ILogger Log => _log;
        public float RaceTime { get; private set; }
        public int Tick { get; private set; }
        public IReadOnlyList<RaceParticipant> Participants => _participants;
        public IReadOnlyList<RaceParticipant> ActiveByPosition => _activeSorted;
        public RaceParticipant Leader => _activeSorted.Count > 0 ? _activeSorted[0] : null;
        public RaceParticipant Find(string playerId) => _byId.TryGetValue(playerId, out var p) ? p : null;

        public RaceParticipant NearestAhead(RaceParticipant from, float maxDistance)
        {
            float fromProgress = from.Checkpoints.Progress(from.State.Position);
            RaceParticipant best = null;
            float bestDist = maxDistance;
            foreach (var p in _activeSorted)
            {
                if (p == from) continue;
                if (p.Checkpoints.Progress(p.State.Position) <= fromProgress) continue;
                float d = Vec3.FlatDistance(p.State.Position, from.State.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        public void Emit(RaceEvent e) => _events.Add(e);

        public RaceEvent Emit(RaceEventType type, string playerId = null, string target = null, string payload = null, int i = 0, float f = 0f)
        {
            var e = new RaceEvent { Type = type, Tick = Tick, Time = RaceTime, PlayerId = playerId, TargetPlayerId = target, Payload = payload, IntValue = i, FloatValue = f };
            _events.Add(e);
            return e;
        }

        public void Eliminate(RaceParticipant p, string reason)
        {
            if (p.HasFinishedOrOut) return;
            p.Status = ParticipantStatus.Eliminated;
            p.State.IsImmobilised = true;
            Emit(RaceEventType.PlayerEliminated, p.PlayerId, null, reason);
            CheckFinishConditions();
        }

        // ---- Public API ----
        public RaceState State => _sm.Current;
        public RaceStateMachine StateMachine => _sm;
        public RaceSetup Setup => _setup;
        public float FixedDeltaTime => _dt;
        public LastmaSystem Lastma => _lastma;
        public RaceResults Results => _results;
        public int ParticipantCount => _participants.Count;
        public bool RematchRequested { get; private set; }

        public bool AddParticipant(string playerId, string displayName, string vehicleId, string characterId, bool isBot = false)
        {
            if (_sm.Current != RaceState.Waiting && _sm.Current != RaceState.Lobby && _sm.Current != RaceState.Matchmaking) return false;
            if (_byId.ContainsKey(playerId) || _participants.Count >= System.Math.Min(_setup.MaxPlayers, _cfg.simulation.maxPlayersPerRace)) return false;
            var vehicle = FindVehicle(vehicleId) ?? (_content.Vehicles.vehicles.Length > 0 ? _content.Vehicles.vehicles[0] : null);
            if (vehicle == null) return false;
            var character = FindCharacter(characterId);
            var p = new RaceParticipant
            {
                PlayerId = playerId,
                DisplayName = displayName ?? playerId,
                Vehicle = vehicle,
                Character = character,
                Stats = VehicleStats.From(vehicle, character, _cfg),
                GridSlot = _participants.Count,
                IsBot = isBot,
                Position = _participants.Count + 1
            };
            PlaceOnGrid(p);
            _participants.Add(p);
            _byId[playerId] = p;
            _inventories[playerId] = new ItemInventory();
            _prevUseItem[playerId] = false;
            _rateLimiters[playerId] = new InputRateLimiter(_cfg.antiCheat.maxInputFramesPerSecond);
            if (_sm.Current == RaceState.Waiting) _sm.Transition(RaceState.Lobby);
            Emit(RaceEventType.PlayerJoined, playerId, null, vehicle.id);
            return true;
        }

        public bool RemoveParticipant(string playerId)
        {
            var p = Find(playerId);
            if (p == null) return false;
            if (_sm.Current == RaceState.Lobby || _sm.Current == RaceState.Waiting || _sm.Current == RaceState.Matchmaking)
            {
                _participants.Remove(p);
                _byId.Remove(playerId);
                for (int i = 0; i < _participants.Count; i++)
                {
                    _participants[i].GridSlot = i;
                    PlaceOnGrid(_participants[i]);
                }
                Emit(RaceEventType.PlayerLeft, playerId);
                return true;
            }
            // Mid-race: treat as DNF; the car stays in the world until results.
            if (!p.HasFinishedOrOut)
            {
                p.Status = ParticipantStatus.DidNotFinish;
                p.State.IsImmobilised = true;
                Emit(RaceEventType.PlayerLeft, playerId);
                Emit(RaceEventType.PlayerDnf, playerId, null, "left");
                CheckFinishConditions();
            }
            return true;
        }

        public void SetReady(string playerId, bool ready)
        {
            var p = Find(playerId);
            if (p != null) p.IsReady = ready;
        }

        public bool AllReady
        {
            get
            {
                if (_participants.Count == 0) return false;
                foreach (var p in _participants) if (!p.IsReady && !p.IsBot) return false;
                return true;
            }
        }

        public bool CanStart => _participants.Count >= System.Math.Max(1, _setup.Mode == RaceMode.Practice ? 1 : _cfg.raceRules.minPlayersToStart);

        public void BeginLoading()
        {
            if (_sm.Current == RaceState.Lobby) _sm.Transition(RaceState.Loading);
        }

        /// <summary>Starts the countdown. Grid is final from here; no joins allowed.</summary>
        public void BeginCountdown()
        {
            if (_sm.Current == RaceState.Lobby) _sm.Transition(RaceState.Loading);
            if (_sm.Current != RaceState.Loading) return;
            foreach (var p in _participants)
            {
                p.Checkpoints = new CheckpointTracker(_trackDef, _track, _setup.Laps, _minLapSeconds, p.State.Position, p.State.Heading);
            }
            _sm.Transition(RaceState.Countdown);
            _lastCountdownValue = -1;
        }

        public void Cancel(string reason)
        {
            if (_sm.IsTerminal) return;
            _sm.TryTransition(RaceState.RaceCancelled);
            Emit(RaceEventType.RaceCancelled, null, null, reason);
            _results = BuildResults(cancelled: true);
        }

        public void SubmitInput(string playerId, PlayerInputFrame frame)
        {
            var p = Find(playerId);
            if (p == null || !p.IsActiveRacer) return;
            if (!_rateLimiters[playerId].Accept(frame.Sequence, RaceTime)) return;
            p.LatestInput = frame.Sanitised();
            p.LastInputSequence = frame.Sequence;
            if (p.Status == ParticipantStatus.Disconnected || p.Status == ParticipantStatus.Reconnecting)
            {
                MarkReconnected(playerId);
            }
        }

        public bool PayFine(string playerId) => _lastma.PayFine(playerId);
        public bool RequestBail(string playerId) => _lastma.RequestBail(playerId);
        public bool PayBail(string payerId, string targetPlayerId) => _lastma.PayBail(payerId, targetPlayerId);

        public void MarkDisconnected(string playerId)
        {
            var p = Find(playerId);
            if (p == null || p.Status != ParticipantStatus.Connected) return;
            p.Status = ParticipantStatus.Disconnected;
            p.DisconnectedAt = RaceTime;
            p.LatestInput = PlayerInputFrame.Neutral;
            Emit(RaceEventType.PlayerDisconnected, playerId);
        }

        public void MarkReconnected(string playerId)
        {
            var p = Find(playerId);
            if (p == null || (p.Status != ParticipantStatus.Disconnected && p.Status != ParticipantStatus.Reconnecting)) return;
            p.Status = ParticipantStatus.Connected;
            p.DisconnectedAt = -1f;
            Emit(RaceEventType.PlayerReconnected, playerId);
        }

        public void VoteRematch(string playerId)
        {
            var p = Find(playerId);
            if (p == null || _sm.Current != RaceState.Results) return;
            p.VotedRematch = true;
            Emit(RaceEventType.RematchVote, playerId);
            int connected = 0, voted = 0;
            foreach (var q in _participants)
            {
                if (q.IsBot) continue;
                if (q.Status == ParticipantStatus.Disconnected) continue;
                connected++;
                if (q.VotedRematch) voted++;
            }
            if (connected > 0 && voted == connected) RematchRequested = true;
        }

        public ItemInventory InventoryOf(string playerId) => _inventories.TryGetValue(playerId, out var inv) ? inv : null;

        public void DrainEvents(List<RaceEvent> into)
        {
            into.AddRange(_events);
            _events.Clear();
        }

        /// <summary>Advances the simulation by one fixed tick.</summary>
        public void Step()
        {
            float dt = _dt;
            _sm.Tick(dt);
            Tick++;
            switch (_sm.Current)
            {
                case RaceState.Lobby:
                    if (_sm.TimeInState >= _cfg.raceRules.lobbyReadyTimeoutSeconds && CanStart) BeginCountdown();
                    break;
                case RaceState.Countdown:
                    StepCountdown();
                    break;
                case RaceState.Racing:
                case RaceState.FinalLap:
                    StepRacing(dt);
                    break;
                case RaceState.Finish:
                    _sm.Transition(RaceState.Results);
                    _results = BuildResults(cancelled: false);
                    break;
                case RaceState.Timeout:
                    _sm.Transition(RaceState.Results);
                    _results = BuildResults(cancelled: false);
                    _results.TimedOut = true;
                    break;
            }
        }

        private void StepCountdown()
        {
            int value = (int)System.Math.Ceiling(_cfg.raceRules.countdownSeconds - _sm.TimeInState);
            if (value != _lastCountdownValue && value > 0)
            {
                _lastCountdownValue = value;
                Emit(RaceEventType.CountdownTick, null, null, null, value);
            }
            if (_sm.TimeInState >= _cfg.raceRules.countdownSeconds)
            {
                _sm.Transition(RaceState.Racing);
                RaceTime = 0f;
                foreach (var p in _participants) p.Checkpoints.StartLapTimer(0f);
                Emit(RaceEventType.RaceStarted);
                RecomputePositions(emitOvertakes: false);
            }
        }

        private void StepRacing(float dt)
        {
            RaceTime += dt;

            foreach (var p in _participants)
            {
                if (!p.IsActiveRacer) continue;
                StepVehicle(p, dt);
            }

            ResolveVehicleCollisions();

            foreach (var p in _participants)
            {
                if (!p.IsActiveRacer) continue;
                StepHazardContacts(p);
                StepItemBoxes(p);
                StepItemUse(p);
                StepCheckpoints(p);
            }

            foreach (var expired in _hazards.Tick(dt)) Emit(RaceEventType.HazardExpired, null, null, expired.Id);
            for (int i = 0; i < _boxRespawn.Length; i++) if (_boxRespawn[i] > 0f) _boxRespawn[i] -= dt;
            foreach (var inv in _inventories.Values) inv.Tick(dt);

            if (_setup.RoadEventsEnabled) _roadEvents.Tick(dt);
            if (_setup.LastmaEnabled) _lastma.Tick(dt);

            RecomputePositions(emitOvertakes: true);
            StepDisconnects();
            CheckFinishConditions();
        }

        private void StepVehicle(RaceParticipant p, float dt)
        {
            var input = p.Status == ParticipantStatus.Connected ? p.LatestInput : PlayerInputFrame.Neutral;
            var surface = _track.SampleSurface(p.State.Position, 1f, 1f);
            var ev = _vehicleModel.Step(ref p.State, input, p.Stats, surface, dt);

            if ((ev & VehicleStepEvents.DriftStarted) != 0) { p.Telemetry.Drifts++; Emit(RaceEventType.DriftStarted, p.PlayerId); }
            if ((ev & VehicleStepEvents.DriftLevelUp) != 0)
            {
                if (p.State.DriftLevel == DriftLevel.Purple) p.Telemetry.PurpleDrifts++;
                Emit(RaceEventType.DriftLevelUp, p.PlayerId, null, p.State.DriftLevel.ToString(), (int)p.State.DriftLevel);
            }
            if ((ev & VehicleStepEvents.BoostStarted) != 0) { p.Telemetry.Boosts++; Emit(RaceEventType.BoostStarted, p.PlayerId, null, "drift"); }
            if ((ev & VehicleStepEvents.WentOffroad) != 0) Emit(RaceEventType.WentOffroad, p.PlayerId);
            if (p.State.Speed > p.Telemetry.TopSpeed) p.Telemetry.TopSpeed = p.State.Speed;

            if (_vehicleModel.NeedsRecovery(p.State))
            {
                _vehicleModel.Recover(ref p.State, p.Checkpoints.LastGatePosition, p.Checkpoints.LastGateHeading, p.Stats);
                p.Telemetry.Recoveries++;
                Emit(RaceEventType.Recovered, p.PlayerId);
            }
        }

        private void ResolveVehicleCollisions()
        {
            float r = _cfg.collision.vehicleRadius;
            float minDist = r * 2f;
            for (int i = 0; i < _participants.Count; i++)
            {
                var a = _participants[i];
                if (!a.IsActiveRacer || a.State.IsImmobilised) continue;
                for (int j = i + 1; j < _participants.Count; j++)
                {
                    var b = _participants[j];
                    if (!b.IsActiveRacer || b.State.IsImmobilised) continue;
                    Vec3 delta = (b.State.Position - a.State.Position).Flat;
                    float dist = delta.FlatMagnitude;
                    if (dist >= minDist || dist < 1e-4f) continue;

                    Vec3 n = delta / dist;
                    float overlap = minDist - dist;
                    float wa = a.Stats.Weight01 + 0.5f, wb = b.Stats.Weight01 + 0.5f;
                    float shareA = wb / (wa + wb), shareB = wa / (wa + wb);
                    a.State.Position = a.State.Position - n * (overlap * shareA);
                    b.State.Position = b.State.Position + n * (overlap * shareB);

                    float closing = Vec3.Dot(a.State.Velocity - b.State.Velocity, n);
                    if (closing <= 0f) continue;
                    // Lighter kart loses more speed and gets pushed sideways.
                    a.State.Speed *= 1f - _cfg.collision.bumpSpeedLoss * shareA;
                    b.State.Speed *= 1f - _cfg.collision.bumpSpeedLoss * shareB;
                    float lateral = _cfg.collision.bumpLateralImpulse;
                    a.State.LateralVelocity -= Vec3.Dot(n, a.State.Right) * lateral * shareA;
                    b.State.LateralVelocity += Vec3.Dot(n, b.State.Right) * lateral * shareB;
                    if (closing >= _cfg.collision.hitEventMinClosingSpeed)
                    {
                        a.Telemetry.Collisions++;
                        b.Telemetry.Collisions++;
                        var e = Emit(RaceEventType.Collision, a.PlayerId, b.PlayerId, null, 0, closing);
                        e.Position = a.State.Position;
                    }
                }
            }
        }

        private void StepHazardContacts(RaceParticipant p)
        {
            var h = _hazards.FirstContact(p.State.Position, _cfg.collision.vehicleRadius, p.PlayerId);
            if (h != null) _itemEffects.ApplyHazardContact(p, h);
        }

        private void StepItemBoxes(RaceParticipant p)
        {
            if (!_setup.ItemsEnabled || _itemRoller.Count == 0) return;
            var inv = _inventories[p.PlayerId];
            if (inv.HasItem) return;
            var boxes = _trackDef.itemBoxes;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (_boxRespawn[i] > 0f) continue;
                if (Vec3.FlatDistance(p.State.Position, boxes[i].position) > _cfg.items.boxPickupRadius) continue;
                var item = _itemRoller.Roll(p.Position, _activeSorted.Count, _rng, inv, RaceTime);
                if (item == null) return;
                inv.Grant(item.id, _cfg.items.rollDurationSeconds, item.cooldown, RaceTime);
                _boxRespawn[i] = _cfg.items.boxRespawnSeconds;
                Emit(RaceEventType.ItemBoxTaken, p.PlayerId, null, boxes[i].id);
                Emit(RaceEventType.ItemGranted, p.PlayerId, null, item.id);
                return;
            }
        }

        private void StepItemUse(RaceParticipant p)
        {
            bool pressed = p.Status == ParticipantStatus.Connected && p.LatestInput.UseItem;
            bool rising = pressed && !_prevUseItem[p.PlayerId];
            _prevUseItem[p.PlayerId] = pressed;
            if (!rising || p.State.IsImmobilised) return;
            var inv = _inventories[p.PlayerId];
            if (!inv.IsReady) return;
            var def = _itemRoller.Find(inv.HeldItemId);
            inv.Consume();
            _itemEffects.Use(p, def);
        }

        private void StepCheckpoints(RaceParticipant p)
        {
            var result = p.Checkpoints.Update(p.State.Position, RaceTime, out bool viaShortcut);
            if (viaShortcut)
            {
                p.Telemetry.ShortcutsTaken++;
                _lastma.NotifyShortcut(p.PlayerId);
            }
            switch (result)
            {
                case CheckpointResult.GatePassed:
                    Emit(RaceEventType.GatePassed, p.PlayerId, null, viaShortcut ? "shortcut" : null, p.Checkpoints.NextCheckpoint);
                    break;
                case CheckpointResult.LapCompleted:
                    Emit(RaceEventType.LapCompleted, p.PlayerId, null, null, p.Checkpoints.LapsCompleted, p.Checkpoints.LastLapSeconds);
                    if (p.Checkpoints.IsOnFinalLap && _sm.Current == RaceState.Racing)
                    {
                        _sm.Transition(RaceState.FinalLap);
                        foreach (var q in _participants) q.Telemetry.PositionAtFinalLapStart = q.Position;
                        Emit(RaceEventType.FinalLapStarted, p.PlayerId);
                    }
                    break;
                case CheckpointResult.LapRejected:
                    Emit(RaceEventType.LapRejected, p.PlayerId);
                    _log.Warn("anticheat", $"Rejected impossible lap for {p.PlayerId} in race {RaceId}");
                    break;
                case CheckpointResult.RaceFinished:
                    p.Status = ParticipantStatus.Finished;
                    p.FinishPosition = ++_finishCount;
                    p.FinishTime = RaceTime;
                    if (_firstFinishTime < 0f) _firstFinishTime = RaceTime;
                    Emit(RaceEventType.PlayerFinished, p.PlayerId, null, null, p.FinishPosition, RaceTime);
                    break;
            }
        }

        private void RecomputePositions(bool emitOvertakes)
        {
            _activeSorted.Clear();
            foreach (var p in _participants) if (p.IsActiveRacer) _activeSorted.Add(p);
            _activeSorted.Sort((a, b) =>
            {
                int c = b.Checkpoints.Progress(b.State.Position).CompareTo(a.Checkpoints.Progress(a.State.Position));
                return c != 0 ? c : a.GridSlot.CompareTo(b.GridSlot);
            });
            int finished = _finishCount;
            for (int i = 0; i < _activeSorted.Count; i++)
            {
                var p = _activeSorted[i];
                int newPos = finished + i + 1;
                if (emitOvertakes && p.Position != 0 && newPos < p.Position)
                {
                    p.Telemetry.Overtakes += p.Position - newPos;
                    Emit(RaceEventType.Overtake, p.PlayerId, null, null, newPos);
                }
                else if (emitOvertakes && p.Position != 0 && newPos > p.Position)
                {
                    p.Telemetry.TimesOvertaken += newPos - p.Position;
                }
                p.Position = newPos;
                if (newPos > p.Telemetry.WorstPosition) p.Telemetry.WorstPosition = newPos;
            }
            foreach (var p in _participants)
            {
                if (p.Status == ParticipantStatus.Finished) p.Position = p.FinishPosition;
            }
        }

        private void StepDisconnects()
        {
            foreach (var p in _participants)
            {
                if (p.Status != ParticipantStatus.Disconnected) continue;
                if (RaceTime - p.DisconnectedAt >= _cfg.raceRules.reconnectWindowSeconds)
                {
                    p.Status = ParticipantStatus.DidNotFinish;
                    p.State.IsImmobilised = true;
                    Emit(RaceEventType.PlayerDnf, p.PlayerId, null, "disconnect_timeout");
                }
            }
        }

        private void CheckFinishConditions()
        {
            if (!_sm.IsRacing) return;
            bool anyActive = false;
            foreach (var p in _participants) if (p.IsActiveRacer) { anyActive = true; break; }

            if (!anyActive)
            {
                _sm.Transition(RaceState.Finish);
                return;
            }
            if (_firstFinishTime >= 0f && RaceTime - _firstFinishTime >= _cfg.raceRules.finishGraceSeconds)
            {
                foreach (var p in _participants)
                {
                    if (!p.IsActiveRacer) continue;
                    p.Status = ParticipantStatus.DidNotFinish;
                    Emit(RaceEventType.PlayerDnf, p.PlayerId, null, "finish_grace");
                }
                _sm.Transition(RaceState.Finish);
                return;
            }
            if (RaceTime >= _cfg.raceRules.maxRaceDurationSeconds)
            {
                foreach (var p in _participants)
                {
                    if (!p.IsActiveRacer) continue;
                    p.Status = ParticipantStatus.DidNotFinish;
                    Emit(RaceEventType.PlayerDnf, p.PlayerId, null, "timeout");
                }
                Emit(RaceEventType.RaceTimeout);
                _sm.Transition(RaceState.Timeout);
            }
        }

        private RaceResults BuildResults(bool cancelled)
        {
            var results = new RaceResults
            {
                RaceId = RaceId,
                TrackId = _trackDef.id,
                Mode = _setup.Mode,
                Laps = _setup.Laps,
                RaceDuration = RaceTime,
                Cancelled = cancelled
            };
            var ordered = new List<RaceParticipant>(_participants);
            ordered.Sort((a, b) =>
            {
                bool fa = a.Status == ParticipantStatus.Finished, fb = b.Status == ParticipantStatus.Finished;
                if (fa && fb) return a.FinishPosition.CompareTo(b.FinishPosition);
                if (fa != fb) return fa ? -1 : 1;
                float pa = a.Checkpoints != null ? a.Checkpoints.Progress(a.State.Position) : 0f;
                float pb = b.Checkpoints != null ? b.Checkpoints.Progress(b.State.Position) : 0f;
                int c = pb.CompareTo(pa);
                return c != 0 ? c : a.GridSlot.CompareTo(b.GridSlot);
            });
            for (int i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                bool finished = p.Status == ParticipantStatus.Finished;
                int pos = i + 1;
                results.Entries.Add(new RaceResultEntry
                {
                    PlayerId = p.PlayerId,
                    DisplayName = p.DisplayName,
                    VehicleId = p.Vehicle.id,
                    CharacterId = p.Character?.id,
                    FinishPosition = pos,
                    Finished = finished,
                    Status = p.Status,
                    TotalTime = finished ? p.FinishTime : RaceTime,
                    BestLap = p.Checkpoints != null && p.Checkpoints.BestLapSeconds < float.MaxValue ? p.Checkpoints.BestLapSeconds : 0f,
                    LapsCompleted = p.Checkpoints?.LapsCompleted ?? 0,
                    Stats = p.Telemetry,
                    Highlights = RaceHighlights.Build(p.Telemetry, pos, p.Telemetry.PositionAtFinalLapStart),
                    IsBot = p.IsBot
                });
            }
            return results;
        }

        public RaceSnapshot BuildSnapshot()
        {
            var snap = new RaceSnapshot
            {
                RaceId = RaceId,
                Tick = Tick,
                Time = RaceTime,
                State = _sm.Current,
                StateTime = _sm.TimeInState,
                CountdownValue = _sm.Current == RaceState.Countdown ? _lastCountdownValue : 0,
                Participants = new ParticipantSnapshot[_participants.Count],
                Hazards = new HazardSnapshot[_hazards.All.Count],
                ItemBoxes = new ItemBoxSnapshot[_trackDef.itemBoxes.Length]
            };
            for (int i = 0; i < _participants.Count; i++)
            {
                var p = _participants[i];
                var inv = _inventories[p.PlayerId];
                var le = _lastma.EventFor(p.PlayerId);
                snap.Participants[i] = new ParticipantSnapshot
                {
                    PlayerId = p.PlayerId,
                    Position = p.State.Position,
                    Heading = p.State.Heading,
                    Speed = p.State.Speed,
                    LateralVelocity = p.State.LateralVelocity,
                    Lap = p.Checkpoints?.LapsCompleted ?? 0,
                    NextCheckpoint = p.Checkpoints?.NextCheckpoint ?? 0,
                    RacePosition = p.Position,
                    Status = p.Status,
                    IsDrifting = p.State.IsDrifting,
                    DriftLevel = p.State.DriftLevel,
                    DriftCharge = p.State.DriftCharge,
                    IsBoosting = p.State.IsBoosting,
                    IsStunned = p.State.IsStunned,
                    BlindTimeRemaining = p.State.BlindTimeRemaining,
                    ShieldTimeRemaining = p.State.ShieldTimeRemaining,
                    WobbleTimeRemaining = p.State.WobbleTimeRemaining,
                    IsImmobilised = p.State.IsImmobilised,
                    HeldItemId = inv.HeldItemId,
                    ItemReady = inv.IsReady,
                    LastmaPhase = le?.Phase ?? LastmaPhase.None,
                    LastmaPressure = le?.Pressure ?? 0f,
                    LastmaTimeRemaining = le?.PhaseTimeRemaining ?? 0f,
                    LastmaBailRequested = le?.BailRequested ?? false,
                    LastInputSequence = p.LastInputSequence,
                    FullState = p.State
                };
            }
            for (int i = 0; i < _hazards.All.Count; i++)
            {
                var h = _hazards.All[i];
                snap.Hazards[i] = new HazardSnapshot
                {
                    Id = h.Id, Kind = h.Kind, Position = h.Position, Velocity = h.Velocity, Radius = h.Radius,
                    ZoneHalfLength = h.ZoneHalfLength, ZoneDirection = h.ZoneDirection, Armed = h.IsArmed, OwnerPlayerId = h.OwnerPlayerId
                };
            }
            for (int i = 0; i < _boxRespawn.Length; i++)
            {
                snap.ItemBoxes[i] = new ItemBoxSnapshot { Id = _trackDef.itemBoxes[i].id, Available = _boxRespawn[i] <= 0f };
            }
            return snap;
        }

        private void PlaceOnGrid(RaceParticipant p)
        {
            _track.GridSlot(p.GridSlot, _cfg.raceRules.gridRowSpacing, _cfg.raceRules.gridColumnSpacing, out Vec3 pos, out float heading);
            p.State = VehicleState.AtRest(pos, heading);
        }

        private VehicleDefinition FindVehicle(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var v in _content.Vehicles.vehicles) if (v.id == id) return v;
            return null;
        }

        private CharacterDefinition FindCharacter(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in _content.Characters.characters) if (c.id == id) return c;
            return null;
        }
    }
}
