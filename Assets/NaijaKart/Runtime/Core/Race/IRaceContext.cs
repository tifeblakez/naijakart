using System.Collections.Generic;
using NaijaKart.Core.Chaos;
using NaijaKart.Core.Config;
using NaijaKart.Core.Track;
using NaijaKart.Core.Util;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Race
{
    /// <summary>
    /// What subsystems (items, hazards, LASTMA) need from the simulation without owning it.
    /// Keeps the subsystems decoupled and individually testable with a fake context.
    /// </summary>
    public interface IRaceContext
    {
        string RaceId { get; }
        GameConfig Config { get; }
        IConfigSource Content { get; }
        TrackGeometry Track { get; }
        ArcadeVehicleModel VehicleModel { get; }
        HazardSystem Hazards { get; }
        DeterministicRandom Rng { get; }
        ILogger Log { get; }
        float RaceTime { get; }
        int Tick { get; }
        IReadOnlyList<RaceParticipant> Participants { get; }
        RaceParticipant Find(string playerId);
        /// <summary>Active racers only (not finished/eliminated), sorted by live position.</summary>
        IReadOnlyList<RaceParticipant> ActiveByPosition { get; }
        RaceParticipant NearestAhead(RaceParticipant from, float maxDistance);
        RaceParticipant Leader { get; }
        void Emit(RaceEvent e);
        RaceEvent Emit(RaceEventType type, string playerId = null, string target = null, string payload = null, int i = 0, float f = 0f);
        /// <summary>Removes a racer from competition (LASTMA arrest). They continue as a spectator.</summary>
        void Eliminate(RaceParticipant p, string reason);
    }
}
