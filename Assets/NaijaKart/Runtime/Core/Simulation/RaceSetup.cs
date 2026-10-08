using System;
using System.Collections.Generic;

namespace NaijaKart.Core.Simulation
{
    public enum RaceMode
    {
        QuickRace,
        Ranked,
        PrivateRoom,
        FriendChallenge,
        Tournament,
        Practice
    }

    /// <summary>Immutable description of a race to run. Produced by matchmaking/private room/tournament.</summary>
    [Serializable]
    public sealed class RaceSetup
    {
        public string RaceId;
        public string TrackId;
        public RaceMode Mode = RaceMode.QuickRace;
        public int Laps = 3;
        public ulong Seed = 1;
        public bool ItemsEnabled = true;
        public bool LastmaEnabled = true;
        public bool RoadEventsEnabled = true;
        /// <summary>Optional item allow-list for event rules (Wahala Calendar). Null = library default.</summary>
        public List<string> AllowedItemIds;
        public int MaxPlayers = 8;
        /// <summary>Live-event id applied to this race (Wahala Calendar), for results/analytics. Null = none.</summary>
        public string LiveEventId;
        public float LastmaIntervalMultiplier = 1f;
        public float DriftBoostMultiplier = 1f;
    }
}
