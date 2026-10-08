using System.Linq;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Net;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    /// <summary>Garage style, shop and season pass (design 13.2, 18, 19): looks only, never speed.</summary>
    public class ShopServerTests
    {
        [Test]
        public void ShopSellsLooksForCoinsOrPremiumAndEquipsThem()
        {
            var h = new ServerHarness();
            var a = h.NewClient("tife");
            a.Connect(); h.Run(0.1f, a);
            h.Server.Ledger.Credit("tife", Currency.Premium, 300, "test", "test:p:tife", out _);

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetShop });
            h.Run(0.1f, a);
            var shop = a.Shop.Cosmetics;
            Assert.That(shop.Single(c => c.Id == "color_danfo_yellow").State, Is.EqualTo("Equipped"), "the free starter colour is the default look");
            Assert.That(shop.Single(c => c.Id == "color_eko_red").State, Is.EqualTo("Coins"));
            Assert.That(shop.Single(c => c.Id == "color_owambe_gold").State, Is.EqualTo("Premium"));
            Assert.That(shop.Single(c => c.Id == "color_chrome").State, Is.EqualTo("Level"));
            Assert.That(shop.Single(c => c.Id == "rims_fire").State, Is.EqualTo("Pass"));
            var featured = shop.Single(c => c.Featured);
            Assert.That(featured.Id, Is.EqualTo("skin_molue_danfo"));
            Assert.That(featured.FeaturedDaysLeft, Is.GreaterThan(0));

            long coins = a.Shop.Amount;
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseCosmetic, Text = "color_eko_red" });
            h.Run(0.1f, a);
            Assert.That(a.Shop.Amount, Is.EqualTo(coins - 1200));
            Assert.That(a.Shop.Cosmetics.Single(c => c.Id == "color_eko_red").State, Is.EqualTo("Equipped"), "bought looks are worn at once");
            Assert.That(a.Shop.Cosmetics.Single(c => c.Id == "color_danfo_yellow").State, Is.EqualTo("Owned"));

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseCosmetic, Text = "color_owambe_gold" });
            h.Run(0.1f, a);
            Assert.That(a.Shop.PremiumBalance, Is.EqualTo(100), "premium currency buys looks");
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseCosmetic, Text = "skin_molue_danfo" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Last(), Does.Contain("Not enough P"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseCosmetic, Text = "color_chrome" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Last(), Does.Contain("level 15"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.PurchaseCosmetic, Text = "rims_fire" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Last(), Does.Contain("Season Pass"));

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.EquipCosmetic, Text = "color_danfo_yellow" });
            h.Run(0.1f, a);
            Assert.That(a.Shop.Cosmetics.Single(c => c.Id == "color_danfo_yellow").State, Is.EqualTo("Equipped"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.EquipCosmetic, Text = "rims_gold_spokes" });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Last(), Does.Contain("own"));
        }

        [Test]
        public void SeasonPassTiersUnlockWithSeasonXpAndPremiumTrackNeedsThePass()
        {
            var h = new ServerHarness();
            var a = h.NewClient("tife");
            a.Connect(); h.Run(0.1f, a);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetSeasonPass });
            h.Run(0.1f, a);
            Assert.That(a.Pass.Tiers.Length, Is.EqualTo(6));
            Assert.That(a.Pass.CurrentTier, Is.EqualTo(0));
            Assert.That(a.Pass.Tiers[0].FreeState, Is.EqualTo("Locked"));
            Assert.That(a.Pass.DaysLeft, Is.GreaterThanOrEqualTo(0));

            // Season XP from races (simulated here) opens tiers.
            var p = h.Server.Profiles.Get("tife"); p.SeasonXp = 2100; h.Server.Profiles.Save(p);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetSeasonPass });
            h.Run(0.1f, a);
            Assert.That(a.Pass.CurrentTier, Is.EqualTo(2));
            Assert.That(a.Pass.Tiers[0].FreeState, Is.EqualTo("Claimable"));
            Assert.That(a.Pass.Tiers[1].FreeState, Is.EqualTo("Claimable"));
            Assert.That(a.Pass.Tiers[2].FreeState, Is.EqualTo("Locked"));
            Assert.That(a.Pass.Tiers[0].PremiumState, Is.EqualTo("Locked"), "premium track needs the pass");

            long coins = h.Server.Ledger.GetBalance("tife");
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimPassTier, Laps = 1 });
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimPassTier, Laps = 2 });
            h.Run(0.1f, a);
            Assert.That(h.Server.Ledger.GetBalance("tife"), Is.EqualTo(coins + 300));
            Assert.That(a.Pass.Tiers[0].FreeState, Is.EqualTo("Claimed"));
            Assert.That(h.Server.Profiles.Get("tife").OwnedCosmeticIds, Does.Contain("horn_season_free"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimPassTier, Laps = 1 });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Last(), Does.Contain("Already"));
            Assert.That(h.Server.Ledger.GetBalance("tife"), Is.EqualTo(coins + 300), "never paid twice");

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.BuyPremiumPass });
            h.Run(0.1f, a);
            Assert.That(a.Errors.Last(), Does.Contain("Not enough P"));
            h.Server.Ledger.Credit("tife", Currency.Premium, 600, "test", "test:p2:tife", out _);
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.BuyPremiumPass });
            h.Run(0.1f, a);
            Assert.That(a.Pass.PremiumOwned, Is.True);
            Assert.That(a.Pass.Tiers[0].PremiumState, Is.EqualTo("Claimable"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimPassTier, Laps = 1, Flag = true });
            h.Run(0.1f, a);
            Assert.That(h.Server.Profiles.Get("tife").OwnedCosmeticIds, Does.Contain("skin_molue_danfo"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetShop });
            h.Run(0.1f, a);
            Assert.That(a.Shop.Cosmetics.Single(c => c.Id == "skin_molue_danfo").Owned, Is.True);
        }
    }
}
