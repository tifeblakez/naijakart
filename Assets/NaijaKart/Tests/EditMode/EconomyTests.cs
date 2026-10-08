using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class CoinLedgerTests
    {
        private CoinLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            _ledger = new CoinLedger(new InMemoryCoinStore(), new EconomyConfig { startingBalance = 100, maxSingleTransaction = 10000 }, () => 1234L);
            _ledger.EnsureAccount("tife");
        }

        [Test]
        public void CreditAndDebitRecordFullTransaction()
        {
            Assert.That(_ledger.Credit("tife", 50, "race_reward", "k1", out var tx), Is.EqualTo(TransactionResult.Ok));
            Assert.That(tx.BalanceBefore, Is.EqualTo(100));
            Assert.That(tx.BalanceAfter, Is.EqualTo(150));
            Assert.That(tx.Amount, Is.EqualTo(50));
            Assert.That(tx.TimestampUnixMs, Is.EqualTo(1234L));
            Assert.That(tx.TransactionId, Is.Not.Empty);
            Assert.That(_ledger.Debit("tife", 30, "fine", "k2", out tx), Is.EqualTo(TransactionResult.Ok));
            Assert.That(_ledger.GetBalance("tife"), Is.EqualTo(120));
            Assert.That(_ledger.History("tife").Count, Is.EqualTo(2));
        }

        [Test]
        public void InsufficientBalanceIsRejectedWithoutChange()
        {
            Assert.That(_ledger.Debit("tife", 500, "fine", "k1", out var tx), Is.EqualTo(TransactionResult.InsufficientBalance));
            Assert.That(tx, Is.Null);
            Assert.That(_ledger.GetBalance("tife"), Is.EqualTo(100));
        }

        [Test]
        public void DuplicateIdempotencyKeyIsRejected()
        {
            Assert.That(_ledger.Credit("tife", 50, "r", "same", out _), Is.EqualTo(TransactionResult.Ok));
            Assert.That(_ledger.Credit("tife", 50, "r", "same", out _), Is.EqualTo(TransactionResult.Duplicate));
            Assert.That(_ledger.GetBalance("tife"), Is.EqualTo(150));
        }

        [Test]
        public void InvalidAmountsAndUnknownPlayers()
        {
            Assert.That(_ledger.Credit("tife", 0, "r", "k", out _), Is.EqualTo(TransactionResult.InvalidAmount));
            Assert.That(_ledger.Credit("tife", 999999, "r", "k", out _), Is.EqualTo(TransactionResult.InvalidAmount));
            Assert.That(_ledger.Credit("ghost", 10, "r", "k", out _), Is.EqualTo(TransactionResult.UnknownPlayer));
        }

        [Test]
        public void EnsureAccountIsIdempotent()
        {
            _ledger.Credit("tife", 50, "r", "k", out _);
            _ledger.EnsureAccount("tife");
            Assert.That(_ledger.GetBalance("tife"), Is.EqualTo(150));
        }
    }
}
