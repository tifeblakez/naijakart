using System;

namespace NaijaKart.Core.Challenges
{
    /// <summary>Which telemetry counter a rule reads (PRD §41–§43). Values come from ParticipantStats / results.</summary>
    public enum ChallengeMetric
    {
        RacesCompleted,
        Wins,
        TopThreeFinishes,
        Drifts,
        PurpleDrifts,
        Boosts,
        Overtakes,
        ItemsUsed,
        ItemHitsLanded,
        LastmaEscapes,
        BailGiven,
        ShortcutsTaken,
        FriendsBeaten,
        DistinctTracksWon,
        WinsWithoutItems,
        WinsFromLastOnFinalLap,
        TournamentWins
    }

    public enum ChallengeCadence { Daily, Weekly, Achievement }

    /// <summary>
    /// One rule: accumulate a metric until a target. Daily/weekly rules reset per period; achievements
    /// are permanent. Rewards are Coins/XP/title. Data rows, never code (PRD §69).
    /// </summary>
    [Serializable]
    public sealed class ChallengeDefinition
    {
        public string id;
        public string displayName;
        public string description;
        public ChallengeCadence cadence = ChallengeCadence.Daily;
        public ChallengeMetric metric;
        public int target = 1;
        public long rewardCoins;
        public int rewardXp;
        /// <summary>Title unlocked on completion (achievements only).</summary>
        public string rewardTitle;
        public bool disabled;
    }

    [Serializable]
    public sealed class ChallengeLibrary
    {
        public ChallengeDefinition[] challenges = Array.Empty<ChallengeDefinition>();
    }

    /// <summary>Per-player progress on one rule within the current period.</summary>
    [Serializable]
    public sealed class ChallengeProgress
    {
        public string challengeId;
        /// <summary>Period key: yyyy-MM-dd for daily, yyyy-Www for weekly, "" for achievements.</summary>
        public string periodKey = "";
        public int value;
        public bool completed;
        public bool rewardClaimed;
    }
}
