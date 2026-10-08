using System.Collections.Generic;
using System.Linq;
using NaijaKart.Core.Config;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Server.Config;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    public class ConfigFileTests
    {
        [Test]
        public void ShippedConfigLoadsAndValidates()
        {
            var json = new JsonConfigSource(ServerHarness.ConfigDir());
            Assert.That(ConfigValidator.Validate(json), Is.Empty);
            Assert.That(json.Items.items.Length, Is.GreaterThanOrEqualTo(10), "PRD §26: ten initial items (+ UI extras)");
            foreach (var id in new[] { "jollof_boost", "suya_burst", "generator_shield", "no_wahala", "pure_water", "egg", "oil", "danfo", "okada", "sharp_guy", "spike_strip", "banana" })
                Assert.That(json.Items.items.Any(i => i.id == id), id);
            Assert.That(json.Vehicles.vehicles.Length, Is.EqualTo(10), "PRD §20: ten vehicles");
            Assert.That(json.Characters.characters.Length, Is.EqualTo(8), "PRD §21: eight characters");
            Assert.That(json.TrackIds, Does.Contain("third_mainland_rush"));
            Assert.That(json.Game.progression.ranks.Length, Is.EqualTo(7), "design system: seven tiers with three divisions (ADR-0010 supersedes PRD §7 names)");
            Assert.That(json.Game.progression.ranks[5].displayName, Is.EqualTo("Chairman"));
            Assert.That(json.Game.lastma.fineAmount, Is.EqualTo(200), "PULL OVER design: 200 Coins (configurable)");
        }

        [Test]
        public void ThirdMainlandRushIsRaceable()
        {
            var json = new JsonConfigSource(ServerHarness.ConfigDir());
            var t = json.GetTrack("third_mainland_rush");
            var geo = new Core.Track.TrackGeometry(t);
            Assert.That(geo.LapLength, Is.GreaterThan(1000f).And.LessThan(3000f));
            Assert.That(t.checkpoints.Length, Is.GreaterThanOrEqualTo(8));
            bool hasShortcut = t.checkpoints.Any(c => c.gates.Any(g => g.isShortcut));
            Assert.That(hasShortcut, Is.True, "PRD §47: every track has a risk route");
            Assert.That(t.itemBoxes.Length, Is.GreaterThanOrEqualTo(6));
            // Every checkpoint gate must be on a drivable surface (main road or a shortcut road).
            foreach (var cp in t.checkpoints)
                foreach (var g in cp.gates)
                    Assert.That(geo.IsOnAnyRoad(g.position), Is.True, cp.id + "/" + g.label);
            Assert.That(t.shortcutRoads.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(geo.IsOnAnyRoad(new Core.Math.Vec3(240, 0, 510)), Is.True, "under-bridge shortcut is drivable");
            Assert.That(geo.IsOnAnyRoad(new Core.Math.Vec3(260, 0, 510)), Is.False, "but the gap between is not");
        }
    }

    public class GameServerTests
    {
        [Test]
        public void HelloCreatesAccountAndWelcome()
        {
            var h = new ServerHarness();
            var c = h.NewClient("tife");
            c.Connect("Tife");
            h.Run(0.1f, c);
            Assert.That(c.Welcome, Is.Not.Null);
            Assert.That(c.Welcome.Amount, Is.EqualTo(h.Content.Game.economy.startingBalance));
            Assert.That(c.Welcome.Text, Is.EqualTo("jjc"));
            Assert.That(h.Server.Profiles.Get("tife").DisplayName, Is.EqualTo("Tife"));
        }

        [Test]
        public void MessagesBeforeHelloAreRejected()
        {
            var h = new ServerHarness();
            var c = h.NewClient();
            c.Transport.Connect();
            c.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue });
            h.Run(0.1f, c);
            Assert.That(c.Errors, Has.Some.Contains("Hello"));
        }

        [Test]
        public void PrivateRoomFullRaceOverTheWire()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 2; g.lastma.enabled = true; });
            var host = h.NewClient("host", 0.8f);
            var guest = h.NewClient("guest", 0.6f);
            host.Connect(); guest.Connect();
            h.Run(0.1f, host, guest);
            host.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 2, VehicleId = "danfo", CharacterId = "tunde" });
            h.Run(0.1f, host, guest);
            Assert.That(host.Room, Is.Not.Null);
            Assert.That(host.Room.HostPlayerId, Is.EqualTo("host"));
            string code = host.Room.RoomCode;
            Assert.That(code, Does.Match("^[A-Z]{3}-[0-9]{3}$"), "EKO-427 style room code");

            guest.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = code.ToLowerInvariant(), VehicleId = "keke" });
            h.Run(0.1f, host, guest);
            Assert.That(guest.Room.Members.Length, Is.EqualTo(2));
            Assert.That(guest.Room.Members.First(m => m.PlayerId == "guest").VehicleId, Is.EqualTo("keke"));

            guest.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = true });
            h.Run(0.1f, host, guest);
            Assert.That(guest.Errors, Has.Some.Contains("host"), "only the host starts");

            host.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = true, ItemsEnabled = true, LastmaEnabled = true, Laps = 2 });
            h.Run(0.2f, host, guest);
            Assert.That(host.Room.State, Is.EqualTo(RaceState.Countdown));
            Assert.That(host.Room.Members.Length, Is.EqualTo(8), "bots filled the grid on request");

            bool done = h.RunUntil(() => host.Results != null && guest.Results != null, 500f, host, guest);
            Assert.That(done, Is.True, "race must reach results");
            Assert.That(host.SnapshotsReceived, Is.GreaterThan(100));
            Assert.That(host.Results.Entries.Count, Is.EqualTo(8));
            Assert.That(host.Results.Entries.Count(e => e.Finished), Is.GreaterThanOrEqualTo(2));
            Assert.That(host.Results.Entries.Count(e => !e.IsBot), Is.EqualTo(2));
            Assert.That(host.Rewards.Length, Is.EqualTo(2), "only humans are settled");
            var hostReward = host.Rewards.First(r => r.PlayerId == "host");
            Assert.That(hostReward.Xp, Is.GreaterThan(0));
            Assert.That(hostReward.CoinBalance, Is.GreaterThanOrEqualTo(h.Content.Game.economy.startingBalance - h.Content.Game.lastma.fineAmount));
            Assert.That(h.Server.Profiles.Get("host").Races, Is.EqualTo(1));
            Assert.That(host.Events.Any(e => e.Type == RaceEventType.RaceStarted));
            Assert.That(host.Events.Any(e => e.Type == RaceEventType.PlayerFinished));
            Assert.That(h.Server.Rivalries.GetOrCreate("host", "guest").TotalRaces, Is.EqualTo(1));
        }

        [Test]
        public void QuickRaceMatchmakingStartsAfterWait()
        {
            var h = new ServerHarness(g => { g.raceRules.matchmakingWaitSeconds = 2f; g.raceRules.minPlayersToStart = 2; });
            var a = h.NewClient("a"); var b = h.NewClient("b");
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.QuickRace });
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.QuickRace });
            h.Run(1f, a, b);
            Assert.That(a.Room, Is.Null, "not enough players yet, waiting");
            h.Run(1.5f, a, b);
            Assert.That(a.Room, Is.Not.Null);
            Assert.That(a.Room.Mode, Is.EqualTo(RaceMode.QuickRace));
            Assert.That(a.Room.Members.Length, Is.EqualTo(2));
            Assert.That(h.RunUntil(() => a.Room.State == RaceState.Countdown || a.Room.State == RaceState.Racing, 5f, a, b), Is.True);
        }

        [Test]
        public void RankedMatchmakingFillsAtEightAndSettlesRating()
        {
            var h = new ServerHarness(g => { g.accounts.rankedRequiresAccount = false; g.raceRules.defaultLaps = 1; g.lastma.enabled = false; });
            var clients = new List<TestClient>();
            for (int i = 0; i < 8; i++) { var c = h.NewClient("r" + i, 0.5f + 0.05f * i); c.Connect(); clients.Add(c); }
            var arr = clients.ToArray();
            h.Run(0.1f, arr);
            foreach (var c in clients) c.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.Ranked });
            h.Run(0.2f, arr);
            Assert.That(clients.All(c => c.Room != null), "full grid starts immediately");
            Assert.That(h.RunUntil(() => clients.All(c => c.Results != null), 400f, arr), Is.True);
            var rewards = clients[0].Rewards;
            Assert.That(rewards.Length, Is.EqualTo(8));
            var winner = clients[0].Results.Entries[0].PlayerId;
            Assert.That(rewards.First(r => r.PlayerId == winner).RatingDelta, Is.GreaterThan(0));
            Assert.That(rewards.Sum(r => r.RatingDelta), Is.InRange(-8, 8));
            // Ranked Points: the displayed ladder (ADR-0010). Winner +24, last -12 but never below 0.
            Assert.That(rewards.First(r => r.PlayerId == winner).RpDelta, Is.EqualTo(h.Content.Game.ranked.rpByPosition[0]));
            Assert.That(rewards.First(r => r.PlayerId == winner).RpAfter, Is.EqualTo(h.Content.Game.ranked.rpByPosition[0]));
            var last = clients[0].Results.Entries.Last(e => !e.IsBot).PlayerId;
            Assert.That(rewards.First(r => r.PlayerId == last).RpDelta, Is.LessThan(0));
            Assert.That(rewards.First(r => r.PlayerId == last).RpAfter, Is.EqualTo(0));
            clients[0].Send(new ClientEnvelope { Kind = ClientMessageKind.GetProfile });
            clients[0].Send(new ClientEnvelope { Kind = ClientMessageKind.GetSeason });
            h.Run(0.2f, arr);
            Assert.That(clients[0].Profile.RecentPositions.Length, Is.EqualTo(1), "last 5 races list");
            Assert.That(clients[0].Profile.RankLabel, Does.StartWith("JJC"));
            Assert.That(clients[0].Season.DaysLeft, Is.GreaterThanOrEqualTo(0));
            Assert.That(clients[0].Season.RpForWin, Is.EqualTo(24));
            Assert.That(clients[0].Season.MinRealPlayers, Is.EqualTo(h.Content.Game.ranked.minRealPlayers));
        }

        [Test]
        public void DisconnectMidRaceThenReconnectKeepsPlayerInRace()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.raceRules.reconnectWindowSeconds = 15f; g.lastma.enabled = false; });
            var a = h.NewClient("a", 0.8f); var b = h.NewClient("b", 0.8f);
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => b.LastSnapshot != null && b.LastSnapshot.State == RaceState.Racing && b.LastSnapshot.Time > 3f, 20f, a, b));

            // b drops its connection for 2 seconds.
            b.Transport.Disconnect();
            h.Run(2f, a, b);
            var room = h.Server.GetRoom(a.Room.RoomCode);
            Assert.That(room.Race.Find("b").Status, Is.EqualTo(ParticipantStatus.Disconnected));
            Assert.That(a.Events.Any(e => e.Type == RaceEventType.PlayerDisconnected && e.PlayerId == "b"));

            // Reconnect with a new connection and the same identity.
            var b2 = new TestClient("b", h.Hub.CreateClient("b-second"), h.Track, 99, 0.8f);
            b2.Connect();
            h.Run(0.2f, a, b2);
            Assert.That(room.Race.Find("b").Status, Is.EqualTo(ParticipantStatus.Connected));
            Assert.That(b2.Room, Is.Not.Null, "reconnect restores room state");
            Assert.That(h.RunUntil(() => a.Results != null, 300f, a, b2), Is.True);
            Assert.That(a.Results.For("b").Finished, Is.True);
        }

        [Test]
        public void DisconnectBeyondWindowIsDnfAndResultsAreIntact()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.raceRules.reconnectWindowSeconds = 1f; g.lastma.enabled = false; g.raceRules.finishGraceSeconds = 5f; });
            var a = h.NewClient("a", 0.8f); var b = h.NewClient("b", 0.8f);
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => b.LastSnapshot != null && b.LastSnapshot.Time > 3f, 20f, a, b));
            b.Transport.Disconnect();
            Assert.That(h.RunUntil(() => a.Results != null, 300f, a), Is.True);
            Assert.That(a.Results.For("a").Finished, Is.True);
            Assert.That(a.Results.For("b").Status, Is.EqualTo(ParticipantStatus.DidNotFinish));
            Assert.That(a.Results.For("a").FinishPosition, Is.EqualTo(1));
            Assert.That(h.Server.Profiles.Get("a").Wins, Is.EqualTo(1));
        }

        [Test]
        public void LastmaBailWorksAcrossTheNetwork()
        {
            var h = new ServerHarness(g =>
            {
                g.raceRules.defaultLaps = 3;
                g.lastma.firstTriggerMinSeconds = 2f;
                g.lastma.minIntervalSeconds = 1f;
                g.lastma.maxIntervalSeconds = 2f;
                g.lastma.warningSeconds = 0.5f;
                g.lastma.pursuitSeconds = 2f;
                g.lastma.pressureGainPerSecond = 2f; // guaranteed catch for the test
                g.lastma.fineDecisionSeconds = 10f;
                g.lastma.positionWeights = new[] { 1f };
                g.roadEvents.enabled = false;
            });
            var broke = h.NewClient("broke", 0.9f);
            var friend = h.NewClient("friend", 0.5f);
            broke.AutoPayFine = false;        // will call for bail
            broke.PullOverChoice = "bail";
            friend.AutoBailOthers = true;      // pays when asked
            broke.Connect(); friend.Connect();
            h.Run(0.1f, broke, friend);
            // Make "broke" unable to pay, and give "friend" enough to cover their own fines plus bail.
            h.Server.Ledger.Debit("broke", h.Server.Ledger.GetBalance("broke") - 100, "test", "drain", out _);
            h.Server.Ledger.Credit("friend", 20000, "test", "rich", out _);
            long friendBefore = h.Server.Ledger.GetBalance("friend");
            broke.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 3 });
            h.Run(0.1f, broke, friend);
            friend.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = broke.Room.RoomCode });
            h.Run(0.1f, broke, friend);
            broke.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });

            Assert.That(h.RunUntil(() => friend.BailRequests.Count > 0, 60f, broke, friend), Is.True, "friend receives 'needs bail'");
            Assert.That(friend.BailRequests[0].PlayerId, Is.EqualTo("broke"));
            Assert.That(friend.BailRequests[0].Amount, Is.EqualTo(h.Content.Game.lastma.fineAmount));
            Assert.That(h.RunUntil(() => broke.Events.Any(e => e.Type == RaceEventType.LastmaBailed), 10f, broke, friend), Is.True);
            var bailed = broke.Events.First(e => e.Type == RaceEventType.LastmaBailed);
            Assert.That(bailed.TargetPlayerId, Is.EqualTo("friend"));
            long fine = h.Content.Game.lastma.fineAmount;
            long friendPaid = friendBefore - h.Server.Ledger.GetBalance("friend");
            Assert.That(friendPaid, Is.GreaterThanOrEqualTo(fine), "friend paid the bail (and possibly their own fine)");
            Assert.That(friendPaid % fine, Is.EqualTo(0));
            Assert.That(h.Server.Ledger.GetBalance("broke"), Is.EqualTo(100), "the bailed player pays nothing");
            Assert.That(h.Server.Ledger.History("friend").Any(t => t.Source == "lastma_bail"), Is.True);
            Assert.That(broke.Errors, Is.Empty);
        }

        [Test]
        public void ArrestEliminatesAndRaceStillFinishes()
        {
            var h = new ServerHarness(g =>
            {
                g.raceRules.defaultLaps = 1;
                g.lastma.arrestEnabled = true;
                g.lastma.firstTriggerMinSeconds = 2f;
                g.lastma.warningSeconds = 0.5f;
                g.lastma.pursuitSeconds = 2f;
                g.lastma.pressureGainPerSecond = 2f;
                g.lastma.fineDecisionSeconds = 1f;
                g.lastma.positionWeights = new[] { 1f };
                g.roadEvents.enabled = false;
            });
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.6f);
            a.AutoPayFine = false;
            a.PullOverChoice = null; // never answers: arrest when enabled
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            h.Server.Ledger.Debit("a", h.Server.Ledger.GetBalance("a") - 1, "test", "drain", out _);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1, LastmaEnabled = true });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => a.Results != null, 300f, a, b), Is.True);
            var eliminated = a.Results.Entries.FirstOrDefault(e => e.Status == ParticipantStatus.Eliminated);
            Assert.That(eliminated, Is.Not.Null, "someone got locked up");
            Assert.That(a.Events.Any(e => e.Type == RaceEventType.LastmaArrested));
            Assert.That(a.Results.Entries.Any(e => e.Finished), "race still completes");
        }

        [Test]
        public void RematchCreatesANewRaceInTheSameRoom()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.lastma.enabled = false; g.roadEvents.enabled = false; });
            var a = h.NewClient("a", 0.8f); var b = h.NewClient("b", 0.8f);
            a.AutoVoteRematch = true; b.AutoVoteRematch = true;
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => a.Results != null, 300f, a, b), Is.True);
            string firstRace = a.Results.RaceId;
            Assert.That(h.RunUntil(() => a.Room.RaceId != firstRace && a.Room.State == RaceState.Countdown, 10f, a, b), Is.True, "RUN AM BACK");
            Assert.That(a.Room.Members.Length, Is.EqualTo(2));
            var r = a.Results; a.GetType();
            Assert.That(h.RunUntil(() => a.Results != null && a.Results.RaceId != firstRace, 300f, a, b), Is.True, "second race completes");
            Assert.That(h.Server.Profiles.Get("a").Races, Is.EqualTo(2));
        }

        [Test]
        public void RaceSurvivesLatencyAndPacketLoss()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.lastma.enabled = false; });
            h.Hub.Conditions.LatencySeconds = 0.15f;
            h.Hub.Conditions.JitterSeconds = 0.05f;
            h.Hub.Conditions.PacketLoss = 0.1f;
            var a = h.NewClient("a", 0.8f); var b = h.NewClient("b", 0.8f);
            a.Connect(); b.Connect();
            // Intents are unreliable on a lossy link: a real client retries until it sees the effect.
            Assert.That(h.RunUntil(() => a.Welcome != null && b.Welcome != null, 10f, a, b), Is.True);
            Assert.That(h.RunUntil(() => { if (a.Room == null) a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 1 }); return a.Room != null; }, 10f, a, b), Is.True);
            Assert.That(h.RunUntil(() => { if (b.Room == null) b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode }); return b.Room != null; }, 10f, a, b), Is.True);
            Assert.That(h.RunUntil(() => { if (a.Room.State == RaceState.Lobby) a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = true }); return a.Room.State != RaceState.Lobby; }, 10f, a, b), Is.True);
            Assert.That(h.RunUntil(() => a.Results != null && b.Results != null, 400f, a, b), Is.True);
            Assert.That(a.Results.RaceId, Is.EqualTo(b.Results.RaceId));
            Assert.That(a.Results.Entries[0].PlayerId, Is.EqualTo(b.Results.Entries[0].PlayerId), "both clients see the same authoritative order");
        }

        [Test]
        public void EmptyRoomsAreClosed()
        {
            var h = new ServerHarness();
            var a = h.NewClient("a");
            a.Connect();
            h.Run(0.1f, a);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom });
            h.Run(0.1f, a);
            Assert.That(h.Server.RoomCount, Is.EqualTo(1));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.LeaveRoom });
            h.Run(0.1f, a);
            Assert.That(h.Server.RoomCount, Is.EqualTo(0));
        }
    }
}
