using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using NaijaKart.Core.Track;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Race
{
    /// <summary>All authoritative per-racer state for one race. Owned exclusively by the simulation.</summary>
    public sealed class RaceParticipant
    {
        public string PlayerId;
        public string DisplayName;
        public VehicleDefinition Vehicle;
        public CharacterDefinition Character;
        public VehicleStats Stats;
        public VehicleState State;
        public CheckpointTracker Checkpoints;
        public PlayerInputFrame LatestInput;
        public int LastInputSequence;
        public ParticipantStatus Status = ParticipantStatus.Connected;
        public float DisconnectedAt = -1f;
        public int GridSlot;
        /// <summary>1-based live race position.</summary>
        public int Position;
        /// <summary>Finishing position; 0 until finished.</summary>
        public int FinishPosition;
        public float FinishTime;
        public bool IsReady;
        public bool VotedRematch;
        public bool IsBot;

        public readonly ParticipantStats Telemetry = new ParticipantStats();

        public bool IsActiveRacer =>
            Status == ParticipantStatus.Connected || Status == ParticipantStatus.Disconnected || Status == ParticipantStatus.Reconnecting;

        public bool HasFinishedOrOut => Status == ParticipantStatus.Finished || Status == ParticipantStatus.DidNotFinish
                                        || Status == ParticipantStatus.Eliminated || Status == ParticipantStatus.Spectating;
    }

    /// <summary>Per-race counters that feed Wahala highlights, challenges, XP bonuses and analytics (PRD §57, §78).</summary>
    public sealed class ParticipantStats
    {
        public int Overtakes;
        public int TimesOvertaken;
        public int ItemsUsed;
        public int ItemHitsLanded;
        public int ItemHitsTaken;
        public int Drifts;
        public int PurpleDrifts;
        public int Boosts;
        public int ShortcutsTaken;
        public int Collisions;
        public int HazardHits;
        public int LastmaTargeted;
        public int LastmaEscapes;
        public int LastmaFinesPaid;
        public int BailRequested;
        public int BailReceived;
        public int BailGiven;
        public int Recoveries;
        public int WorstPosition;
        public int PositionAtFinalLapStart;
        public float TopSpeed;
    }
}
