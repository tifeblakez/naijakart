using System;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.Economy
{
    /// <summary>Coins are earned through play and spend on fines, vehicles and cosmetics. Premium is purchased and
    /// spends on cosmetics only: it can never buy a fine, a vehicle stat or anything competitive (PRD §37).</summary>
    public enum Currency { Coins, Premium }

    public enum TransactionResult
    {
        Ok,
        InsufficientBalance,
        Duplicate,
        InvalidAmount,
        UnknownPlayer
    }

    /// <summary>Immutable record of one balance change (PRD §68).</summary>
    [Serializable]
    public sealed class CoinTransaction
    {
        public string TransactionId;
        public string PlayerId;
        public Currency Currency;
        public string Source;
        public long Amount;
        public long BalanceBefore;
        public long BalanceAfter;
        public long TimestampUnixMs;
        public string IdempotencyKey;
    }

    /// <summary>Backing store for balances and the idempotency index. In-memory now; database later.</summary>
    public interface ICoinStore
    {
        bool TryGetBalance(string playerId, out long balance);
        void SetBalance(string playerId, long balance);
        bool HasIdempotencyKey(string key);
        void Append(CoinTransaction tx);
        IReadOnlyList<CoinTransaction> History(string playerId);
    }

    public sealed class InMemoryCoinStore : ICoinStore
    {
        private readonly Dictionary<string, long> _balances = new Dictionary<string, long>();
        private readonly HashSet<string> _keys = new HashSet<string>();
        private readonly List<CoinTransaction> _log = new List<CoinTransaction>();

        public bool TryGetBalance(string playerId, out long balance) => _balances.TryGetValue(playerId, out balance);
        public void SetBalance(string playerId, long balance) => _balances[playerId] = balance;
        public bool HasIdempotencyKey(string key) => _keys.Contains(key);

        public void Append(CoinTransaction tx)
        {
            _keys.Add(tx.IdempotencyKey);
            _log.Add(tx);
        }

        public IReadOnlyList<CoinTransaction> History(string playerId)
        {
            var list = new List<CoinTransaction>();
            foreach (var tx in _log) if (tx.PlayerId == playerId) list.Add(tx);
            return list;
        }
    }

    /// <summary>
    /// Server-authoritative Naija Coins ledger. Every change goes through Credit/Debit with an
    /// idempotency key so retries (disconnects, duplicate messages) can never double-apply. Gameplay
    /// code on the client has no path to this class (PRD §36, §68).
    /// </summary>
    public sealed class CoinLedger
    {
        private readonly ICoinStore _store;
        private readonly EconomyConfig _config;
        private readonly Func<long> _clock;
        private readonly object _lock = new object();

        public CoinLedger(ICoinStore store, EconomyConfig config, Func<long> unixMsClock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clock = unixMsClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        private static string Key(string playerId, Currency c) => c == Currency.Coins ? playerId : playerId + "#premium";

        public void EnsureAccount(string playerId)
        {
            lock (_lock)
            {
                if (!_store.TryGetBalance(playerId, out _)) _store.SetBalance(playerId, _config.startingBalance);
                string pk = Key(playerId, Currency.Premium);
                if (!_store.TryGetBalance(pk, out _)) _store.SetBalance(pk, _config.startingPremiumBalance);
            }
        }

        public long GetBalance(string playerId) => GetBalance(playerId, Currency.Coins);

        public long GetBalance(string playerId, Currency currency)
        {
            lock (_lock) return _store.TryGetBalance(Key(playerId, currency), out long b) ? b : 0L;
        }

        public TransactionResult Credit(string playerId, Currency currency, long amount, string source, string idempotencyKey, out CoinTransaction tx) =>
            Apply(playerId, currency, amount, source, idempotencyKey, out tx);

        public TransactionResult Debit(string playerId, Currency currency, long amount, string source, string idempotencyKey, out CoinTransaction tx) =>
            Apply(playerId, currency, -amount, source, idempotencyKey, out tx);

        public bool CanAfford(string playerId, long amount) => GetBalance(playerId) >= amount;

        public TransactionResult Credit(string playerId, long amount, string source, string idempotencyKey, out CoinTransaction tx) =>
            Apply(playerId, Currency.Coins, amount, source, idempotencyKey, out tx);

        public TransactionResult Debit(string playerId, long amount, string source, string idempotencyKey, out CoinTransaction tx) =>
            Apply(playerId, Currency.Coins, -amount, source, idempotencyKey, out tx);

        private TransactionResult Apply(string playerId, Currency currency, long delta, string source, string idempotencyKey, out CoinTransaction tx)
        {
            string key = Key(playerId, currency);
            tx = null;
            if (string.IsNullOrEmpty(idempotencyKey)) throw new ArgumentException("idempotencyKey required");
            if (delta == 0 || System.Math.Abs(delta) > _config.maxSingleTransaction) return TransactionResult.InvalidAmount;
            lock (_lock)
            {
                if (_store.HasIdempotencyKey(idempotencyKey)) return TransactionResult.Duplicate;
                if (!_store.TryGetBalance(key, out long before)) return TransactionResult.UnknownPlayer;
                long after = before + delta;
                if (after < 0) return TransactionResult.InsufficientBalance;
                tx = new CoinTransaction
                {
                    TransactionId = IdGenerator.NextString("tx"),
                    PlayerId = playerId,
                    Currency = currency,
                    Source = source,
                    Amount = delta,
                    BalanceBefore = before,
                    BalanceAfter = after,
                    TimestampUnixMs = _clock(),
                    IdempotencyKey = idempotencyKey
                };
                _store.SetBalance(key, after);
                _store.Append(tx);
                return TransactionResult.Ok;
            }
        }

        public IReadOnlyList<CoinTransaction> History(string playerId)
        {
            lock (_lock) return _store.History(playerId);
        }
    }
}
