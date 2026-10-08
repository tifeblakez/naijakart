using System;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Race
{
    /// <summary>Every notable thing that happens in a race. Drives HUD feedback, audio, analytics and Wahala.</summary>
    public enum RaceEventType
    {
        StateChanged,
        CountdownTick,
        RaceStarted,
        GatePassed,
        LapCompleted,
        LapRejected,
        FinalLapStarted,
        PlayerFinished,
        PlayerDnf,
        PlayerEliminated,
        Overtake,
        ItemBoxTaken,
        ItemGranted,
        ItemUsed,
        ItemHit,
        ShieldBlocked,
        HazardSpawned,
        HazardHit,
        HazardExpired,
        RoadEventTelegraph,
        LastmaWarning,
        LastmaPursuitStarted,
        LastmaEscaped,
        LastmaCaught,
        LastmaFinePaid,
        LastmaBailRequested,
        LastmaBailed,
        LastmaArrested,
        DriftStarted,
        DriftLevelUp,
        BoostStarted,
        Collision,
        WentOffroad,
        Recovered,
        PlayerDisconnected,
        PlayerReconnected,
        PlayerJoined,
        PlayerLeft,
        RematchVote,
        RaceTimeout,
        RaceCancelled
    }

    [Serializable]
    public sealed class RaceEvent
    {
        public RaceEventType Type;
        public int Tick;
        public float Time;
        public string PlayerId;
        public string TargetPlayerId;
        /// <summary>Item id, hazard id, state name, etc.</summary>
        public string Payload;
        public int IntValue;
        public float FloatValue;
        public Vec3 Position;

        public override string ToString() =>
            $"[{Time:0.00}s #{Tick}] {Type} p={PlayerId} t={TargetPlayerId} {Payload} i={IntValue} f={FloatValue:0.00}";
    }
}
