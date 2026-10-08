using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;
using NaijaKart.Core.Vehicle;
using NaijaKart.Unity.Input;
using NaijaKart.Unity.Net;
using NaijaKart.Unity.View;
using UnityEngine;

namespace NaijaKart.Unity.Race
{
    /// <summary>
    /// Client-side race loop (PRD §65 "client controls: local input, presentation"). Each fixed tick it
    /// sends the local PlayerInputFrame, predicts the local kart with the shared ArcadeVehicleModel,
    /// reconciles against authoritative snapshots, interpolates remote karts between snapshots and
    /// keeps hazard/item-box views in sync. It never decides laps, positions, items or results.
    /// </summary>
    public sealed class RaceClientController : MonoBehaviour
    {
        [SerializeField] private TiltInputProvider _input;
        [SerializeField] private VehicleView _vehicleViewPrefab;
        [SerializeField] private HazardView _hazardViewPrefab;
        [SerializeField] private ItemBoxView _itemBoxViewPrefab;
        [SerializeField] private TrackGreyboxBuilder _trackBuilder;
        [SerializeField] private Transform _worldRoot;
        [Tooltip("Seconds of interpolation delay for remote karts (about 1.5 snapshot intervals).")]
        [SerializeField] private float _interpolationDelay = 0.1f;
        [Tooltip("Max metres the local view is allowed to snap per second when correcting prediction error.")]
        [SerializeField] private float _correctionSmoothing = 12f;

        private readonly Dictionary<string, VehicleView> _vehicles = new Dictionary<string, VehicleView>();
        private readonly Dictionary<string, RemoteKartBuffer> _remoteBuffers = new Dictionary<string, RemoteKartBuffer>();
        private readonly Dictionary<string, HazardView> _hazards = new Dictionary<string, HazardView>();
        private readonly List<ItemBoxView> _itemBoxes = new List<ItemBoxView>();
        private NetworkClient _net;
        private GameConfig _cfg;
        private TrackGeometry _track;
        private TrackDefinition _trackDef;
        private ClientPredictor _predictor;
        private VehicleStats _localStats;
        private float _tickAccumulator;
        private float _dt;
        private Vector3 _visualOffset;

        public RaceSnapshot LatestSnapshot { get; private set; }
        public ParticipantSnapshot LocalSnapshot { get; private set; }
        public bool HasLocal { get; private set; }
        public VehicleView LocalView => _vehicles.TryGetValue(_net.PlayerId, out var v) ? v : null;
        public VehicleState PredictedLocalState => _predictor != null ? _predictor.Predicted : default;
        public TrackGeometry Track => _track;
        public string LocalPlayerId => _net.PlayerId;

        private static readonly Color[] Palette =
        {
            new Color(0.3f, 0.85f, 0.4f), new Color(1f, 0.55f, 0.1f), new Color(0.3f, 0.7f, 1f), new Color(0.1f, 0.1f, 0.2f),
            new Color(1f, 0.4f, 0.7f), new Color(0.8f, 0.3f, 1f), new Color(1f, 0.9f, 0.2f), new Color(0.2f, 0.9f, 0.9f)
        };

        public string DisplayNameOf(string playerId)
        {
            var room = _net.Room;
            if (room != null) foreach (var m in room.Members) if (m.PlayerId == playerId) return m.DisplayName;
            return playerId;
        }

        /// <summary>Stable per-racer colour (standings dots, minimap).</summary>
        public Color ColorOf(string playerId)
        {
            var room = _net.Room;
            if (room != null) for (int i = 0; i < room.Members.Length; i++) if (room.Members[i].PlayerId == playerId) return Palette[i % Palette.Length];
            return Color.white;
        }

        /// <summary>Label of the shortcut gate the local racer just passed (checkpoint index from the GatePassed event).</summary>
        public string ShortcutLabel(int nextCheckpoint)
        {
            if (_trackDef == null) return null;
            int passed = (nextCheckpoint - 1 + _trackDef.checkpoints.Length) % _trackDef.checkpoints.Length;
            foreach (var g in _trackDef.checkpoints[passed].gates) if (g.isShortcut) return (g.label ?? "shortcut").Replace('_', ' ');
            return null;
        }

        private void Start()
        {
            _net = NetworkClient.Instance;
            _cfg = GameBootstrap.Content.Game;
            _dt = 1f / _cfg.simulation.tickRate;
            _net.SnapshotReceived += OnSnapshot;
            _net.RoomChanged += OnRoom;
            if (_net.Room != null) OnRoom(_net.Room);
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.SnapshotReceived -= OnSnapshot;
            _net.RoomChanged -= OnRoom;
        }

        private void OnRoom(RoomStateDto room)
        {
            if (_trackDef != null && _trackDef.id == room.TrackId) return;
            _trackDef = GameBootstrap.Content.GetTrack(room.TrackId);
            if (_trackDef == null) { Debug.LogError("[NK:race] unknown track " + room.TrackId); return; }
            _track = new TrackGeometry(_trackDef);
            if (_trackBuilder != null) _trackBuilder.Build(_trackDef);
            foreach (var b in _itemBoxes) if (b != null) Destroy(b.gameObject);
            _itemBoxes.Clear();
            if (_itemBoxViewPrefab != null)
            {
                foreach (var box in _trackDef.itemBoxes)
                {
                    var v = Instantiate(_itemBoxViewPrefab, box.position.ToUnity(), Quaternion.identity, _worldRoot);
                    v.Id = box.id;
                    _itemBoxes.Add(v);
                }
            }
        }

        private void Update()
        {
            if (LatestSnapshot == null || _track == null) return;
            bool racing = LatestSnapshot.State == RaceState.Racing || LatestSnapshot.State == RaceState.FinalLap;

            // Fixed-rate input + prediction, independent of render frame rate.
            _tickAccumulator += Time.deltaTime;
            int steps = 0;
            while (_tickAccumulator >= _dt && steps++ < 6)
            {
                _tickAccumulator -= _dt;
                if (racing && HasLocal && _predictor != null && !LocalSnapshot.IsImmobilised && LocalSnapshot.Status == ParticipantStatus.Connected)
                {
                    var frame = _predictor.Apply(_input.Consume());
                    _net.SendInput(frame);
                }
            }

            float renderTime = Time.time - _interpolationDelay;
            foreach (var kv in _vehicles)
            {
                if (kv.Key == _net.PlayerId && _predictor != null && racing)
                {
                    // Local kart: predicted state plus a decaying visual offset so corrections never pop.
                    _visualOffset = Vector3.MoveTowards(_visualOffset, Vector3.zero, _correctionSmoothing * Time.deltaTime);
                    var s = _predictor.Predicted;
                    kv.Value.Apply(s.Position.ToUnity() + _visualOffset, s.Heading, s, Time.deltaTime);
                }
                else if (_remoteBuffers.TryGetValue(kv.Key, out var buf))
                {
                    buf.Sample(renderTime, out Vector3 pos, out float heading, out ParticipantSnapshot snap);
                    kv.Value.Apply(pos, heading, snap.FullState, Time.deltaTime);
                }
            }
        }

        private void OnSnapshot(RaceSnapshot snap)
        {
            LatestSnapshot = snap;
            if (_track == null) return;
            EnsureViews(snap);
            foreach (var p in snap.Participants)
            {
                if (p.PlayerId == _net.PlayerId)
                {
                    HasLocal = true;
                    LocalSnapshot = p;
                    if (_predictor == null)
                    {
                        var room = _net.Room;
                        var def = FindVehicle(room, p.PlayerId);
                        _localStats = VehicleStats.From(def, FindCharacter(room, p.PlayerId), _cfg);
                        _predictor = new ClientPredictor(_cfg, _track, _localStats, p.FullState);
                    }
                    else
                    {
                        Vector3 before = _predictor.Predicted.Position.ToUnity();
                        _predictor.Reconcile(p);
                        Vector3 after = _predictor.Predicted.Position.ToUnity();
                        if (_predictor.LastCorrectionMagnitude < 6f) _visualOffset += before - after; // smooth small corrections, snap big ones (recovery)
                        else _visualOffset = Vector3.zero;
                    }
                }
                else
                {
                    if (!_remoteBuffers.TryGetValue(p.PlayerId, out var buf))
                    {
                        buf = new RemoteKartBuffer();
                        _remoteBuffers[p.PlayerId] = buf;
                    }
                    buf.Push(Time.time, p);
                }
            }
            SyncHazards(snap);
            for (int i = 0; i < snap.ItemBoxes.Length && i < _itemBoxes.Count; i++) _itemBoxes[i].SetAvailable(snap.ItemBoxes[i].Available);
        }

        private void EnsureViews(RaceSnapshot snap)
        {
            if (_vehicleViewPrefab == null) return;
            foreach (var p in snap.Participants)
            {
                if (_vehicles.ContainsKey(p.PlayerId)) continue;
                var view = Instantiate(_vehicleViewPrefab, p.Position.ToUnity(), CoreConversions.HeadingToRotation(p.Heading), _worldRoot);
                view.Bind(p.PlayerId, FindVehicle(_net.Room, p.PlayerId), p.PlayerId == _net.PlayerId);
                _vehicles[p.PlayerId] = view;
            }
        }

        private void SyncHazards(RaceSnapshot snap)
        {
            if (_hazardViewPrefab == null) return;
            var seen = new HashSet<string>();
            foreach (var h in snap.Hazards)
            {
                seen.Add(h.Id);
                if (!_hazards.TryGetValue(h.Id, out var view))
                {
                    view = Instantiate(_hazardViewPrefab, h.Position.ToUnity(), Quaternion.identity, _worldRoot);
                    view.Bind(h);
                    _hazards[h.Id] = view;
                }
                view.UpdateFrom(h);
            }
            var gone = new List<string>();
            foreach (var kv in _hazards) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                _hazards[id].Expire();
                _hazards.Remove(id);
            }
        }

        private static VehicleDefinition FindVehicle(RoomStateDto room, string playerId)
        {
            string id = null;
            if (room != null) foreach (var m in room.Members) if (m.PlayerId == playerId) id = m.VehicleId;
            foreach (var v in GameBootstrap.Content.Vehicles.vehicles) if (v.id == id) return v;
            return GameBootstrap.Content.Vehicles.vehicles[0];
        }

        private static CharacterDefinition FindCharacter(RoomStateDto room, string playerId)
        {
            string id = null;
            if (room != null) foreach (var m in room.Members) if (m.PlayerId == playerId) id = m.CharacterId;
            foreach (var c in GameBootstrap.Content.Characters.characters) if (c.id == id) return c;
            return null;
        }
    }
}
