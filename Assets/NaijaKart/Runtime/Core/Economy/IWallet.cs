namespace NaijaKart.Core.Economy
{
    /// <summary>What in-race systems (LASTMA fines, bail) need from the economy. Implemented by LedgerWallet.</summary>
    public interface IWallet
    {
        long Balance(string playerId);
        bool TryDebit(string playerId, long amount, string source, string idempotencyKey);
    }

    public sealed class LedgerWallet : IWallet
    {
        private readonly CoinLedger _ledger;

        public LedgerWallet(CoinLedger ledger)
        {
            _ledger = ledger;
        }

        public long Balance(string playerId) => _ledger.GetBalance(playerId);

        public bool TryDebit(string playerId, long amount, string source, string idempotencyKey) =>
            _ledger.Debit(playerId, amount, source, idempotencyKey, out _) == TransactionResult.Ok;
    }

    /// <summary>
    /// Practice/offline wallet: every debit succeeds. Only for local single-player practice where no
    /// coins are at stake. Never used by the server.
    /// </summary>
    public sealed class PracticeWallet : IWallet
    {
        public long Balance(string playerId) => long.MaxValue;
        public bool TryDebit(string playerId, long amount, string source, string idempotencyKey) => true;
    }
}
