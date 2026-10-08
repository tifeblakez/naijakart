using System.Linq;
using NaijaKart.Core.Accounts;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    /// <summary>Save progress, racer identity and referrals over the wire (design screens 21–25, ADR-0009).</summary>
    public class AccountsServerTests
    {
        [Test]
        public void GuestSavesProgressWithAPhoneCodeThenPicksARacerName()
        {
            var h = new ServerHarness();
            var a = h.NewClient("guest_a");
            a.Connect(); h.Run(0.1f, a);
            long start = a.Welcome.Amount;

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.Ranked });
            h.Run(0.1f, a);
            Assert.That(a.Errors, Has.Some.Contains("Ranked"), "Ranked unlocks after saving");

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimStart, Text = "0803 412 5567", Flag = true });
            h.Run(0.1f, a);
            Assert.That(a.Account.Result, Is.EqualTo(ClaimResult.CodeSent.ToString()));
            Assert.That(a.Account.PhoneMasked, Is.EqualTo("+234803 ••• 5567"));
            Assert.That(a.Account.ResendInSeconds, Is.GreaterThan(0));
            var sender = (RecordingOtpSender)h.Server.OtpSender;
            Assert.That(sender.Sent.Last().Channel, Is.EqualTo(OtpChannel.WhatsApp));

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimVerify, Text = "999999" });
            h.Run(0.1f, a);
            Assert.That(a.Account.Result, Is.EqualTo(ClaimResult.WrongCode.ToString()).Or.EqualTo(ClaimResult.Verified.ToString()));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimVerify, Text = sender.LastCodeFor("+2348034125567") });
            h.Run(0.1f, a);
            Assert.That(a.Account.Status, Is.EqualTo("Claimed"));
            Assert.That(a.Account.RankedUnlocked, Is.True);
            Assert.That(a.LastBalance, Is.EqualTo(start + h.Content.Game.accounts.accountBonusCoins), "account bonus");
            Assert.That(a.Account.ReferralCode, Is.Not.Null.And.Not.Empty);

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.CheckName, Text = "TifeSpeed" });
            h.Run(0.1f, a);
            Assert.That(a.Account.NameStatus, Is.EqualTo("Available"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.SetRacer, DisplayName = "TifeSpeed", CharacterId = "tife", Text = "Lagos" });
            h.Run(0.1f, a);
            Assert.That(a.Account.RacerName, Is.EqualTo("TifeSpeed"));
            Assert.That(a.Account.HomeCity, Is.EqualTo("Lagos"));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetProfile });
            h.Run(0.1f, a);
            Assert.That(a.Profile.DisplayName, Is.EqualTo("TifeSpeed"), "the racer name is the display name everywhere");

            a.Errors.Clear();
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.JoinQueue, Mode = RaceMode.Ranked });
            h.Run(0.1f, a);
            Assert.That(a.Errors, Is.Empty, "Ranked is open now");
        }

        [Test]
        public void ReferralRewardsBothRacersAfterTheInvitedFriendsFirstRace()
        {
            var h = new ServerHarness(g => { g.raceRules.defaultLaps = 1; g.roadEvents.enabled = false; g.lastma.enabled = false; });
            var tife = h.NewClient("tife"); var chuks = h.NewClient("chuks");
            tife.Connect(); chuks.Connect(); h.Run(0.1f, tife, chuks);
            tife.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimWithProvider, Text = "google", AuthToken = "sub-tife" });
            h.Run(0.1f, tife, chuks);
            Assert.That(tife.Account.Status, Is.EqualTo("Claimed"));
            string code = tife.Account.ReferralCode;

            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.ApplyReferral, Text = code });
            h.Run(0.1f, tife, chuks);
            Assert.That(chuks.Account.ReferredBy, Is.EqualTo("tife"));
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.ApplyReferral, Text = code });
            h.Run(0.1f, tife, chuks);
            Assert.That(chuks.Errors, Has.Some.Contains("already"));

            long tifeBefore = h.Server.Ledger.GetBalance("tife");
            long chuksBefore = h.Server.Ledger.GetBalance("chuks");
            // Chuks' first race: a one-lap practice against bots.
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.CreateRoom, Mode = RaceMode.Practice, Laps = 1 });
            h.Run(0.1f, tife, chuks);
            chuks.Send(new ClientEnvelope { Kind = ClientMessageKind.StartRoom, Flag = true });
            Assert.That(h.RunUntil(() => chuks.Results != null, 240f, tife, chuks), Is.True, "race finishes");
            h.Run(0.5f, tife, chuks);
            long bonus = h.Content.Game.accounts.referralBonusCoins;
            Assert.That(h.Server.Ledger.GetBalance("tife"), Is.EqualTo(tifeBefore + bonus), "inviter paid");
            Assert.That(h.Server.Ledger.GetBalance("chuks") - chuksBefore, Is.GreaterThanOrEqualTo(bonus), "invited racer paid (plus race rewards)");
            Assert.That(tife.Account.Result, Is.EqualTo("ReferralRewarded"));
            Assert.That(h.Server.Accounts.Get("chuks").ReferralRewarded, Is.True);
        }

        [Test]
        public void SamePhoneOnAnotherDeviceSignsInToTheExistingAccount()
        {
            var h = new ServerHarness();
            var owner = h.NewClient("owner"); var other = h.NewClient("other_phone");
            owner.Connect(); other.Connect(); h.Run(0.1f, owner, other);
            var sender = (RecordingOtpSender)h.Server.OtpSender;
            owner.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimStart, Text = "08034125567" });
            h.Run(0.1f, owner, other);
            owner.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimVerify, Text = sender.LastCodeFor("+2348034125567") });
            h.Run(0.1f, owner, other);
            Assert.That(owner.Account.Status, Is.EqualTo("Claimed"));

            other.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimStart, Text = "+234 803 412 5567" });
            h.Run(0.1f, owner, other);
            other.Send(new ClientEnvelope { Kind = ClientMessageKind.ClaimVerify, Text = sender.LastCodeFor("+2348034125567") });
            h.Run(0.1f, owner, other);
            Assert.That(other.Account.Result, Is.EqualTo(ClaimResult.SignedInToExisting.ToString()));
            Assert.That(other.Account.SignInPlayerId, Is.EqualTo("owner"));
            Assert.That(other.Account.Status, Is.EqualTo("Guest"));
        }
    }
}
