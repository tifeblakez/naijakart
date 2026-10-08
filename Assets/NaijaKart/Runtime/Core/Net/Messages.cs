using System;
using NaijaKart.Core.Input;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;

namespace NaijaKart.Core.Net
{
    /// <summary>
    /// Client → server intents. Deliberately flat so it serialises identically as JSON (debug/tests)
    /// or packed binary (production transport). Clients never send positions or results.
    /// </summary>
    public enum ClientMessageKind
    {
        Hello,
        JoinQueue,
        LeaveQueue,
        CreateRoom,
        JoinRoom,
        LeaveRoom,
        SelectLoadout,
        Ready,
        StartRoom,
        Input,
        PayFine,
        RequestBail,
        PayBail,
        TakePenalty,
        PurchaseVehicle,
        PurchaseCharacter,
        GetGarage,
        VoteRematch,
        Ping,
        AddFriend,
        RemoveFriend,
        GetChallenges,
        GetLeaderboard,
        GetRivalries,
        GetProfile,
        /// <summary>Save progress: Text = phone number, Flag = WhatsApp instead of SMS.</summary>
        ClaimStart,
        /// <summary>Text = the one-time code.</summary>
        ClaimVerify,
        /// <summary>Social sign-in after the backend verified the provider token: Text = provider, AuthToken = subject.</summary>
        ClaimWithProvider,
        /// <summary>DisplayName = racer name, CharacterId = look, Text = home city.</summary>
        SetRacer,
        /// <summary>Text = candidate racer name.</summary>
        CheckName,
        /// <summary>Text = a friend's referral code.</summary>
        ApplyReferral,
        GetAccount,
        /// <summary>Season, Rush Hour and ranked rules for the Ranked hub.</summary>
        GetSeason,
        /// <summary>Host changes private-room options: TrackId, Laps, LastmaMode, ItemsMode, FillWithAi.</summary>
        SetRoomOptions,
        /// <summary>Invite a friend to the room: TargetPlayerId.</summary>
        InviteFriend,
        AcceptFriend,
        DeclineFriend,
        /// <summary>Friends with presence, incoming requests and sent requests.</summary>
        GetFriends,
        GetShop,
        /// <summary>Text = cosmetic id. Coins or premium currency, as priced.</summary>
        PurchaseCosmetic,
        /// <summary>Text = cosmetic id (owned); equips it for its kind.</summary>
        EquipCosmetic,
        GetSeasonPass,
        /// <summary>Laps = tier number; Flag = premium track.</summary>
        ClaimPassTier,
        BuyPremiumPass
    }

    [Serializable]
    public sealed class ClientEnvelope
    {
        public ClientMessageKind Kind;
        public string PlayerId;
        public string AuthToken;
        public string DisplayName;
        public string RoomCode;
        public string TrackId;
        public string VehicleId;
        public string CharacterId;
        public string TargetPlayerId;
        public RaceMode Mode;
        public int Laps;
        public bool Flag;
        public bool ItemsEnabled = true;
        public bool LastmaEnabled = true;
        /// <summary>Private room options (design 03.2): "Off" | "On" | "Madness" and "Off" | "BoostsOnly" | "On".</summary>
        public string LastmaMode;
        public string ItemsMode;
        public bool FillWithAi;
        public PlayerInputFrame Input;
        public long ClientTimeMs;
        /// <summary>Free text argument (leaderboard metric, etc.).</summary>
        public string Text;
        /// <summary>Leaderboard scope: friends | city | nigeria | global | track (TrackId).</summary>
        public string Scope;
    }

    public enum ServerMessageKind
    {
        Welcome,
        Error,
        QueueStatus,
        RoomState,
        RaceSnapshot,
        RaceEvent,
        RaceResults,
        BailRequest,
        Pong,
        ChallengeCompleted,
        Challenges,
        Leaderboard,
        Rivalries,
        Profile,
        LiveEvent,
        /// <summary>Sent to the caught racer with LastmaCaught: fine, friends online, what is allowed.</summary>
        LastmaOptions,
        Garage,
        Account,
        Season,
        /// <summary>A friend invited you: Room = the room, PlayerId = host, Text = host name.</summary>
        RoomInvite,
        Friends,
        /// <summary>Someone wants to be your friend: PlayerId, Text = their name.</summary>
        FriendRequest,
        Shop,
        SeasonPass
    }

    [Serializable]
    public sealed class CosmeticDto
    {
        public string Id;
        public string Kind;
        public string DisplayName;
        public string Description;
        public string AppliesTo;
        public long PriceCoins;
        public long PricePremium;
        public int UnlockLevel;
        public int PassTier;
        public bool PassPremium;
        public string ColorHex;
        public bool Owned;
        public bool Equipped;
        public bool LevelReached;
        public bool IsNew;
        public bool Featured;
        public int FeaturedDaysLeft;
        public string NairaPrice;
        /// <summary>Owned | Equipped | Coins | Premium | Level | Pass — the state chip in the design.</summary>
        public string State;
    }

    [Serializable]
    public sealed class PassTierDto
    {
        public int Tier;
        public long FreeCoins;
        public CosmeticDto FreeCosmetic;
        public long PremiumCoins;
        public CosmeticDto PremiumCosmetic;
        /// <summary>Locked | Claimable | Claimed for each track.</summary>
        public string FreeState;
        public string PremiumState;
    }

    [Serializable]
    public sealed class SeasonPassDto
    {
        public string SeasonId;
        public string SeasonName;
        public int DaysLeft;
        public long SeasonXp;
        public int XpPerTier;
        public int CurrentTier;
        public bool PremiumOwned;
        public long PremiumPricePremium;
        public PassTierDto[] Tiers = Array.Empty<PassTierDto>();
    }

    [Serializable]
    public sealed class FriendDto
    {
        public string PlayerId;
        public string DisplayName;
        public string RankId;
        public string RankLabel;
        public int Level;
        /// <summary>Online, Racing, PrivateRoom, Offline.</summary>
        public string Presence;
        /// <summary>"Online", "Racing · lap 2", "Private Room", "Seen 2h ago".</summary>
        public string PresenceText;
        public long LastSeenUnixMs;
        /// <summary>Joinable room code when the friend hosts or sits in a private room lobby.</summary>
        public string RoomCode;
        /// <summary>The friend is in LASTMA's hands right now and asked for bail.</summary>
        public bool NeedsBail;
        public long BailCost;
    }

    [Serializable]
    public sealed class SeasonDto
    {
        public string Id;
        public string Name;
        public int DaysLeft;
        public string EndsUtc;
        public bool RushHourActive;
        public float RushHourMultiplier;
        /// <summary>Seconds until Rush Hour ends (active) or starts (inactive).</summary>
        public int RushHourSecondsTo;
        public int RushHourStartHour;
        public int RushHourEndHour;
        public int MinRealPlayers;
        public int RpForWin;
        public int RpForLast;
        public int RpPerDivision;
        public int DivisionsPerTier;
    }

    [Serializable]
    public sealed class AccountDto
    {
        public string PlayerId;
        /// <summary>Guest or Claimed.</summary>
        public string Status;
        public string PhoneMasked;
        public string Provider;
        public string RacerName;
        public string HomeCity;
        public string LookId;
        public string ReferralCode;
        public string ReferredBy;
        /// <summary>Outcome of the request that produced this message (ClaimResult / NameStatus / "Ok").</summary>
        public string Result;
        public string NameStatus;
        /// <summary>Set when the phone belongs to another account: Hello again as this player id.</summary>
        public string SignInPlayerId;
        public int ResendInSeconds;
        public bool RankedUnlocked;
        public long AccountBonusCoins;
        public long ReferralBonusCoins;
        public string[] Cities = Array.Empty<string>();
    }

    [Serializable]
    public sealed class GarageItemDto
    {
        public string Id;
        public string DisplayName;
        public string Tagline;
        public bool Owned;
        public bool Selected;
        public long PriceCoins;
        public int UnlockLevel;
        public bool LevelReached;
        public int Speed, Acceleration, Handling, Drift, Weight, Boost, Traction;
    }

    [Serializable]
    public sealed class ChallengeProgressDto
    {
        public string ChallengeId;
        public string DisplayName;
        public string Description;
        public string Cadence;
        public int Value;
        public int Target;
        public bool Completed;
        public long RewardCoins;
        public int RewardXp;
    }

    [Serializable]
    public sealed class LeaderboardRowDto
    {
        public int Rank;
        public string PlayerId;
        public string DisplayName;
        public long Value;
        public string RankId;
        public string RankLabel;
        public string City;
        /// <summary>Track scope: best time in seconds (Value holds it in milliseconds).</summary>
        public float Seconds;
    }

    [Serializable]
    public sealed class RivalryDto
    {
        public string OpponentId;
        public string OpponentName;
        public int MyWins;
        public int TheirWins;
        public int TotalRaces;
        public float MyFastestLap;
        public float TheirFastestLap;
        public string LastWinner;
        public int MyStreak;
        public int TheirStreak;
        /// <summary>Newest last: true where I won.</summary>
        public bool[] RecentIWon = Array.Empty<bool>();
        public int BailsTheyGaveMe;
        public int BailsIGaveThem;
        public long SinceUnixMs;
        public string BestTrackId;
        public float MyBestTime;
        public float TheirBestTime;
        public string OpponentRankLabel;
    }

    [Serializable]
    public sealed class ProfileDto
    {
        public string PlayerId;
        public string DisplayName;
        public int Level;
        public float LevelProgress;
        public int Rating;
        public string RankId;
        public string RankLabel;
        public int RankedPoints;
        public int Division;
        public int RpInDivision;
        public int RpToNextDivision;
        public int[] RecentPositions = Array.Empty<int>();
        public int[] RecentRpDeltas = Array.Empty<int>();
        public string Title;
        public int Races;
        public int Wins;
        public int Podiums;
        public float WinRate;
        public int LastmaEscapes;
        public int CurrentWinStreak;
        public int BestWinStreak;
        public long Coins;
        public long Premium;
        public string[] Achievements = Array.Empty<string>();
        public string[] FriendIds = Array.Empty<string>();
        public string[] UnlockedVehicleIds = Array.Empty<string>();
        public string[] UnlockedCharacterIds = Array.Empty<string>();
        public string SelectedVehicleId;
        public string SelectedCharacterId;
        public string HomeCity;
        public string RacerCode;
        public int AchievementsTotal;
        public long LastSeenUnixMs;
        public string[] BestTrackIds = Array.Empty<string>();
        public float[] BestTrackTimes = Array.Empty<float>();
        public RivalryDto[] TopRivalries = Array.Empty<RivalryDto>();
    }

    [Serializable]
    public sealed class RoomMemberDto
    {
        public string PlayerId;
        public string DisplayName;
        public string VehicleId;
        public string CharacterId;
        public bool Ready;
        public bool IsBot;
        /// <summary>Host, Ready, Waiting, AI, Invited, Joining, Disconnected, AiDriving.</summary>
        public string Status;
        public int Level;
        public string RankId;
        public string Title;
    }

    [Serializable]
    public sealed class RoomStateDto
    {
        public string RoomCode;
        public string RaceId;
        public string HostPlayerId;
        public string TrackId;
        public RaceMode Mode;
        public int Laps;
        public bool ItemsEnabled;
        public bool LastmaEnabled;
        public string LastmaMode;
        public string ItemsMode;
        public bool FillWithAi;
        public string ShareUrl;
        /// <summary>Public lobbies: seconds until the race starts on its own; -1 when the host decides.</summary>
        public int StartsInSeconds = -1;
        public int AiSeats;
        public float ReconnectWindowSeconds;
        public int ReconnectAttempts;
        public RaceState State;
        public RoomMemberDto[] Members = Array.Empty<RoomMemberDto>();
        public RoomMemberDto[] Invited = Array.Empty<RoomMemberDto>();
    }

    [Serializable]
    public sealed class SettledRewardDto
    {
        public string PlayerId;
        public int Xp;
        public long Coins;
        public int RatingDelta;
        public int NewRating;
        public int RpDelta;
        public int RpAfter;
        public bool RushHour;
        public int DivisionBefore;
        public int DivisionAfter;
        public string RankIdBefore;
        public string RankIdAfter;
        public int LevelBefore;
        public int LevelAfter;
        public long CoinBalance;
    }

    [Serializable]
    public sealed class LastmaOptionsDto
    {
        public long FineAmount;
        public float DecisionSeconds;
        public float PenaltySeconds;
        public float FineResumeSeconds;
        public bool FinesAllowed;
        public bool BailAllowed;
        public bool CanAffordFine;
        public int FriendsOnline;
        public bool Ranked;
    }

    [Serializable]
    public sealed class ServerEnvelope
    {
        public ServerMessageKind Kind;
        public int Tick;
        public long ServerTimeMs;
        public string Error;
        public string Text;
        public RoomStateDto Room;
        public RaceSnapshot Snapshot;
        public RaceEvent Event;
        public RaceResults Results;
        public SettledRewardDto[] Rewards;
        /// <summary>For BailRequest: who needs bail and how much.</summary>
        public string PlayerId;
        public long Amount;
        public long ClientTimeMs;
        public ChallengeProgressDto[] Challenges;
        public LeaderboardRowDto[] Leaderboard;
        public RivalryDto[] Rivalries;
        public ProfileDto Profile;
        public LastmaOptionsDto LastmaOptions;
        public GarageItemDto[] Vehicles;
        public GarageItemDto[] Characters;
        public long PremiumBalance;
        public AccountDto Account;
        public SeasonDto Season;
        /// <summary>QueueStatus: racers found, grid size, seconds until AI fills the rest.</summary>
        public int QueueFound;
        public int QueueMax;
        public int QueueSecondsToAi;
        public FriendDto[] Friends;
        public FriendDto[] FriendRequests;
        public FriendDto[] SentRequests;
        public CosmeticDto[] Cosmetics;
        public SeasonPassDto SeasonPass;
    }
}
