using System.Linq;
using System.Text.RegularExpressions;
using NaijaKart.Core.Config;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    /// <summary>Private rooms, invites, matchmaking status and reconnect stand-ins (design screens 03.2, 03.3, 04, 05, 11).</summary>
    public class RoomsServerTests
    {
        [Test]
        public void PrivateRoomHasASpeakableCodeHostOptionsAndInvites()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.roadEvents.enabled = false; g.lastma.enabled = false; g.items.rollDurationSeconds = 0.1f; });
            var host = h.NewClient("tife"); var chuks = h.NewClient("chuks"); var bisi = h.NewClient("bisi");
            host.Connect(); chuks.Connect(); bisi.Connect(); h.Run(0.1f, host, chuks, bisi);

            host.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 2 });
            h.Run(0.1f, host, chuks, bisi);
            Assert.That(host.Room.RoomCode, Does.Match("^[A-Z]{3}-[0-9]{3}$"), "EKO-427 style code");
            Assert.That(host.Room.ShareUrl, Does.EndWith(host.Room.RoomCode));
            Assert.That(host.Room.Members[0].Status, Is.EqualTo("Host"));
            Assert.That(host.Room.StartsInSeconds, Is.EqualTo(-1), "the host decides when a private room starts");

            host.Send(new ClientEnvelope { Kind = ClientMessageKind.InviteFriend, TargetPlayerId = "chuks" });
            h.Run(0.1f, host, chuks, bisi);
            Assert.That(chuks.Invite, Is.Not.Null, "the friend gets the invite");
            Assert.That(chuks.Invite.Room.RoomCode, Is.EqualTo(host.Room.RoomCode));
            Assert.That(chuks.Invite.Text, Is.EqualTo("tife"));
            Assert.That(host.Room.Invited.Select(i => i.PlayerId), Does.Contain("chuks"));

            // Non-hosts cannot change options; the host picks LASTMA Madness, boosts only and AI fill.
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = host.Room.RoomCode.ToLowerInvariant().Replace("-", " ") });
            h.Run(0.1f, host, chuks, bisi);
            Assert.That(chuks.Room, Is.Not.Null, "codes are forgiving: lower case, no dash");
            Assert.That(host.Room.Invited, Is.Empty, "joining clears the invite");
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.SetRoomOptions, LastmaMode = "Madness" });
            h.Run(0.1f, host, chuks, bisi);
            Assert.That(chuks.Errors, Has.Some.Contains("host"));
            host.Send(new ClientEnvelope { Kind = ClientMessageKind.SetRoomOptions, Laps = 1, LastmaMode = "Madness", ItemsMode = "BoostsOnly", FillWithAi = true });
            h.Run(0.1f, host, chuks, bisi);
            Assert.That(host.Room.LastmaMode, Is.EqualTo("Madness"));
            Assert.That(host.Room.ItemsMode, Is.EqualTo("BoostsOnly"));
            Assert.That(host.Room.FillWithAi, Is.True);
            Assert.That(host.Room.Laps, Is.EqualTo(1));
            var room = h.Server.GetRoom(host.Room.RoomCode);
            Assert.That(room.Race.Setup.LastmaIntervalMultiplier, Is.EqualTo(h.Content.Game.social.lastmaMadnessIntervalMultiplier).Within(0.001f));
            Assert.That(room.Race.Setup.AllowedItemIds, Is.Not.Null.And.Not.Empty);
            Assert.That(room.Race.Setup.AllowedItemIds.All(id => h.Content.Items.items.First(i => i.id == id).category == ItemCategory.Boost));

            host.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            h.Run(0.3f, host, chuks, bisi);
            Assert.That(host.Room.AiSeats, Is.EqualTo(h.Content.Game.simulation.maxPlayersPerRace - 2), "empty seats filled by AI");
            Assert.That(host.Room.Members.Count(m => m.IsBot && m.Status == "AI"), Is.EqualTo(host.Room.AiSeats));
            Assert.That(h.RunUntil(() => host.Events.Any(e => e.Type == RaceEventType.ItemGranted), 90f, host, chuks), Is.True);
            foreach (var e in host.Events.Where(e => e.Type == RaceEventType.ItemGranted))
                Assert.That(h.Content.Items.items.First(i => i.id == e.Payload).category, Is.EqualTo(ItemCategory.Boost), "boosts only");
        }

        [Test]
        public void MatchmakingReportsFoundCountAndAiCountdown()
        {
            var h = new ServerHarness(g => { g.raceRules.matchmakingWaitSeconds = 10f; });
            var a = h.NewClient("a"); var b = h.NewClient("b");
            a.Connect(); b.Connect(); h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.QuickRace });
            h.Run(1.2f, a, b);
            Assert.That(a.QueueStatus.QueueFound, Is.EqualTo(1));
            Assert.That(a.QueueStatus.QueueMax, Is.EqualTo(h.Content.Game.simulation.maxPlayersPerRace));
            Assert.That(a.QueueStatus.QueueSecondsToAi, Is.InRange(7, 10));
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.QuickRace });
            h.Run(1.2f, a, b);
            Assert.That(b.QueueStatus.QueueFound, Is.EqualTo(2));
            Assert.That(h.RunUntil(() => a.Room != null && b.Room != null, 12f, a, b), Is.True, "after the wait the race starts");
            Assert.That(a.Room.StartsInSeconds, Is.GreaterThanOrEqualTo(0).Or.EqualTo(-1));
        }

        [Test]
        public void AiStandInDrivesADisconnectedKartUntilTheRacerReturns()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.raceRules.reconnectWindowSeconds = 15f; g.lastma.enabled = false; g.roadEvents.enabled = false; });
            var a = h.NewClient("a", 0.8f); var b = h.NewClient("b", 0.8f);
            a.Connect(); b.Connect(); h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            Assert.That(a.Room.ReconnectAttempts, Is.EqualTo(h.Content.Game.raceRules.reconnectAttempts));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => b.LastSnapshot != null && b.LastSnapshot.State == RaceState.Racing && b.LastSnapshot.Time > 3f, 20f, a, b));

            b.Transport.Disconnect();
            h.Run(0.5f, a, b);
            var room = h.Server.GetRoom(a.Room.RoomCode);
            var kart = room.Race.Find("b");
            Assert.That(kart.Status, Is.EqualTo(ParticipantStatus.Disconnected));
            var before = kart.State.Position;
            h.Run(3f, a, b);
            Assert.That(kart.Status, Is.EqualTo(ParticipantStatus.Disconnected), "still away, not yet DNF");
            Assert.That(kart.State.Speed, Is.GreaterThan(2f), "the AI stand-in keeps the kart racing");
            Assert.That((kart.State.Position - before).FlatMagnitude, Is.GreaterThan(10f), "and it makes progress");
            Assert.That(a.Room.Members.First(m => m.PlayerId == "b").Status, Is.EqualTo("AiDriving"));

            var b2 = new TestClient("b", h.Hub.CreateClient("b-second"), h.Track, 99, 0.8f);
            b2.Connect();
            Assert.That(h.RunUntil(() => room.Race.Find("b").Status == ParticipantStatus.Connected, 5f, a, b2), Is.True, "the racer takes over again");
            h.Run(0.2f, a, b2);
            Assert.That(a.Room.Members.First(m => m.PlayerId == "b").Status, Is.Not.EqualTo("AiDriving"));
        }
    }
}
