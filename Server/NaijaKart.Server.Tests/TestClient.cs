using System.Collections.Generic;
using NaijaKart.Core.Input;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;

namespace NaijaKart.Server.Tests
{
    /// <summary>
    /// A headless client that speaks only the public wire protocol: Hello, room/queue intents and
    /// PlayerInputFrames. It drives its kart from the snapshots it receives (like a real client would
    /// predict from), never touching server objects. Used to prove the server works end to end.
    /// </summary>
    public sealed class TestClient
    {
        private readonly IClientTransport _transport;
        private readonly BotDriver _bot;
        private readonly TrackGeometry _track;
        private int _seq;
        private bool _fineHandled;

        public string PlayerId { get; }
        public RoomStateDto Room { get; private set; }
        public RaceSnapshot LastSnapshot { get; private set; }
        public RaceResults Results { get; private set; }
        public SettledRewardDto[] Rewards { get; private set; }
        public ServerEnvelope Welcome { get; private set; }
        public List<RaceEvent> Events { get; } = new List<RaceEvent>();
        public List<string> Errors { get; } = new List<string>();
        public List<ServerEnvelope> BailRequests { get; } = new List<ServerEnvelope>();
        public List<ServerEnvelope> Completed { get; } = new List<ServerEnvelope>();
        public ChallengeProgressDto[] ChallengeList { get; private set; }
        public LeaderboardRowDto[] LeaderboardRows { get; private set; }
        public RivalryDto[] RivalryList { get; private set; }
        public ProfileDto Profile { get; private set; }
        public string LiveEventName { get; private set; }
        public LastmaOptionsDto LastOptions { get; private set; }
        public ServerEnvelope Garage { get; private set; }
        public AccountDto Account { get; private set; }
        public SeasonDto Season { get; private set; }
        public ServerEnvelope Invite { get; private set; }
        public ServerEnvelope QueueStatus { get; private set; }
        public ServerEnvelope FriendsMsg { get; private set; }
        public List<ServerEnvelope> FriendRequestsReceived { get; } = new List<ServerEnvelope>();
        public long LastBalance { get; private set; }
        /// <summary>What to do when pulled over: "pay", "bail", "penalty" or null (let the timer decide).</summary>
        public string PullOverChoice = "pay";
        public int SnapshotsReceived { get; private set; }
        public bool AutoPayFine = true;
        public bool AutoBailOthers = false;
        public bool AutoVoteRematch = false;
        public IClientTransport Transport => _transport;

        public TestClient(string playerId, IClientTransport transport, TrackGeometry track, ulong seed, float skill = 0.6f)
        {
            PlayerId = playerId;
            _transport = transport;
            _track = track;
            _bot = new BotDriver(track, seed, skill);
            _transport.MessageReceived += OnMessage;
        }

        private string _displayName;
        private float _sinceHello;

        public void Connect(string displayName = null)
        {
            _displayName = displayName ?? PlayerId;
            _transport.Connect();
            SendHello();
        }

        private void SendHello()
        {
            _sinceHello = 0f;
            _transport.Send(new ClientEnvelope { Kind = ClientMessageKind.Hello, PlayerId = PlayerId, DisplayName = _displayName });
        }

        public void Send(ClientEnvelope env)
        {
            env.PlayerId = PlayerId;
            _transport.Send(env);
        }

        private void OnMessage(ServerEnvelope m)
        {
            switch (m.Kind)
            {
                case ServerMessageKind.Welcome: Welcome = m; break;
                case ServerMessageKind.Error: Errors.Add(m.Error); break;
                case ServerMessageKind.RoomState: Room = m.Room; break;
                case ServerMessageKind.RaceSnapshot: LastSnapshot = m.Snapshot; SnapshotsReceived++; break;
                case ServerMessageKind.RaceEvent: Events.Add(m.Event); break;
                case ServerMessageKind.ChallengeCompleted: Completed.Add(m); break;
                case ServerMessageKind.Challenges: ChallengeList = m.Challenges; break;
                case ServerMessageKind.Leaderboard: LeaderboardRows = m.Leaderboard; break;
                case ServerMessageKind.Rivalries: RivalryList = m.Rivalries; break;
                case ServerMessageKind.Profile: Profile = m.Profile; break;
                case ServerMessageKind.LiveEvent: LiveEventName = m.Text; break;
                case ServerMessageKind.LastmaOptions: LastOptions = m.LastmaOptions; break;
                case ServerMessageKind.Garage: Garage = m; break;
                case ServerMessageKind.Account: Account = m.Account; LastBalance = m.Amount; break;
                case ServerMessageKind.Season: Season = m.Season; break;
                case ServerMessageKind.RoomInvite: Invite = m; break;
                case ServerMessageKind.QueueStatus: QueueStatus = m; break;
                case ServerMessageKind.Friends: FriendsMsg = m; break;
                case ServerMessageKind.FriendRequest: FriendRequestsReceived.Add(m); break;
                case ServerMessageKind.RaceResults:
                    if (Results != null && m.Results != null && Results.RaceId == m.Results.RaceId) break; // re-sent copy
                    Results = m.Results;
                    Rewards = m.Rewards;
                    if (AutoVoteRematch) Send(new ClientEnvelope { Kind = ClientMessageKind.VoteRematch });
                    break;
                case ServerMessageKind.BailRequest:
                    BailRequests.Add(m);
                    if (AutoBailOthers) Send(new ClientEnvelope { Kind = ClientMessageKind.PayBail, TargetPlayerId = m.PlayerId });
                    break;
            }
        }

        /// <summary>Client frame: pump the transport and, if racing, send an input derived from the last snapshot.</summary>
        public void Tick(float dt)
        {
            _transport.PumpIncoming();
            if (Welcome == null && _transport.IsConnected)
            {
                _sinceHello += dt;
                if (_sinceHello > 1f) SendHello(); // Hello may have been lost
            }
            if (LastSnapshot == null || !(LastSnapshot.State == RaceState.Racing || LastSnapshot.State == RaceState.FinalLap)) return;
            ParticipantSnapshot me = default;
            bool found = false;
            foreach (var p in LastSnapshot.Participants) if (p.PlayerId == PlayerId) { me = p; found = true; break; }
            if (!found) return;
            if (me.LastmaPhase == Core.Lastma.LastmaPhase.FinePending)
            {
                // React once per pull-over, like the real UI does on the LastmaCaught event.
                if (!_fineHandled)
                {
                    _fineHandled = true;
                    string choice = AutoPayFine ? "pay" : PullOverChoice;
                    if (choice == "pay") Send(new ClientEnvelope { Kind = ClientMessageKind.PayFine });
                    else if (choice == "bail") Send(new ClientEnvelope { Kind = ClientMessageKind.RequestBail });
                    else if (choice == "penalty") Send(new ClientEnvelope { Kind = ClientMessageKind.TakePenalty });
                }
                return;
            }
            _fineHandled = false;
            var ghost = new RaceParticipant { PlayerId = PlayerId, State = me.FullState, Stats = new Core.Vehicle.VehicleStats { TopSpeed = 30f } };
            var frame = _bot.Think(ghost, dt);
            frame.Sequence = ++_seq;
            Send(new ClientEnvelope { Kind = ClientMessageKind.Input, Input = frame });
        }
    }
}
