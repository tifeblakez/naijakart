using System.Linq;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    /// <summary>Server behaviour behind the designed screens (ADR-0007).</summary>
    public class UiFlowServerTests
    {
        private static ServerHarness LastmaHarness(bool ranked) => new ServerHarness(g =>
        {
            g.raceRules.defaultLaps = 3;
            g.raceRules.matchmakingWaitSeconds = 1f;
            g.lastma.firstTriggerMinSeconds = 2f; g.lastma.minIntervalSeconds = 1f; g.lastma.maxIntervalSeconds = 2f;
            g.lastma.warningSeconds = 0.5f; g.lastma.pursuitSeconds = 2f; g.lastma.pressureGainPerSecond = 2f;
            g.lastma.positionWeights = new[] { 1f }; g.roadEvents.enabled = false;
            g.accounts.rankedRequiresAccount = false; g.ranked.minRealPlayers = 2;   // these tests race as guests, two of them
        });

        [Test]
        public void PullOverSendsOptionsAndPenaltyIsTheDefault()
        {
            var h = LastmaHarness(ranked: false);
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.5f);
            a.AutoPayFine = false; a.PullOverChoice = null;   // lets the timer run out
            b.AutoPayFine = false; b.PullOverChoice = "penalty";
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.AddFriend, TargetPlayerId = "b" });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom, Laps = 3 });
            h.Run(0.1f, a, b);
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinRoom, RoomCode = a.Room.RoomCode });
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom });
            Assert.That(h.RunUntil(() => a.LastOptions != null, 60f, a, b), Is.True, "caught racer receives the dialog data");
            var o = a.LastOptions;
            Assert.That(o.FineAmount, Is.EqualTo(h.Content.Game.lastma.fineAmount));
            Assert.That(o.FinesAllowed, Is.True);
            Assert.That(o.BailAllowed, Is.True);
            Assert.That(o.CanAffordFine, Is.True);
            Assert.That(o.FriendsOnline, Is.EqualTo(1), "b is a friend and online");
            Assert.That(o.Ranked, Is.False);
            Assert.That(h.RunUntil(() => a.Events.Any(e => e.Type == RaceEventType.LastmaPenaltyTaken && e.PlayerId == "a"), 10f, a, b), Is.True, "no choice = penalty");
            Assert.That(h.RunUntil(() => a.Events.Any(e => e.Type == RaceEventType.LastmaPenaltyServed && e.PlayerId == "a"), 10f, a, b), Is.True);
            Assert.That(a.Events.Any(e => e.Type == RaceEventType.LastmaArrested), Is.False, "nobody is locked up by default");
            Assert.That(h.Server.Ledger.GetBalance("a"), Is.EqualTo(h.Content.Game.economy.startingBalance), "penalty is free");
        }

        [Test]
        public void RankedEveryoneTakesTheSamePenalty()
        {
            var h = LastmaHarness(ranked: true);
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.5f);
            a.AutoPayFine = true; b.AutoPayFine = true; // they try to pay, the server refuses
            a.Connect(); b.Connect();
            h.Run(0.1f, a, b);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.Ranked });
            b.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.Ranked });
            Assert.That(h.RunUntil(() => a.Room != null && a.Room.Mode == RaceMode.Ranked, 5f, a, b), Is.True);
            Assert.That(h.RunUntil(() => a.LastOptions != null || b.LastOptions != null, 60f, a, b), Is.True);
            var o = a.LastOptions ?? b.LastOptions;
            Assert.That(o.Ranked, Is.True);
            Assert.That(o.FinesAllowed, Is.False);
            Assert.That(o.BailAllowed, Is.False);
            Assert.That(h.RunUntil(() => a.Events.Any(e => e.Type == RaceEventType.LastmaPenaltyTaken), 10f, a, b), Is.True);
            Assert.That(a.Events.Any(e => e.Type == RaceEventType.LastmaFinePaid), Is.False, "paying is refused in ranked");
            Assert.That(a.Errors.Concat(b.Errors).Any(e => e.Contains("fine")), "the refused PayFine is reported to whoever was caught");
        }

        [Test]
        public void GaragePurchaseAndOwnershipAreServerDecided()
        {
            var h = new ServerHarness();
            var a = h.NewClient("a");
            a.Connect();
            h.Run(0.1f, a);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetGarage });
            h.Run(0.1f, a);
            Assert.That(a.Garage, Is.Not.Null);
            var karts = a.Garage.Vehicles;
            Assert.That(karts.Length, Is.EqualTo(10));
            Assert.That(karts.First(k => k.Id == "danfo").Owned, Is.True, "starter");
            Assert.That(karts.First(k => k.Id == "keke").Owned, Is.True);
            var camry = karts.First(k => k.Id == "executive_sedan");
            Assert.That(camry.Owned, Is.False);
            Assert.That(camry.PriceCoins, Is.EqualTo(5000));
            var suv = karts.First(k => k.Id == "suv");
            Assert.That(suv.Owned, Is.False);
            Assert.That(suv.UnlockLevel, Is.EqualTo(12));
            Assert.That(suv.LevelReached, Is.False);

            // Cannot select a kart you do not own: the server falls back and reports it.
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom });
            h.Run(0.1f, a);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.SelectLoadout, VehicleId = "executive_sedan" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Any(e => e.Contains("own")), "unowned kart rejected");
            Assert.That(a.Room.Members[0].VehicleId, Is.Not.EqualTo("executive_sedan"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.LeaveRoom });
            h.Run(0.1f, a);

            // Too poor, then rich enough: purchase goes through the ledger with earned Coins.
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseVehicle, VehicleId = "executive_sedan" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Any(e => e.Contains("Not enough Coins")));
            h.Server.Ledger.Credit("a", 10000, "test", "rich", out _);
            h.Server.Ledger.Credit("a", Core.Economy.Currency.Premium, 350, "iap", "p", out _);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseVehicle, VehicleId = "executive_sedan" });
            h.Run(0.1f, a);
            Assert.That(a.Garage.Vehicles.First(k => k.Id == "executive_sedan").Owned, Is.True);
            Assert.That(a.Garage.Vehicles.First(k => k.Id == "executive_sedan").Selected, Is.True);
            Assert.That(a.Garage.Amount, Is.EqualTo(h.Content.Game.economy.startingBalance + 10000 - 5000));
            Assert.That(a.Garage.PremiumBalance, Is.EqualTo(350), "premium untouched: it never buys karts");
            Assert.That(h.Server.Ledger.History("a").Any(t => t.Source == "purchase:executive_sedan"));

            // Level-locked kart stays locked regardless of Coins.
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseVehicle, VehicleId = "suv" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Any(e => e.Contains("level 12")));

            // Replay of the same purchase is a no-op (already owned).
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseVehicle, VehicleId = "executive_sedan" });
            h.Run(0.1f, a);
            Assert.That(a.Garage.Amount, Is.EqualTo(h.Content.Game.economy.startingBalance + 10000 - 5000));

            // The bought kart is what the next room uses by default.
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.PrivateRoom });
            h.Run(0.1f, a);
            Assert.That(a.Room.Members[0].VehicleId, Is.EqualTo("executive_sedan"));
        }

        [Test]
        public void FourItemSlotsAndBoostChargesReachTheSnapshot()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.lastma.enabled = false; });
            var a = h.NewClient("a", 0.9f);
            a.Connect();
            h.Run(0.1f, a);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.Practice, Laps = 1 });
            h.Run(0.1f, a);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = true });
            Assert.That(h.RunUntil(() => a.LastSnapshot != null && a.LastSnapshot.State == RaceState.Racing, 10f, a), Is.True);
            var me = a.LastSnapshot.Participants.First(p => p.PlayerId == "a");
            Assert.That(me.HeldItemIds.Length, Is.EqualTo(h.Content.Game.items.inventorySlots));
            Assert.That(me.ItemsReady.Length, Is.EqualTo(h.Content.Game.items.inventorySlots));
            Assert.That(h.RunUntil(() => a.LastSnapshot.Participants.Any(p => p.BoostCharges > 0), 120f, a), Is.True, "bots drift and bank charges");
        }
    }
}
