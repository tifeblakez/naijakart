using System;
using System.IO;
using System.Linq;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Server.Config;
using NaijaKart.Server.Hosting;
using NaijaKart.Server.Persistence;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    public class ProgressionServerTests
    {
        private static void RunPrivateRace(ServerHarness h, TestClient a, TestClient b, bool bots = false)
        {
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = bots });
            Assert.That(h.RunUntil(() => a.Results != null && b.Results != null, 300f, a, b), Is.True);
        }

        [Test]
        public void ShippedChallengesLoadAndCompleteOverTheWire()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.lastma.enabled = false; g.roadEvents.enabled = false; });
            Assert.That(h.Content.Challenges.challenges.Length, Is.GreaterThanOrEqualTo(15), "PRD §41–§43 rules shipped");
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.5f);
            RunPrivateRace(h, a, b);
            string winner = a.Results.Entries[0].PlayerId;
            var w = winner == "a" ? a : b;
            h.Run(0.2f, a, b);
            Assert.That(w.Completed.Any(c => c.Text == "Podium Guy"), "daily top-3 completes after one race: " + string.Join(",", w.Completed.Select(c => c.Text)));
            long coinsBefore = h.Server.Ledger.GetBalance(winner);
            w.Send(new ClientEnvelope { Kind = ClientMessageKind.GetChallenges });
            h.Run(0.1f, a, b);
            Assert.That(w.ChallengeList, Is.Not.Null);
            var podium = w.ChallengeList.First(c => c.ChallengeId == "daily_top3");
            Assert.That(podium.Completed, Is.True);
            var races = w.ChallengeList.First(c => c.ChallengeId == "daily_races_3");
            Assert.That(races.Value, Is.EqualTo(1));
            Assert.That(h.Server.Ledger.History(winner).Any(t => t.Source.StartsWith("challenge:")), "challenge reward paid through the ledger");
            Assert.That(coinsBefore, Is.GreaterThan(h.Content.Game.economy.startingBalance));
        }

        [Test]
        public void FriendsRivalriesLeaderboardAndProfile()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.lastma.enabled = false; g.roadEvents.enabled = false; });
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.5f);
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = "b" });
            h.Run(0.1f, a, b);
            Assert.That(a.Profile, Is.Not.Null);
            Assert.That(a.Profile.FriendIds, Does.Contain("b"));
            Assert.That(h.Server.Profiles.Get("b").FriendIds, Does.Contain("a"), "symmetric first pass");

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => a.Results != null && b.Results != null, 300f, a, b), Is.True);

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetRivalries });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetLeaderboard, Text = "wins" });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetProfile });
            h.Run(0.1f, a, b);
            Assert.That(a.RivalryList.Length, Is.EqualTo(1));
            Assert.That(a.RivalryList[0].OpponentId, Is.EqualTo("b"));
            Assert.That(a.RivalryList[0].TotalRaces, Is.EqualTo(1));
            Assert.That(a.RivalryList[0].MyWins + a.RivalryList[0].TheirWins, Is.EqualTo(1));
            Assert.That(a.LeaderboardRows.Length, Is.EqualTo(2));
            Assert.That(a.LeaderboardRows[0].Rank, Is.EqualTo(1));
            Assert.That(a.LeaderboardRows[0].Value, Is.EqualTo(1), "one win on the board");
            Assert.That(a.Profile.Races, Is.EqualTo(1));
            Assert.That(a.Profile.Level, Is.GreaterThanOrEqualTo(1));

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetLeaderboard, Text = "rating", Flag = true });
            h.Run(0.1f, a, b);
            Assert.That(a.LeaderboardRows.Length, Is.EqualTo(2), "friends board includes self + friend");
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.RemoveFriend, TargetPlayerId = "b" });
            h.Run(0.1f, a, b);
            Assert.That(a.Profile.FriendIds, Is.Empty);
        }

        [Test]
        public void WahalaCalendarAppliesToPublicRaces()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.raceRules.matchmakingWaitSeconds = 1f; g.roadEvents.enabled = false; });
            h.Server.UtcNow = () => new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc); // Monday: No Item Race
            var a = h.NewClient("a", 0.8f); var b = h.NewClient("b", 0.8f);
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            Assert.That(a.LiveEventName, Is.EqualTo("No Item Race"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.QuickRace });
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.QuickRace });
            Assert.That(h.RunUntil(() => a.Room != null, 5f, a, b), Is.True);
            Assert.That(a.Room.ItemsEnabled, Is.False);
            var room = h.Server.GetRoom(a.Room.RoomCode);
            Assert.That(room.LiveEvent.id, Is.EqualTo("no_item_race"));
            Assert.That(room.Race.Setup.LiveEventId, Is.EqualTo("no_item_race"));
            Assert.That(h.RunUntil(() => a.Results != null, 300f, a, b), Is.True);
            Assert.That(a.Events.Any(e => e.Type == RaceEventType.ItemGranted), Is.False, "no items on Monday");

            // Sunday: Chill Race switches LASTMA off; Saturday City Cup boosts ranked XP only.
            h.Server.UtcNow = () => new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(h.Server.TodayRule().lastmaEnabled, Is.False);
        }

        [Test]
        public void StateSurvivesServerRestartThroughJsonFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "nk-state-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var content = new JsonConfigSource(ServerHarness.ConfigDir());
                content.Game.raceRules.defaultLaps = 1; content.Game.lastma.enabled = false; content.Game.roadEvents.enabled = false;
                long balanceAfterRace; int racesAfter;
                using (var state = new JsonFileStateStore(path, content.Game.progression))
                {
                    var hub = new LoopbackTransportHub(3);
                    var server = new GameServer(content, hub.Server, null, state.Coins, state.Profiles, state.Rivalries, challengeProgress: state.Challenges);
                    var track = new Core.Track.TrackGeometry(content.GetTrack(content.TrackIds[0]));
                    var a = new TestClient("a", hub.CreateClient("a"), track, 1, 0.9f);
                    var b = new TestClient("b", hub.CreateClient("b"), track, 2, 0.5f);
                    float dt = server.FixedDeltaTime;
                    void Tick() { hub.Advance(dt); server.Tick(); a.Tick(dt); b.Tick(dt); }
                    a.Connect(); b.Connect();
                    for (int i = 0; i < 3; i++) Tick();
                    a.Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = "b" });
                    a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
                    for (int i = 0; i < 3; i++) Tick();
                    b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
                    for (int i = 0; i < 3; i++) Tick();
                    a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
                    int guard = 0;
                    while ((a.Results == null || b.Results == null) && guard++ < 30 * 300) Tick();
                    Assert.That(a.Results, Is.Not.Null);
                    balanceAfterRace = server.Ledger.GetBalance("a");
                    racesAfter = server.Profiles.Get("a").Races;
                    Assert.That(racesAfter, Is.EqualTo(1));
                    state.Flush();
                }
                Assert.That(File.Exists(path));

                using (var reloaded = new JsonFileStateStore(path, content.Game.progression))
                {
                    var hub = new LoopbackTransportHub(4);
                    var server = new GameServer(content, hub.Server, null, reloaded.Coins, reloaded.Profiles, reloaded.Rivalries, challengeProgress: reloaded.Challenges);
                    Assert.That(server.Ledger.GetBalance("a"), Is.EqualTo(balanceAfterRace));
                    Assert.That(server.Profiles.Get("a").Races, Is.EqualTo(racesAfter));
                    Assert.That(server.Profiles.Get("a").FriendIds, Does.Contain("b"));
                    Assert.That(server.Rivalries.For("a").Count(), Is.EqualTo(1));
                    Assert.That(server.Ledger.History("a").Count, Is.GreaterThanOrEqualTo(1), "transaction log persisted");
                    Assert.That(server.Challenges.CurrentProgress("a").Any(p => p.value > 0 || p.completed), "challenge progress persisted");
                    // Idempotency survives restart: replaying the race reward key is a duplicate.
                    Assert.That(server.Ledger.Credit("a", 1, "test", server.Ledger.History("a")[0].IdempotencyKey, out _), Is.EqualTo(Core.Economy.TransactionResult.Duplicate));
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
