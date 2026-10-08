using NaijaKart.Core.Accounts;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NUnit.Framework;

namespace NaijaKart.Tests.EditMode
{
    /// <summary>Guest-first accounts (design screens 21–25, ADR-0009).</summary>
    public class AccountServiceTests
    {
        private long _now = 1_700_000_000_000L;
        private RecordingOtpSender _otp;
        private CoinLedger _ledger;
        private AccountService _svc;
        private AccountsConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            _cfg = new AccountsConfig();
            _otp = new RecordingOtpSender();
            _ledger = new CoinLedger(new InMemoryCoinStore(), new EconomyConfig(), () => _now);
            _svc = new AccountService(new InMemoryAccountStore(), _otp, _cfg, _ledger, () => _now, seed: 3);
            _ledger.EnsureAccount("guest1"); _ledger.EnsureAccount("guest2"); _ledger.EnsureAccount("owner");
        }

        [Test]
        public void NigerianNumbersNormaliseToE164()
        {
            Assert.That(_svc.NormalizePhone("0803 412 5567"), Is.EqualTo("+2348034125567"));
            Assert.That(_svc.NormalizePhone("+234 803 412 5567"), Is.EqualTo("+2348034125567"));
            Assert.That(_svc.NormalizePhone("8034125567"), Is.EqualTo("+2348034125567"));
            Assert.That(_svc.NormalizePhone("12"), Is.Null);
            Assert.That(_svc.NormalizePhone(""), Is.Null);
        }

        [Test]
        public void ClaimFlowPaysTheAccountBonusOnceAndUnlocksRanked()
        {
            Assert.That(_svc.RankedAllowed("guest1"), Is.False, "guests cannot play Ranked");
            long before = _ledger.GetBalance("guest1");
            Assert.That(_svc.StartClaim("guest1", "08034125567", OtpChannel.WhatsApp), Is.EqualTo(ClaimResult.CodeSent));
            Assert.That(_otp.Sent, Has.Count.EqualTo(1));
            Assert.That(_otp.Sent[0].Channel, Is.EqualTo(OtpChannel.WhatsApp));
            Assert.That(_otp.Sent[0].Code.Length, Is.EqualTo(_cfg.otpDigits));
            Assert.That(_svc.StartClaim("guest1", "08034125567", OtpChannel.Sms), Is.EqualTo(ClaimResult.TooSoon), "resend is rate limited");

            Assert.That(_svc.VerifyClaim("guest1", "000000", out _), Is.EqualTo(ClaimResult.WrongCode).Or.EqualTo(ClaimResult.Verified));
            var code = _otp.LastCodeFor("+2348034125567");
            Assert.That(_svc.VerifyClaim("guest1", code, out string signIn), Is.EqualTo(ClaimResult.Verified));
            Assert.That(signIn, Is.Null);
            var a = _svc.Get("guest1");
            Assert.That(a.Status, Is.EqualTo(AccountStatus.Claimed));
            Assert.That(a.Provider, Is.EqualTo("phone"));
            Assert.That(a.ReferralCode, Does.Match("^[A-Z]{2,4}-[A-Z2-9]{3}$"));
            Assert.That(_ledger.GetBalance("guest1"), Is.EqualTo(before + _cfg.accountBonusCoins));
            Assert.That(_svc.RankedAllowed("guest1"), Is.True);
            Assert.That(_svc.StartClaim("guest1", "08034125567", OtpChannel.Sms), Is.EqualTo(ClaimResult.AlreadyClaimed));
            Assert.That(_ledger.GetBalance("guest1"), Is.EqualTo(before + _cfg.accountBonusCoins), "bonus is paid once");
        }

        [Test]
        public void CodesExpireAndLockAfterTooManyAttempts()
        {
            _svc.StartClaim("guest1", "08034125567", OtpChannel.Sms);
            _now += (_cfg.otpTtlSeconds + 1) * 1000L;
            Assert.That(_svc.VerifyClaim("guest1", _otp.LastCodeFor("+2348034125567"), out _), Is.EqualTo(ClaimResult.Expired));
            Assert.That(_svc.VerifyClaim("guest1", "123456", out _), Is.EqualTo(ClaimResult.NoPendingClaim));

            _now += _cfg.otpResendSeconds * 1000L;
            _svc.StartClaim("guest1", "08034125567", OtpChannel.Sms);
            string real = _otp.LastCodeFor("+2348034125567");
            string wrong = real == "111111" ? "222222" : "111111";
            ClaimResult last = ClaimResult.WrongCode;
            for (int i = 0; i < _cfg.otpMaxAttempts; i++) last = _svc.VerifyClaim("guest1", wrong, out _);
            Assert.That(last, Is.EqualTo(ClaimResult.TooManyAttempts));
            Assert.That(_svc.VerifyClaim("guest1", real, out _), Is.EqualTo(ClaimResult.NoPendingClaim), "locked codes are discarded");
        }

        [Test]
        public void SamePhoneSignsInToTheExistingAccount()
        {
            _svc.StartClaim("owner", "08034125567", OtpChannel.Sms);
            _svc.VerifyClaim("owner", _otp.LastCodeFor("+2348034125567"), out _);
            _now += 60_000;
            _svc.StartClaim("guest2", "+2348034125567", OtpChannel.Sms);
            Assert.That(_svc.VerifyClaim("guest2", _otp.LastCodeFor("+2348034125567"), out string signIn), Is.EqualTo(ClaimResult.SignedInToExisting));
            Assert.That(signIn, Is.EqualTo("owner"));
            Assert.That(_svc.Get("guest2").Status, Is.EqualTo(AccountStatus.Guest), "the guest id is not merged");
        }

        [Test]
        public void RacerNamesAreValidatedAndUnique()
        {
            Assert.That(_svc.CheckName("Ti"), Is.EqualTo(NameStatus.Invalid));
            Assert.That(_svc.CheckName("Tife Speed"), Is.EqualTo(NameStatus.Invalid));
            Assert.That(_svc.CheckName("Tife_Eko"), Is.EqualTo(NameStatus.Available));
            Assert.That(_svc.CheckName("LASTMA"), Is.EqualTo(NameStatus.Taken), "reserved");
            Assert.That(_svc.SetRacer("guest1", "TifeSpeed", "tife", "Lagos", out _), Is.True);
            Assert.That(_svc.CheckName("tifespeed"), Is.EqualTo(NameStatus.Taken), "case-insensitive uniqueness");
            Assert.That(_svc.CheckName("tifespeed", forPlayerId: "guest1"), Is.EqualTo(NameStatus.Available), "your own name stays yours");
            Assert.That(_svc.SetRacer("guest2", "TIFESPEED", null, "Mars", out string err), Is.False);
            Assert.That(err, Is.Not.Empty);
            Assert.That(_svc.SetRacer("guest2", "Chuks", null, "Mars", out _), Is.True);
            Assert.That(_svc.Get("guest2").HomeCity, Is.EqualTo("Other"), "unknown cities fall back");
            Assert.That(_svc.Get("guest1").HomeCity, Is.EqualTo("Lagos"));
            Assert.That(_svc.Get("guest1").LookId, Is.EqualTo("tife"));
        }

        [Test]
        public void ReferralPaysBothSidesOnceAfterTheFirstRace()
        {
            _svc.StartClaim("owner", "08034125567", OtpChannel.Sms);
            _svc.VerifyClaim("owner", _otp.LastCodeFor("+2348034125567"), out _);
            string code = _svc.ReferralCodeFor("owner");
            Assert.That(_svc.ApplyReferral("owner", code, out string self), Is.False, "not your own code");
            Assert.That(_svc.ApplyReferral("guest1", "NOPE-123", out _), Is.False);
            Assert.That(_svc.ApplyReferral("guest1", code.ToLowerInvariant(), out _), Is.True);
            Assert.That(_svc.ApplyReferral("guest1", code, out _), Is.False, "one code per player");
            long g = _ledger.GetBalance("guest1"), o = _ledger.GetBalance("owner");
            Assert.That(_svc.RewardReferralAfterFirstRace("guest1", out string referrer), Is.True);
            Assert.That(referrer, Is.EqualTo("owner"));
            Assert.That(_ledger.GetBalance("guest1"), Is.EqualTo(g + _cfg.referralBonusCoins));
            Assert.That(_ledger.GetBalance("owner"), Is.EqualTo(o + _cfg.referralBonusCoins));
            Assert.That(_svc.RewardReferralAfterFirstRace("guest1", out _), Is.False, "paid once");
            Assert.That(_ledger.GetBalance("owner"), Is.EqualTo(o + _cfg.referralBonusCoins));
        }

        [Test]
        public void SocialSignInClaimsOrSignsIn()
        {
            Assert.That(_svc.ClaimWithProvider("guest1", "google", "sub-123", out _), Is.EqualTo(ClaimResult.Verified));
            Assert.That(_svc.ClaimWithProvider("guest2", "google", "sub-123", out string signIn), Is.EqualTo(ClaimResult.SignedInToExisting));
            Assert.That(signIn, Is.EqualTo("guest1"));
            Assert.That(AccountService.MaskPhone("+2348034125567"), Is.EqualTo("+234803 ••• 5567"));
        }
    }
}
