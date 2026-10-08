using System;
using System.Collections.Generic;
using NaijaKart.Core.Race;
using NaijaKart.Core.Social;

namespace NaijaKart.Core.Simulation
{
    [Serializable]
    public sealed class RaceResultEntry
    {
        public string PlayerId;
        public string DisplayName;
        public string VehicleId;
        public string CharacterId;
        /// <summary>1-based. DNF/eliminated racers are ordered after finishers by progress.</summary>
        public int FinishPosition;
        public bool Finished;
        public ParticipantStatus Status;
        public float TotalTime;
        public float BestLap;
        public int LapsCompleted;
        public ParticipantStats Stats;
        public List<Highlight> Highlights;
        public bool IsBot;
    }

    /// <summary>Authoritative outcome of a race. Built once by the simulation; rewards are attached by settlement.</summary>
    [Serializable]
    public sealed class RaceResults
    {
        public string RaceId;
        public string TrackId;
        public RaceMode Mode;
        public int Laps;
        public float RaceDuration;
        public bool TimedOut;
        public bool Cancelled;
        public List<RaceResultEntry> Entries = new List<RaceResultEntry>();

        public RaceResultEntry For(string playerId)
        {
            foreach (var e in Entries) if (e.PlayerId == playerId) return e;
            return null;
        }
    }
}
