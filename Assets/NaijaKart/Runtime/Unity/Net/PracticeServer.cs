using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;

namespace NaijaKart.Unity.Net
{
    /// <summary>
    /// Minimal in-process host for practice mode: one room, bots, a subset of the real message
    /// contract (Hello, CreateRoom/StartRoom, Input, PayFine, RequestBail, VoteRematch). Not a
    /// replacement for NaijaKart.Server: no economy persistence, matchmaking or social features.
    /// </summary>
    public sealed class PracticeServer
    {
        private readonly IServerTransport _transport;
        private readonly IConfigSource _content;
        private readonly GameConfig _cfg;
        private readonly Dictionary<string, BotDriver> _bots = new Dictionary<string, BotDriver>();
        private readonly List<RaceEvent> _events = new List<RaceEvent>();
        private readonly string _trackId;
        private readonly int _laps, _botCount;
        private readonly bool _items, _lastma, _road;
        private RaceSimulation _race;
        private string _humanId;
        private string _selectedVehicle;
        private string _selectedCharacter;
        private float _snapshotAccumulator;
        private int _raceCounter;

        public RaceSimulation Race => _race;

        public PracticeServer(IConfigSource content, IServerTransport transport, string trackId, int laps, int bots, bool items, bool lastma, bool road)
        {
            _content = content;
            _cfg = content.Game;
            _transport = transport;
            _trackId = content.GetTrack(trackId) != null ? trackId : content.TrackIds[0];
            _laps = laps;
            _botCount = bots;
            _items = items;
            _lastma = lastma;
            _road = road;
            _transport.MessageReceived += OnMessage;
        }

        private void CreateRace()
        {
            _raceCounter++;
            _race = new RaceSimulation(new RaceSetup
            {
                RaceId = "practice-" + _raceCounter,
                TrackId = _trackId,
                Mode = RaceMode.Practice,
                Laps = _laps,
                Seed = (ulong)System.Environment.TickCount + (ulong)_raceCounter,
                ItemsEnabled = _items,
                LastmaEnabled = _lastma,
                RoadEventsEnabled = _road
            }, _content, new PracticeWallet());
            _bots.Clear();
            var vehicles = _content.Vehicles.vehicles;
            _race.AddParticipant(_humanId, PlayerSettingsStore.DisplayName ?? "You", _selectedVehicle, _selectedCharacter);
            for (int i = 0; i < _botCount; i++)
            {
                string id = "bot_" + (i + 1);
                _race.AddParticipant(id, "Bot " + (i + 1), vehicles[i % vehicles.Length].id, null, isBot: true);
                _bots[id] = new BotDriver(_race.Track, (ulong)(i + 1) * 97, 0.4f + 0.08f * i);
            }
            _race.BeginCountdown();
        }

        private void OnMessage(string connectionId, ClientEnvelope msg)
        {
            switch (msg.Kind)
            {
                case ClientMessageKind.Hello:
                    _humanId = msg.PlayerId;
                    _transport.Send(connectionId, new ServerEnvelope { Kind = ServerMessageKind.Welcome, PlayerId = _humanId, Text = "newbie" });
                    break;
                case ClientMessageKind.CreateRoom:
                case ClientMessageKind.StartRoom:
                    if (msg.VehicleId != null) _selectedVehicle = msg.VehicleId;
                    if (msg.CharacterId != null) _selectedCharacter = msg.CharacterId;
                    if (_humanId != null && (_race == null || _race.State == RaceState.Results)) CreateRace();
                    break;
                case ClientMessageKind.Input:
                    _race?.SubmitInput(_humanId, msg.Input);
                    break;
                case ClientMessageKind.PayFine:
                    _race?.PayFine(_humanId);
                    break;
                case ClientMessageKind.RequestBail:
                    _race?.RequestBail(_humanId);
                    break;
                case ClientMessageKind.VoteRematch:
                    if (_race != null && _race.State == RaceState.Results) CreateRace();
                    break;
            }
        }

        public void Tick()
        {
            _transport.PumpIncoming();
            if (_race == null) return;
            float dt = _race.FixedDeltaTime;
            if (_race.StateMachine.IsRacing)
            {
                foreach (var kv in _bots)
                {
                    var p = _race.Find(kv.Key);
                    if (p == null || !p.IsActiveRacer) continue;
                    _race.SubmitInput(kv.Key, kv.Value.Think(p, _race.Hazards.All, dt));
                    var le = _race.Lastma.EventFor(kv.Key);
                    if (le != null && le.Phase == Core.Lastma.LastmaPhase.FinePending) _race.PayFine(kv.Key);
                }
            }
            _race.Step();
            _events.Clear();
            _race.DrainEvents(_events);
            bool reachedResults = false;
            foreach (var e in _events)
            {
                _transport.Broadcast(new ServerEnvelope { Kind = ServerMessageKind.RaceEvent, Event = e, Tick = _race.Tick });
                if (e.Type == RaceEventType.StateChanged && e.IntValue == (int)RaceState.Results) reachedResults = true;
            }
            if (reachedResults)
            {
                _transport.Broadcast(new ServerEnvelope { Kind = ServerMessageKind.RaceResults, Results = _race.Results, Rewards = new SettledRewardDto[0], Tick = _race.Tick });
            }
            _snapshotAccumulator += dt;
            if (_snapshotAccumulator >= 1f / _cfg.simulation.snapshotRate)
            {
                _snapshotAccumulator = 0f;
                _transport.Broadcast(new ServerEnvelope { Kind = ServerMessageKind.RaceSnapshot, Snapshot = _race.BuildSnapshot(), Tick = _race.Tick });
            }
        }
    }
}
