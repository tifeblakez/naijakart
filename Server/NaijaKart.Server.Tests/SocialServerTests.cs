using System.Linq;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    /// <summary>Social, rivalry, profile and leaderboard screens (design 12, 14, 15, 16).</summary>
    public class SocialServerTests
    {
        [Test]
        public void FriendRequestsNeedAnAcceptAndPresenceFollowsTheRacer()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.lastma.enabled = false; g.roadEvents.enabled = false; });
            var tife = h.NewClient("tife"); var chuks = h.NewClient("chuks"); var bisi = h.NewClient("bisi");
            tife.Connect(); chuks.Connect(); bisi.Connect(); h.Run(0.1f, tife, chuks, bisi);

            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = "chuks" });
            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = "bisi" });
            h.Run(0.1f, tife, chuks, bisi);
            Assert.That(chuks.FriendRequestsReceived.Select(r => r.PlayerId), Does.Contain("tife"), "the other racer is told");
            Assert.That(tife.FriendsMsg.SentRequests.Select(f => f.PlayerId), Is.EquivalentTo(new[] { "chuks", "bisi" }));
            Assert.That(tife.FriendsMsg.Friends, Is.Empty, "not friends until accepted");

            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.AcceptFriend, TargetPlayerId = "tife" });
            bisi.Send(new ClientEnvelope { Kind = ClientMessageKind.DeclineFriend, TargetPlayerId = "tife" });
            h.Run(0.1f, tife, chuks, bisi);
            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.GetFriends });
            h.Run(0.1f, tife, chuks, bisi);
            Assert.That(tife.FriendsMsg.Friends.Select(f => f.PlayerId), Is.EquivalentTo(new[] { "chuks" }));
            Assert.That(tife.FriendsMsg.SentRequests, Is.Empty, "declined requests disappear");
            Assert.That(tife.FriendsMsg.Friends[0].Presence, Is.EqualTo("Online"));

            // Chuks opens a private room: presence shows a joinable room. Then he races: "Racing · lap 1".
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, tife, chuks, bisi);
            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.GetFriends });
            h.Run(0.1f, tife, chuks, bisi);
            Assert.That(tife.FriendsMsg.Friends[0].Presence, Is.EqualTo("PrivateRoom"));
            Assert.That(tife.FriendsMsg.Friends[0].RoomCode, Is.EqualTo(chuks.Room.RoomCode));
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = true });
            Assert.That(h.RunUntil(() => chuks.LastSnapshot != null && chuks.LastSnapshot.State == RaceState.Racing, 20f, tife, chuks, bisi), Is.True);
            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.GetFriends });
            h.Run(0.1f, tife, chuks, bisi);
            Assert.That(tife.FriendsMsg.Friends[0].Presence, Is.EqualTo("Racing"));
            Assert.That(tife.FriendsMsg.Friends[0].PresenceText, Does.StartWith("Racing · lap"));

            // Offline shows when they were last seen.
            chuks.Transport.Disconnect();
            h.Run(0.2f, tife, bisi);
            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.GetFriends });
            h.Run(0.1f, tife, bisi);
            Assert.That(tife.FriendsMsg.Friends[0].Presence, Is.EqualTo("Offline"));
            Assert.That(tife.FriendsMsg.Friends[0].PresenceText, Does.StartWith("Seen"));
            Assert.That(tife.FriendsMsg.Friends[0].LastSeenUnixMs, Is.GreaterThan(0));
        }

        [Test]
        public void RivalryKeepsLastFiveBestTimesAndBailsAndTrackLeaderboardRanksByTime()
        {
            var h = new ServerHarness(g =>
            {
                g.raceRules.defaultLaps = 1; g.roadEvents.enabled = false; g.social.friendRequestsRequireAccept = false;
                g.lastma.firstTriggerMinSeconds = 2f; g.lastma.minIntervalSeconds = 1f; g.lastma.maxIntervalSeconds = 2f;
                g.lastma.warningSeconds = 0.5f; g.lastma.pursuitSeconds = 1.5f; g.lastma.pressureGainPerSecond = 3f; g.lastma.positionWeights = new[] { 1f };
            });
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.6f);
            a.PullOverChoice = "bail"; a.AutoPayFine = false; b.AutoBailOthers = true; b.AutoPayFine = false; b.PullOverChoice = "penalty";
            a.Connect(); b.Connect(); h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = "b" });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => a.Results != null && b.Results != null, 300f, a, b), Is.True);
            int bails = a.Events.Count(e => e.Type == RaceEventType.LastmaBailed && e.PlayerId == "a");

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetRivalries });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetLeaderboard, Scope = "track", TrackId = h.Content.TrackIds[0] });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetProfile });
            h.Run(0.2f, a, b);
            var r = a.RivalryList.Single();
            Assert.That(r.RecentIWon.Length, Is.EqualTo(1), "Last 5 list");
            Assert.That(r.RecentIWon[0], Is.EqualTo(r.MyWins == 1));
            Assert.That(r.SinceUnixMs, Is.GreaterThan(0), "Rivalry · since");
            Assert.That(r.OpponentRankLabel, Does.StartWith("JJC"));
            if (a.Results.Entries.All(e => e.Finished))
            {
                Assert.That(r.BestTrackId, Is.EqualTo(h.Content.TrackIds[0]));
                Assert.That(r.MyBestTime, Is.GreaterThan(0f)); Assert.That(r.TheirBestTime, Is.GreaterThan(0f));
            }
            Assert.That(r.BailsTheyGaveMe, Is.EqualTo(bails), "He bailed you N times");

            var finished = a.Results.Entries.Where(e => e.Finished && !e.IsBot).OrderBy(e => e.TotalTime).Select(e => e.PlayerId).ToArray();
            Assert.That(a.LeaderboardRows.Select(x => x.PlayerId), Is.EqualTo(finished), "track board is fastest first");
            Assert.That(a.LeaderboardRows.All(x => x.Seconds > 0f));
            Assert.That(a.Profile.BestTrackIds, Does.Contain(h.Content.TrackIds[0]).Or.Empty);
            Assert.That(a.Profile.AchievementsTotal, Is.GreaterThan(0));
            Assert.That(a.Profile.TopRivalries.Length, Is.EqualTo(1));
        }
    }
}
