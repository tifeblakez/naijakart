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
        VoteRematch,
        Ping
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
        public PlayerInputFrame Input;
        public long ClientTimeMs;
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
        Pong
    }

    [Serializable]
    public sealed class RoomMemberDto
    {
        public string PlayerId;
        public string DisplayName;
        public string VehicleId;
        public string CharacterId;
        public bool Ready;
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
        public RaceState State;
        public RoomMemberDto[] Members = Array.Empty<RoomMemberDto>();
    }

    [Serializable]
    public sealed class SettledRewardDto
    {
        public string PlayerId;
        public int Xp;
        public long Coins;
        public int RatingDelta;
        public int NewRating;
        public string RankIdBefore;
        public string RankIdAfter;
        public int LevelBefore;
        public int LevelAfter;
        public long CoinBalance;
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
    }
}
