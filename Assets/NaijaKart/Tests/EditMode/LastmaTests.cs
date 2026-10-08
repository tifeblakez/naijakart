using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Lastma;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class LastmaTests
    {
        private InMemoryConfigSource _content;
        private CoinLedger _ledger;
        private RaceSimulation _sim;
        private readonly List<RaceEvent> _events = new List<RaceEvent>();
        private readonly Dictionary<string, BotDriver> _bots = new Dictionary<string, BotDriver>();

        [SetUp]
        public void SetUp()
        {
            _content = TestContent.Create();
            _content.Game.lastma.warningSeconds = 1f;
            _content.Game.lastma.pursuitSeconds = 5f;
            _content.Game.lastma.fineDecisionSeconds = 3f;
            _ledger = new CoinLedger(new InMemoryCoinStore(), _content.Game.economy);
            _sim = new RaceSimulation(TestContent.Setup(items: false, lastma: true, road: false), _content, new LedgerWallet(_ledger));
            int n = 0;
            foreach (var id in new[] { "a", "b", "c" })
            {
                _sim.AddParticipant(id, id.ToUpperInvariant(), "danfo", null);
                _ledger.EnsureAccount(id);
                _bots[id] = new BotDriver(_sim.Track, (ulong)(++n) * 13, 0.7f);
            }
            _sim.BeginCountdown();
            while (_sim.State == RaceState.Countdown) _sim.Step();
        }

        private void StepAll(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                foreach (var p in _sim.Participants)
                {
                    if (p.Status != ParticipantStatus.Connected) continue;
                    var frame = _bots[p.PlayerId].Think(p, _sim.Hazards.All, _sim.FixedDeltaTime);
                    frame.Drift = false;
                    frame.UseItem = false;
                    _sim.SubmitInput(p.PlayerId, frame);
                }
                _sim.Step();
            }
        }

        private void StepUntil(string id, LastmaPhase phase, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                var e = _sim.Lastma.EventFor(id);
                if (e != null && e.Phase == phase) return;
                StepAll(1);
            }
        }

        private List<RaceEventType> Drain()
        {
            _events.Clear();
            _sim.DrainEvents(_events);
            var types = new List<RaceEventType>();
            foreach (var e in _events) types.Add(e.Type);
            return types;
        }

        private LastmaEvent Trigger(string id) => _sim.Lastma.Trigger(_sim.Find(id));

        [Test]
        public void WarningThenPursuitThenEscapeWhenDrivingClean()
        {
            StepAll(90); // up to speed
            var e = Trigger("a");
            Assert.That(e.Phase, Is.EqualTo(LastmaPhase.Warning));
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaWarning));
            StepAll(35);
            Assert.That(e.Phase, Is.EqualTo(LastmaPhase.Pursuit));
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaPursuitStarted));
            StepAll(30 * 5 + 5);
            Assert.That(e.Phase, Is.EqualTo(LastmaPhase.Escaped));
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaEscaped));
            Assert.That(_sim.Find("a").Telemetry.LastmaEscapes, Is.EqualTo(1));
            Assert.That(_sim.Lastma.EventFor("a"), Is.Null);
        }

        [Test]
        public void StunnedRacerGetsCaughtAndPulledOver()
        {
            StepAll(90);
            var e = Trigger("a");
            StepAll(35);
            var a = _sim.Find("a");
            a.State.StunTimeRemaining = 10f; // crashed badly
            StepUntil("a", LastmaPhase.FinePending, 30 * 4);
            Assert.That(e.Phase, Is.EqualTo(LastmaPhase.FinePending));
            Assert.That(a.State.IsImmobilised, Is.True);
            Assert.That(a.State.Speed, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaCaught));
            var surface = _sim.Track.SampleSurface(a.State.Position, 1f, 1f);
            Assert.That(surface.OnRoad, Is.False, "pulled over onto the roadside, out of the racing line");
        }

        private LastmaEvent GetCaught(string id)
        {
            StepAll(90);
            var e = Trigger(id);
            StepAll(35);
            _sim.Find(id).State.StunTimeRemaining = 10f;
            StepUntil(id, LastmaPhase.FinePending, 30 * 4);
            Assert.That(e.Phase, Is.EqualTo(LastmaPhase.FinePending));
            return e;
        }

        [Test]
        public void PayingFineResumesRacingAndDebitsLedger()
        {
            GetCaught("a");
            long before = _ledger.GetBalance("a");
            Assert.That(_sim.PayFine("a"), Is.True);
            Assert.That(_ledger.GetBalance("a"), Is.EqualTo(before - _content.Game.lastma.fineAmount));
            Assert.That(_sim.Find("a").State.IsImmobilised, Is.False);
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaFinePaid));
            Assert.That(_sim.PayFine("a"), Is.False, "nothing to pay now");
            Assert.That(_sim.Find("a").Telemetry.LastmaFinesPaid, Is.EqualTo(1));
        }

        [Test]
        public void InsufficientCoinsCannotPayFine()
        {
            _ledger.Debit("a", _ledger.GetBalance("a") - 10, "test", "drain", out _);
            GetCaught("a");
            Assert.That(_sim.PayFine("a"), Is.False);
            Assert.That(_sim.Find("a").State.IsImmobilised, Is.True);
            Assert.That(_ledger.GetBalance("a"), Is.EqualTo(10));
        }

        [Test]
        public void BailFlowLetsAFriendPay()
        {
            _ledger.Debit("a", _ledger.GetBalance("a") - 10, "test", "drain", out _);
            var e = GetCaught("a");
            Assert.That(_sim.PayBail("b", "a"), Is.False, "no request yet");
            Assert.That(_sim.RequestBail("a"), Is.True);
            Assert.That(_sim.RequestBail("a"), Is.False, "only once");
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaBailRequested));
            Assert.That(_sim.PayBail("a", "a"), Is.False, "cannot bail yourself");
            long bBefore = _ledger.GetBalance("b");
            Assert.That(_sim.PayBail("b", "a"), Is.True);
            Assert.That(_ledger.GetBalance("b"), Is.EqualTo(bBefore - _content.Game.lastma.fineAmount));
            Assert.That(_ledger.GetBalance("a"), Is.EqualTo(10), "the bailed player pays nothing");
            Assert.That(_sim.Find("a").State.IsImmobilised, Is.False);
            Assert.That(_sim.Find("b").Telemetry.BailGiven, Is.EqualTo(1));
            Assert.That(_sim.Find("a").Telemetry.BailReceived, Is.EqualTo(1));
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaBailed));
        }

        [Test]
        public void NoPaymentMeansArrestAndElimination()
        {
            _ledger.Debit("a", _ledger.GetBalance("a") - 10, "test", "drain", out _);
            GetCaught("a");
            _sim.RequestBail("a");
            StepAll(30 * 4);
            var types = Drain();
            Assert.That(types, Does.Contain(RaceEventType.LastmaArrested));
            Assert.That(types, Does.Contain(RaceEventType.PlayerEliminated));
            Assert.That(_sim.Find("a").Status, Is.EqualTo(ParticipantStatus.Eliminated));
            Assert.That(_sim.Find("a").IsActiveRacer, Is.False);
            Assert.That(_sim.ActiveByPosition.Count, Is.EqualTo(2));
        }

        [Test]
        public void ShieldBlocksTheCatch()
        {
            StepAll(90);
            var e = Trigger("a");
            StepAll(35);
            var a = _sim.Find("a");
            a.State.ShieldTimeRemaining = 60f;
            a.State.StunTimeRemaining = 10f;
            StepUntil("a", LastmaPhase.Escaped, 30 * 4);
            StepAll(1);
            Assert.That(a.State.HasShield, Is.False);
            Assert.That(a.State.IsImmobilised, Is.False);
            Assert.That(Drain(), Does.Contain(RaceEventType.LastmaEscaped));
        }

        [Test]
        public void BoostingRelievesPressure()
        {
            StepAll(90);
            var e = Trigger("a");
            StepAll(35);
            StepAll(30);
            float p1 = e.Pressure;
            _sim.VehicleModel.ApplyBoost(ref _sim.Find("a").State, 5f, 1.3f);
            StepAll(30);
            Assert.That(e.Pressure, Is.LessThan(p1 + 1e-4f));
        }

        [Test]
        public void TargetsAnyPositionNotOnlyFirst()
        {
            _content.Game.lastma.firstTriggerMinSeconds = 0f;
            _content.Game.lastma.minIntervalSeconds = 0.5f;
            _content.Game.lastma.maxIntervalSeconds = 1f;
            _content.Game.lastma.targetCooldownSeconds = 0f;
            _content.Game.lastma.warningSeconds = 0.1f;
            _content.Game.lastma.pursuitSeconds = 0.2f;
            var targeted = new Dictionary<int, int>();
            TestContent.RunBotRace(_content, TestContent.Setup(laps: 2, items: false, lastma: true, road: false), 6, perTick: s =>
            {
                _events.Clear();
                s.DrainEvents(_events);
                foreach (var ev in _events)
                {
                    if (ev.Type != RaceEventType.LastmaWarning) continue;
                    int pos = s.Find(ev.PlayerId).Position;
                    targeted[pos] = targeted.TryGetValue(pos, out int c) ? c + 1 : 1;
                }
            });
            Assert.That(targeted.Count, Is.GreaterThan(2), "LASTMA must spread across the field: " + string.Join(",", targeted.Keys));
        }
    }
}
