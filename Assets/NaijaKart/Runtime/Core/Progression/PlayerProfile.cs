using System;
using System.Collections.Generic;

namespace NaijaKart.Core.Progression
{
    [Serializable]
    public sealed class RankedResultRecord
    {
        public int Position;
        public int RpDelta;
        public bool RushHour;
    }

    /// <summary>Persisted player identity and progression (PRD §44, §92). Coins live in the ledger, not here.</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string PlayerId;
        public string DisplayName;
        public long TotalXp;
        /// <summary>Hidden skill rating (Elo) used for matchmaking.</summary>
        public int Rating;
        /// <summary>Ranked Points: the displayed ladder (tiers and divisions, ADR-0010).</summary>
        public int RankedPoints;
        /// <summary>Season the profile was last reset for.</summary>
        public string SeasonId;
        /// <summary>Most recent ranked results, newest last: finishing position and RP delta.</summary>
        public List<RankedResultRecord> RecentRanked = new List<RankedResultRecord>();
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
        /// <summary>Pending friend requests received / sent (design 16: Requests tab).</summary>
        public List<string> FriendRequestsIn = new List<string>();
        public List<string> FriendRequestsOut = new List<string>();
        /// <summary>Last time the player was connected (presence: "Seen 2h ago").</summary>
        public long LastSeenUnixMs;
        /// <summary>Best finished race time per track ("Best: Third Mainland 3:18.40").</summary>
        public Dictionary<string, float> TrackBestTimes = new Dictionary<string, float>();
        /// <summary>Cosmetics (looks only): owned ids and the equipped id per kind.</summary>
        public List<string> OwnedCosmeticIds = new List<string>();
        public Dictionary<string, string> EquippedCosmetics = new Dictionary<string, string>();
        /// <summary>Season pass: XP this season, premium pass owned, claimed tiers ("free:3", "premium:3").</summary>
        public long SeasonXp;
        public bool PremiumPass;
        public List<string> ClaimedPassTiers = new List<string>();
        public List<string> TracksWon = new List<string>();
        public string SelectedVehicleId;
        public string SelectedCharacterId;

        public float WinRate => Races == 0 ? 0f : (float)Wins / Races;
    }
}
