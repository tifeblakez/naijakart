using System;
using System.Collections.Generic;

namespace NaijaKart.Core.Progression
{
    /// <summary>Persisted player identity and progression (PRD §44, §92). Coins live in the ledger, not here.</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string PlayerId;
        public string DisplayName;
        public long TotalXp;
        public int Rating;
        public int Races;
        public int Wins;
        public int Podiums;
        public int TournamentWins;
        public int LastmaEscapes;
        public int LastmaArrests;
        public int CurrentWinStreak;
        public int BestWinStreak;
        public string FavouriteVehicleId;
        public string FavouriteTrackId;
        public string Title;
        public List<string> UnlockedVehicleIds = new List<string>();
        public List<string> UnlockedCharacterIds = new List<string>();
        public List<string> Achievements = new List<string>();
        public List<string> FriendIds = new List<string>();

        public float WinRate => Races == 0 ? 0f : (float)Wins / Races;
    }
}
