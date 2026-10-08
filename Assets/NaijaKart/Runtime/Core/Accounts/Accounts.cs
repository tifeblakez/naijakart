using System;
using System.Collections.Generic;
using System.Text;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.Accounts
{
    /// <summary>
    /// Guest-first identity (design screens 21–25, ADR-0009): a player races as a guest and "saves
    /// progress" by claiming the account with a phone number (SMS or WhatsApp one-time code) or a
    /// social sign-in. Claiming unlocks Ranked, gives the account bonus, and issues a referral code.
    /// Rewards go through the ledger with idempotency keys, so nothing is paid twice.
    /// </summary>
    public enum AccountStatus { Guest = 0, Claimed = 1 }

    public enum OtpChannel { Sms = 0, WhatsApp = 1 }

    public enum NameStatus { Available, Taken, Invalid }

    public enum ClaimResult
    {
        CodeSent, InvalidPhone, TooSoon, AlreadyClaimed,
        Verified, WrongCode, Expired, TooManyAttempts, NoPendingClaim,
        /// <summary>The phone belongs to another account: the client should sign in as SignInPlayerId.</summary>
        SignedInToExisting
    }

    [Serializable]
    public sealed class AccountRecord
    {
        public string PlayerId;
        public AccountStatus Status = AccountStatus.Guest;
        /// <summary>"phone", "google", "apple".</summary>
        public string Provider;
        /// <summary>Phone in E.164 or the provider's subject id.</summary>
        public string ProviderSubject;
        public string RacerName;
        public string HomeCity;
        public string LookId;
        public string ReferralCode;
        public string ReferredBy;
        public bool ReferralRewarded;
        public long CreatedUnixMs;
        public long ClaimedUnixMs;
        // Pending one-time code (cleared on verify)
        public string PendingPhone;
        public OtpChannel PendingChannel;
        public string PendingCode;
        public long PendingExpiresUnixMs;
        public long PendingSentUnixMs;
        public int PendingAttempts;
    }

    public interface IAccountStore
    {
        AccountRecord Get(string playerId);
        void Save(AccountRecord account);
        AccountRecord FindByProvider(string provider, string subject);
        AccountRecord FindByName(string racerName);
        AccountRecord FindByReferralCode(string code);
    }

    public sealed class InMemoryAccountStore : IAccountStore
    {
        private readonly Dictionary<string, AccountRecord> _accounts = new Dictionary<string, AccountRecord>();

        public AccountRecord Get(string playerId) => _accounts.TryGetValue(playerId, out var a) ? a : null;
        public void Save(AccountRecord account) => _accounts[account.PlayerId] = account;
        public AccountRecord FindByProvider(string provider, string subject)
        {
            foreach (var a in _accounts.Values) if (a.Status == AccountStatus.Claimed && a.Provider == provider && a.ProviderSubject == subject) return a;
            return null;
        }
        public AccountRecord FindByName(string racerName)
        {
            foreach (var a in _accounts.Values) if (a.RacerName != null && string.Equals(a.RacerName, racerName, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }
        public AccountRecord FindByReferralCode(string code)
        {
            foreach (var a in _accounts.Values) if (a.ReferralCode != null && string.Equals(a.ReferralCode, code, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }
    }

    /// <summary>Delivers one-time codes. Production binds an SMS/WhatsApp provider; tests and dev record them.</summary>
    public interface IOtpSender
    {
        void Send(string phoneE164, string code, OtpChannel channel);
    }

    public sealed class RecordingOtpSender : IOtpSender
    {
        public readonly List<(string Phone, string Code, OtpChannel Channel)> Sent = new List<(string, string, OtpChannel)>();
        public string LastCodeFor(string phone) { for (int i = Sent.Count - 1; i >= 0; i--) if (Sent[i].Phone == phone) return Sent[i].Code; return null; }
        public void Send(string phoneE164, string code, OtpChannel channel) => Sent.Add((phoneE164, code, channel));
    }

    public sealed class AccountService
    {
        private readonly IAccountStore _store;
        private readonly IOtpSender _otp;
        private readonly AccountsConfig _cfg;
        private readonly CoinLedger _ledger;
        private readonly Func<long> _unixMs;
        private readonly DeterministicRandom _rng;

        public AccountService(IAccountStore store, IOtpSender otp, AccountsConfig cfg, CoinLedger ledger, Func<long> unixMsClock = null, ulong seed = 7)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _otp = otp ?? new RecordingOtpSender();
            _cfg = cfg ?? new AccountsConfig();
            _ledger = ledger;
            _unixMs = unixMsClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _rng = new DeterministicRandom(seed);
        }

        public AccountsConfig Config => _cfg;

        public AccountRecord Get(string playerId)
        {
            var a = _store.Get(playerId);
            if (a == null)
            {
                a = new AccountRecord { PlayerId = playerId, CreatedUnixMs = _unixMs() };
                _store.Save(a);
            }
            return a;
        }

        public bool IsClaimed(string playerId) => Get(playerId).Status == AccountStatus.Claimed;
        public bool RankedAllowed(string playerId) => !_cfg.rankedRequiresAccount || IsClaimed(playerId);

        // ---- phone claim ----

        /// <summary>Normalises a Nigerian or international number to E.164 (0803… → +234803…). Null when invalid.</summary>
        public string NormalizePhone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var digits = new StringBuilder();
            bool plus = raw.TrimStart().StartsWith("+");
            foreach (char ch in raw) if (char.IsDigit(ch)) digits.Append(ch);
            string d = digits.ToString();
            string cc = (_cfg.defaultCountryCode ?? "+234").TrimStart('+');
            if (!plus && d.Length == 11 && d[0] == '0') d = cc + d.Substring(1);
            else if (!plus && d.Length == 10 && d[0] != '0') d = cc + d;
            if (d.Length < 10 || d.Length > 15) return null;
            return "+" + d;
        }

        public ClaimResult StartClaim(string playerId, string rawPhone, OtpChannel channel)
        {
            var a = Get(playerId);
            if (a.Status == AccountStatus.Claimed) return ClaimResult.AlreadyClaimed;
            string phone = NormalizePhone(rawPhone);
            if (phone == null) return ClaimResult.InvalidPhone;
            long now = _unixMs();
            if (a.PendingPhone == phone && now - a.PendingSentUnixMs < _cfg.otpResendSeconds * 1000L) return ClaimResult.TooSoon;
            a.PendingPhone = phone;
            a.PendingChannel = channel;
            a.PendingCode = NewCode();
            a.PendingSentUnixMs = now;
            a.PendingExpiresUnixMs = now + _cfg.otpTtlSeconds * 1000L;
            a.PendingAttempts = 0;
            _store.Save(a);
            _otp.Send(phone, a.PendingCode, channel);
            return ClaimResult.CodeSent;
        }

        public int ResendInSeconds(string playerId)
        {
            var a = Get(playerId);
            if (a.PendingPhone == null) return 0;
            long left = _cfg.otpResendSeconds * 1000L - (_unixMs() - a.PendingSentUnixMs);
            return left <= 0 ? 0 : (int)System.Math.Ceiling(left / 1000.0);
        }

        /// <summary>Verifies the code. On success the guest becomes a claimed account (bonus paid) or,
        /// when the phone already owns an account, returns that account id to sign in to.</summary>
        public ClaimResult VerifyClaim(string playerId, string code, out string signInPlayerId)
        {
            signInPlayerId = null;
            var a = Get(playerId);
            if (a.Status == AccountStatus.Claimed) return ClaimResult.AlreadyClaimed;
            if (a.PendingCode == null) return ClaimResult.NoPendingClaim;
            long now = _unixMs();
            if (now > a.PendingExpiresUnixMs) { ClearPending(a); _store.Save(a); return ClaimResult.Expired; }
            if (a.PendingAttempts >= _cfg.otpMaxAttempts) { ClearPending(a); _store.Save(a); return ClaimResult.TooManyAttempts; }
            if ((code ?? "").Trim() != a.PendingCode)
            {
                a.PendingAttempts++;
                _store.Save(a);
                if (a.PendingAttempts >= _cfg.otpMaxAttempts) { ClearPending(a); _store.Save(a); return ClaimResult.TooManyAttempts; }
                return ClaimResult.WrongCode;
            }
            var existing = _store.FindByProvider("phone", a.PendingPhone);
            if (existing != null && existing.PlayerId != playerId)
            {
                // "Have an account? Sign in": the guest switches to the existing account. Guest progress stays
                // on the guest id (ADR-0009: no automatic merge).
                ClearPending(a); _store.Save(a);
                signInPlayerId = existing.PlayerId;
                return ClaimResult.SignedInToExisting;
            }
            return CompleteClaim(a, "phone", a.PendingPhone, now);
        }

        /// <summary>Social sign-in (Google/Apple): the backend verified the provider token and passes the subject.</summary>
        public ClaimResult ClaimWithProvider(string playerId, string provider, string subject, out string signInPlayerId)
        {
            signInPlayerId = null;
            var a = Get(playerId);
            if (a.Status == AccountStatus.Claimed) return ClaimResult.AlreadyClaimed;
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject)) return ClaimResult.InvalidPhone;
            var existing = _store.FindByProvider(provider, subject);
            if (existing != null && existing.PlayerId != playerId) { signInPlayerId = existing.PlayerId; return ClaimResult.SignedInToExisting; }
            return CompleteClaim(a, provider, subject, _unixMs());
        }

        private ClaimResult CompleteClaim(AccountRecord a, string provider, string subject, long now)
        {
            a.Status = AccountStatus.Claimed;
            a.Provider = provider;
            a.ProviderSubject = subject;
            a.ClaimedUnixMs = now;
            ClearPending(a);
            if (a.ReferralCode == null) a.ReferralCode = NewReferralCode(a);
            _store.Save(a);
            if (_ledger != null && _cfg.accountBonusCoins > 0)
                _ledger.Credit(a.PlayerId, _cfg.accountBonusCoins, "account_bonus", "account_bonus:" + a.PlayerId, out _);
            return ClaimResult.Verified;
        }

        private static void ClearPending(AccountRecord a)
        {
            a.PendingCode = null; a.PendingPhone = null; a.PendingAttempts = 0; a.PendingExpiresUnixMs = 0; a.PendingSentUnixMs = 0;
        }

        private string NewCode()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < System.Math.Max(4, _cfg.otpDigits); i++) sb.Append((char)('0' + _rng.Range(0, 10)));
            return sb.ToString();
        }

        // ---- racer identity ----

        public NameStatus CheckName(string name, string forPlayerId = null)
        {
            if (!IsValidName(name)) return NameStatus.Invalid;
            foreach (var reserved in _cfg.reservedNames ?? Array.Empty<string>())
                if (string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase)) return NameStatus.Taken;
            var owner = _store.FindByName(name);
            if (owner != null && owner.PlayerId != forPlayerId) return NameStatus.Taken;
            return NameStatus.Available;
        }

        public bool IsValidName(string name)
        {
            if (name == null) return false;
            name = name.Trim();
            if (name.Length < _cfg.nameMinLength || name.Length > _cfg.nameMaxLength) return false;
            foreach (char ch in name) if (!(char.IsLetterOrDigit(ch) && ch < 128) && ch != '_') return false;
            return true;
        }

        /// <summary>Sets racer name, look and home city. Returns false with a reason when the name is invalid or taken.</summary>
        public bool SetRacer(string playerId, string name, string lookId, string city, out string error)
        {
            error = null;
            var a = Get(playerId);
            if (name != null)
            {
                name = name.Trim();
                var status = CheckName(name, playerId);
                if (status == NameStatus.Invalid) { error = $"Names are {_cfg.nameMinLength} to {_cfg.nameMaxLength} letters, numbers or _"; return false; }
                if (status == NameStatus.Taken) { error = "That name don go"; return false; }
                a.RacerName = name;
            }
            if (lookId != null) a.LookId = lookId;
            if (city != null)
            {
                bool known = false;
                foreach (var c in _cfg.cities ?? Array.Empty<string>()) if (string.Equals(c, city, StringComparison.OrdinalIgnoreCase)) { known = true; city = c; }
                a.HomeCity = known ? city : "Other";
            }
            if (a.ReferralCode == null && a.RacerName != null && a.Status == AccountStatus.Claimed) a.ReferralCode = NewReferralCode(a);
            _store.Save(a);
            return true;
        }

        // ---- referrals ----

        public string ReferralCodeFor(string playerId)
        {
            var a = Get(playerId);
            if (a.ReferralCode == null) { a.ReferralCode = NewReferralCode(a); _store.Save(a); }
            return a.ReferralCode;
        }

        private string NewReferralCode(AccountRecord a)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var prefix = new StringBuilder();
            foreach (char ch in (a.RacerName ?? a.PlayerId ?? "RACE")) { if (char.IsLetter(ch) && ch < 128) prefix.Append(char.ToUpperInvariant(ch)); if (prefix.Length == 4) break; }
            if (prefix.Length < 2) prefix.Append("NK");
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var sb = new StringBuilder(prefix.ToString()).Append('-');
                for (int i = 0; i < 3; i++) sb.Append(alphabet[_rng.Range(0, alphabet.Length)]);
                string code = sb.ToString();
                if (_store.FindByReferralCode(code) == null) return code;
            }
            return prefix + "-" + _unixMs() % 1000;
        }

        /// <summary>Links a referral code to a player who has not raced with one yet. The reward is paid after their first race.</summary>
        public bool ApplyReferral(string playerId, string code, out string error)
        {
            error = null;
            var a = Get(playerId);
            if (a.ReferredBy != null) { error = "You already used a code"; return false; }
            var referrer = _store.FindByReferralCode((code ?? "").Trim());
            if (referrer == null) { error = "Code no correct"; return false; }
            if (referrer.PlayerId == playerId) { error = "That's your own code"; return false; }
            a.ReferredBy = referrer.PlayerId;
            _store.Save(a);
            return true;
        }

        /// <summary>Call once the referred player has finished their first race. Pays both sides once.</summary>
        public bool RewardReferralAfterFirstRace(string playerId, out string referrerId)
        {
            referrerId = null;
            var a = Get(playerId);
            if (a.ReferredBy == null || a.ReferralRewarded || _ledger == null) return false;
            referrerId = a.ReferredBy;
            _ledger.Credit(playerId, _cfg.referralBonusCoins, "referral_joined", "referral:joined:" + playerId, out _);
            _ledger.Credit(referrerId, _cfg.referralBonusCoins, "referral_invited", "referral:invited:" + playerId, out _);
            a.ReferralRewarded = true;
            _store.Save(a);
            return true;
        }

        public static string MaskPhone(string e164)
        {
            if (string.IsNullOrEmpty(e164) || e164.Length < 8) return e164;
            return e164.Substring(0, e164.Length - 7) + " ••• " + e164.Substring(e164.Length - 4);
        }
    }
}
