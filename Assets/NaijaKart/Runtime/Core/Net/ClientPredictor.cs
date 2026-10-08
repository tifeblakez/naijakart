using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Net
{
    /// <summary>
    /// Client-side prediction + server reconciliation for the local kart (PRD §66). Runs the same
    /// ArcadeVehicleModel the server runs; keeps a history of unacknowledged inputs and replays them on
    /// top of each authoritative state. Item/hazard effects are not predicted (they are rare and the
    /// server's version wins on the next snapshot), which keeps prediction simple and cheat-proof.
    /// </summary>
    public sealed class ClientPredictor
    {
        private struct HistoryEntry
        {
            public PlayerInputFrame Input;
            public VehicleState ResultState;
        }

        private readonly ArcadeVehicleModel _model;
        private readonly TrackGeometry _track;
        private readonly VehicleStats _stats;
        private readonly float _dt;
        private readonly List<HistoryEntry> _history = new List<HistoryEntry>();
        private readonly int _maxHistory;
        private int _nextSequence = 1;

        public VehicleState Predicted;
        /// <summary>Distance between the predicted and corrected state at the last reconciliation (for smoothing/HUD debug).</summary>
        public float LastCorrectionMagnitude { get; private set; }
        public int PendingInputs => _history.Count;

        public ClientPredictor(GameConfig cfg, TrackGeometry track, VehicleStats stats, VehicleState initial)
        {
            _model = new ArcadeVehicleModel(cfg);
            _track = track;
            _stats = stats;
            _dt = 1f / cfg.simulation.tickRate;
            _maxHistory = cfg.simulation.tickRate * 2;
            Predicted = initial;
        }

        /// <summary>Stamps and applies a new local input; returns the frame to send to the server.</summary>
        public PlayerInputFrame Apply(PlayerInputFrame input)
        {
            input.Sequence = _nextSequence++;
            Step(ref Predicted, input);
            _history.Add(new HistoryEntry { Input = input, ResultState = Predicted });
            if (_history.Count > _maxHistory) _history.RemoveAt(0);
            return input;
        }

        /// <summary>Applies an authoritative snapshot for the local player and replays unacknowledged inputs.</summary>
        public void Reconcile(in ParticipantSnapshot authoritative)
        {
            int ack = authoritative.LastInputSequence;
            int drop = 0;
            while (drop < _history.Count && _history[drop].Input.Sequence <= ack) drop++;
            if (drop > 0) _history.RemoveRange(0, drop);

            VehicleState before = Predicted;
            VehicleState state = authoritative.FullState;
            for (int i = 0; i < _history.Count; i++)
            {
                Step(ref state, _history[i].Input);
                var h = _history[i];
                h.ResultState = state;
                _history[i] = h;
            }
            LastCorrectionMagnitude = Math.Vec3.FlatDistance(before.Position, state.Position);
            Predicted = state;
        }

        private void Step(ref VehicleState state, in PlayerInputFrame input)
        {
            var surface = _track.SampleSurface(state.Position, 1f, 1f);
            _model.Step(ref state, input, _stats, surface, _dt);
        }
    }
}
