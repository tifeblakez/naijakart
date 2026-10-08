using System;
using NaijaKart.Core.Chaos;
using NaijaKart.Core.Lastma;
using NaijaKart.Core.Math;
using NaijaKart.Core.Race;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Simulation
{
    /// <summary>Wire-friendly view of one racer. Everything a client needs to render and predict.</summary>
    [Serializable]
    public struct ParticipantSnapshot
    {
        public string PlayerId;
        public Vec3 Position;
        public float Heading;
        public float Speed;
        public float LateralVelocity;
        public int Lap;
        public int NextCheckpoint;
        public int RacePosition;
        public ParticipantStatus Status;
        public bool IsDrifting;
        public DriftLevel DriftLevel;
        public float DriftCharge;
        public bool IsBoosting;
        public bool IsStunned;
        public float BlindTimeRemaining;
        public float ShieldTimeRemaining;
        public float WobbleTimeRemaining;
        public bool IsImmobilised;
        public string HeldItemId;
        public bool ItemReady;
        public LastmaPhase LastmaPhase;
        public float LastmaPressure;
        public float LastmaTimeRemaining;
        public bool LastmaBailRequested;
        /// <summary>Last client input sequence the server applied; used for prediction reconciliation.</summary>
        public int LastInputSequence;
        /// <summary>Full state for the owning client's reconciliation (other clients ignore most of it).</summary>
        public VehicleState FullState;
    }

    [Serializable]
    public struct HazardSnapshot
    {
        public string Id;
        public HazardKind Kind;
        public Vec3 Position;
        public Vec3 Velocity;
        public float Radius;
        public float ZoneHalfLength;
        public Vec3 ZoneDirection;
        public bool Armed;
        public string OwnerPlayerId;
    }

    [Serializable]
    public struct ItemBoxSnapshot
    {
        public string Id;
        public bool Available;
    }

    [Serializable]
    public sealed class RaceSnapshot
    {
        public string RaceId;
        public int Tick;
        public float Time;
        public RaceState State;
        public float StateTime;
        public int CountdownValue;
        public ParticipantSnapshot[] Participants;
        public HazardSnapshot[] Hazards;
        public ItemBoxSnapshot[] ItemBoxes;
    }
}
