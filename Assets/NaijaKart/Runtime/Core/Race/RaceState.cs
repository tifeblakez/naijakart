using System;
using System.Collections.Generic;

namespace NaijaKart.Core.Race
{
    /// <summary>Explicit race states (PRD §13). Transitions are validated by RaceStateMachine.</summary>
    public enum RaceState
    {
        Waiting,
        Lobby,
        Matchmaking,
        Loading,
        Countdown,
        Racing,
        FinalLap,
        Finish,
        Results,
        // Exceptional
        RaceCancelled,
        ServerError,
        Timeout
    }

    public enum ParticipantStatus
    {
        Connected,
        Disconnected,
        Reconnecting,
        Finished,
        DidNotFinish,
        Eliminated,
        Spectating
    }

    public sealed class RaceStateMachine
    {
        private static readonly Dictionary<RaceState, RaceState[]> Allowed = new Dictionary<RaceState, RaceState[]>
        {
            { RaceState.Waiting, new[] { RaceState.Lobby, RaceState.Matchmaking, RaceState.RaceCancelled } },
            { RaceState.Matchmaking, new[] { RaceState.Lobby, RaceState.RaceCancelled, RaceState.Timeout } },
            { RaceState.Lobby, new[] { RaceState.Loading, RaceState.RaceCancelled, RaceState.Timeout } },
            { RaceState.Loading, new[] { RaceState.Countdown, RaceState.RaceCancelled, RaceState.ServerError, RaceState.Timeout } },
            { RaceState.Countdown, new[] { RaceState.Racing, RaceState.RaceCancelled, RaceState.ServerError } },
            { RaceState.Racing, new[] { RaceState.FinalLap, RaceState.Finish, RaceState.RaceCancelled, RaceState.ServerError, RaceState.Timeout } },
            { RaceState.FinalLap, new[] { RaceState.Finish, RaceState.RaceCancelled, RaceState.ServerError, RaceState.Timeout } },
            { RaceState.Finish, new[] { RaceState.Results, RaceState.ServerError } },
            { RaceState.Timeout, new[] { RaceState.Results, RaceState.RaceCancelled } },
            { RaceState.Results, new[] { RaceState.Lobby, RaceState.Waiting } },
            { RaceState.RaceCancelled, new[] { RaceState.Waiting } },
            { RaceState.ServerError, new[] { RaceState.Waiting } },
        };

        public RaceState Current { get; private set; }
        public float TimeInState { get; private set; }
        public event Action<RaceState, RaceState> Transitioned;

        public RaceStateMachine(RaceState initial = RaceState.Waiting)
        {
            Current = initial;
        }

        public bool CanTransition(RaceState to) =>
            Allowed.TryGetValue(Current, out var list) && Array.IndexOf(list, to) >= 0;

        public bool TryTransition(RaceState to)
        {
            if (!CanTransition(to)) return false;
            RaceState from = Current;
            Current = to;
            TimeInState = 0f;
            Transitioned?.Invoke(from, to);
            return true;
        }

        public void Transition(RaceState to)
        {
            if (!TryTransition(to))
            {
                throw new InvalidOperationException($"Illegal race state transition {Current} -> {to}");
            }
        }

        public void Tick(float dt) => TimeInState += dt;

        public bool IsRacing => Current == RaceState.Racing || Current == RaceState.FinalLap;
        public bool IsTerminal => Current == RaceState.Results || Current == RaceState.RaceCancelled || Current == RaceState.ServerError;
    }
}
