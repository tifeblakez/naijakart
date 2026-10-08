using System.Collections.Generic;
using NaijaKart.Core.Chaos;
using NaijaKart.Core.Config;
using NaijaKart.Core.Items;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Util;
using NaijaKart.Core.Vehicle;
using NUnit.Framework;

namespace NaijaKart.Tests
{
    public class ItemTests
    {
        private InMemoryConfigSource _content;
        private RaceSimulation _sim;
        private ItemEffectSystem _effects;

        [SetUp]
        public void SetUp()
        {
            _content = TestContent.Create();
            _sim = new RaceSimulation(TestContent.Setup(items: true, lastma: false, road: false), _content, null);
            _sim.AddParticipant("a", "A", "danfo", null);
            _sim.AddParticipant("b", "B", "keke", null);
            _sim.BeginCountdown();
            while (_sim.State == RaceState.Countdown) _sim.Step();
            _effects = new ItemEffectSystem(_sim, new RoadEventScheduler(_sim));
        }

        private ItemDefinition Item(string id)
        {
            foreach (var i in _content.Items.items) if (i.id == id) return i;
            return null;
        }

        [Test]
        public void RollerRespectsPositionWeights()
        {
            var roller = new ItemRoller(_content.Items);
            var rng = new DeterministicRandom(3);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < 2000; i++)
            {
                var item = roller.Roll(1, 8, rng, null, 0f);
                counts[item.id] = counts.TryGetValue(item.id, out int c) ? c + 1 : 1;
            }
            Assert.That(counts.ContainsKey("pure_water"), Is.False, "zero weight in 1st place is never rolled");
            Assert.That(counts.ContainsKey("danfo"), Is.False);
            Assert.That(counts["generator_shield"], Is.GreaterThan(counts["jollof_boost"]), "leader favours defence");
            counts.Clear();
            for (int i = 0; i < 2000; i++)
            {
                var item = roller.Roll(8, 8, rng, null, 0f);
                counts[item.id] = counts.TryGetValue(item.id, out int c) ? c + 1 : 1;
            }
            Assert.That(counts["danfo"], Is.GreaterThan(counts["generator_shield"]), "back of the pack favours comeback chaos");
            Assert.That(counts.ContainsKey("generator_shield"), Is.True, "but a comeback is never guaranteed");
        }

        [Test]
        public void RollerHonoursAllowListAndCooldown()
        {
            var roller = new ItemRoller(_content.Items, new HashSet<string> { "egg" });
            Assert.That(roller.Count, Is.EqualTo(1));
            var inv = new ItemInventory();
            inv.Grant("egg", 1f, 5f, 0f);
            inv.Consume(0);
            Assert.That(inv.IsOnCooldown("egg", 2f), Is.True);
            Assert.That(inv.IsOnCooldown("egg", 6f), Is.False);
        }

        [Test]
        public void InventoryRollDelayGatesUse()
        {
            var inv = new ItemInventory();
            inv.Grant("egg", 1f, 0f, 0f);
            Assert.That(inv.HasItem && !inv.IsReadyAny);
            inv.Tick(0.5f);
            Assert.That(inv.IsReadyAny, Is.False);
            inv.Tick(0.6f);
            Assert.That(inv.IsReadyAny, Is.True);
            Assert.That(inv.Consume(0), Is.EqualTo("egg"));
            Assert.That(inv.HasItem, Is.False);
        }

        [Test]
        public void FourSlotInventoryFillsInOrderAndUsesByTappedSlot()
        {
            var inv = new ItemInventory(4);
            Assert.That(inv.Grant("jollof_boost", 0f, 0f, 0f), Is.EqualTo(0));
            Assert.That(inv.Grant("pure_water", 0f, 0f, 0f), Is.EqualTo(1));
            Assert.That(inv.Grant("egg", 1f, 0f, 0f), Is.EqualTo(2));
            Assert.That(inv.Grant("oil", 0f, 0f, 0f), Is.EqualTo(3));
            Assert.That(inv.IsFull);
            Assert.That(inv.Grant("egg", 0f, 0f, 0f), Is.EqualTo(-1), "full");
            Assert.That(inv.ResolveSlot(2), Is.EqualTo(-1), "slot 2 still rolling");
            Assert.That(inv.ResolveSlot(-1), Is.EqualTo(0), "first ready slot");
            Assert.That(inv.ResolveSlot(1), Is.EqualTo(1));
            Assert.That(inv.Consume(1), Is.EqualTo("pure_water"));
            Assert.That(inv.ItemAt(1), Is.Null);
            Assert.That(inv.Grant("danfo", 0f, 0f, 0f), Is.EqualTo(1), "gap refilled first");
            var ids = inv.SnapshotIds();
            Assert.That(ids, Is.EqualTo(new[] { "jollof_boost", "danfo", "egg", "oil" }));
            inv.Clear();
            Assert.That(inv.Count, Is.EqualTo(0));
        }

        [Test]
        public void SpikeStripStunsAndIsConsumed()
        {
            var a = _sim.Find("a");
            var b = _sim.Find("b");
            TestContent.Drive(_sim, "a", 30);
            TestContent.Drive(_sim, "b", 30);
            var spike = new ItemDefinition { id = "spike", effect = ItemEffectType.DropSpikeStrip, duration = 10f, magnitude = 2f };
            Assert.That(_effects.Use(a, spike), Is.True);
            var h = _sim.Hazards.All[0];
            Assert.That(h.Kind, Is.EqualTo(HazardKind.SpikeStrip));
            Assert.That(_effects.ApplyHazardContact(b, h), Is.True);
            Assert.That(b.State.IsStunned);
            Assert.That(_sim.Hazards.All.Count, Is.EqualTo(0));
        }

        [Test]
        public void SimulationUsesTheTappedSlot()
        {
            var a = _sim.Find("a");
            var inv = _sim.InventoryOf("a");
            inv.Grant("jollof_boost", 0f, 0f, 0f);
            inv.Grant("generator_shield", 0f, 0f, 0f);
            TestContent.Drive(_sim, "a", 30);
            _sim.SubmitInput("a", new Core.Input.PlayerInputFrame { Sequence = a.LastInputSequence + 1, UseItem = true, ItemSlot = 1 });
            _sim.Step();
            Assert.That(a.State.HasShield, "slot 1 (shield) was used");
            Assert.That(inv.ItemAt(0), Is.EqualTo("jollof_boost"), "slot 0 untouched");
            Assert.That(inv.ItemAt(1), Is.Null);
        }

        [Test]
        public void BoostItemRaisesSpeed()
        {
            var a = _sim.Find("a");
            TestContent.Drive(_sim, "a", 90);
            float before = a.State.Speed;
            Assert.That(_effects.Use(a, Item("jollof_boost")), Is.True);
            Assert.That(a.State.IsBoosting);
            TestContent.Drive(_sim, "a", 15);
            Assert.That(a.State.Speed, Is.GreaterThan(before * 1.05f));
            Assert.That(a.Telemetry.ItemsUsed, Is.EqualTo(1));
        }

        [Test]
        public void PureWaterBlindsNearestAheadAndShieldBlocksIt()
        {
            var a = _sim.Find("a");
            var b = _sim.Find("b");
            TestContent.Drive(_sim, "a", 30);            // a pulls ahead
            Assert.That(_sim.NearestAhead(b, 100f), Is.SameAs(a));
            Assert.That(_effects.Use(b, Item("pure_water")), Is.True);
            Assert.That(a.State.BlindTimeRemaining, Is.GreaterThan(0f));
            Assert.That(b.Telemetry.ItemHitsLanded, Is.EqualTo(1));
            Assert.That(a.Telemetry.ItemHitsTaken, Is.EqualTo(1));

            _effects.Use(a, Item("generator_shield"));
            Assert.That(a.State.HasShield);
            float blind = a.State.BlindTimeRemaining;
            _effects.Use(b, Item("pure_water"));
            Assert.That(a.State.HasShield, Is.False, "shield consumed");
            Assert.That(a.State.BlindTimeRemaining, Is.EqualTo(blind), "no new blind applied");
            Assert.That(b.Telemetry.ItemHitsLanded, Is.EqualTo(1));
        }

        [Test]
        public void AttackWithNobodyAheadFails()
        {
            var a = _sim.Find("a");
            TestContent.Drive(_sim, "a", 30);
            Assert.That(_effects.Use(a, Item("egg")), Is.False, "leader has no target ahead");
        }

        [Test]
        public void EggWobblesAndNoWahalaCleanses()
        {
            var a = _sim.Find("a");
            var b = _sim.Find("b");
            TestContent.Drive(_sim, "a", 30);
            _effects.Use(b, Item("egg"));
            Assert.That(a.State.WobbleTimeRemaining, Is.GreaterThan(0f));
            _effects.Use(a, Item("no_wahala"));
            Assert.That(a.State.WobbleTimeRemaining, Is.EqualTo(0f));
        }

        [Test]
        public void OilDropsHazardThatCutsGripAndSharpGuyAvoidsIt()
        {
            var a = _sim.Find("a");
            var b = _sim.Find("b");
            TestContent.Drive(_sim, "a", 30);
            Assert.That(_effects.Use(a, Item("oil")), Is.True);
            Assert.That(_sim.Hazards.All.Count, Is.EqualTo(1));
            var oil = _sim.Hazards.All[0];
            Assert.That(oil.Kind, Is.EqualTo(HazardKind.OilPatch));
            Assert.That(oil.OwnerPlayerId, Is.EqualTo("a"));

            b.State.AutoAvoidCharges = 1;
            Assert.That(_effects.ApplyHazardContact(b, oil), Is.False, "Sharp Guy auto-avoids");
            Assert.That(b.State.AutoAvoidCharges, Is.EqualTo(0));
            Assert.That(_sim.Hazards.All.Count, Is.EqualTo(0));

            var oil2 = _sim.Hazards.Spawn(HazardKind.OilPatch, b.State.Position, 2f, 10f, 0f, "a");
            Assert.That(_effects.ApplyHazardContact(b, oil2), Is.True);
            Assert.That(b.State.ExternalGripMultiplier, Is.LessThan(0.5f));
            Assert.That(a.Telemetry.ItemHitsLanded, Is.EqualTo(1));
            Assert.That(b.State.HazardImmunityTime, Is.GreaterThan(0f));
        }

        [Test]
        public void DanfoItemSpawnsCrossingAheadOfTarget()
        {
            var a = _sim.Find("a");
            var b = _sim.Find("b");
            TestContent.Drive(_sim, "a", 30);
            Assert.That(_effects.Use(b, Item("danfo")), Is.True);
            Assert.That(_sim.Hazards.All.Count, Is.EqualTo(1));
            var h = _sim.Hazards.All[0];
            Assert.That(h.Kind, Is.EqualTo(HazardKind.DanfoCrossing));
            Assert.That(h.IsArmed, Is.False, "telegraphed before it is dangerous");
            Assert.That(h.Velocity.FlatMagnitude, Is.GreaterThan(0f));
        }

        [Test]
        public void HardHazardStunsAndGrantsImmunity()
        {
            var b = _sim.Find("b");
            TestContent.Drive(_sim, "b", 60);
            var h = _sim.Hazards.Spawn(HazardKind.OkadaCrossing, b.State.Position, 1f, 5f, 0f);
            Assert.That(_effects.ApplyHazardContact(b, h), Is.True);
            Assert.That(b.State.IsStunned);
            var h2 = _sim.Hazards.Spawn(HazardKind.OkadaCrossing, b.State.Position, 1f, 5f, 0f);
            Assert.That(_effects.ApplyHazardContact(b, h2), Is.False, "immune right after a hit");
        }

        [Test]
        public void ItemBoxesGrantItemsDuringRace()
        {
            var events = new List<RaceEvent>();
            int granted = 0, used = 0;
            TestContent.RunBotRace(_content, TestContent.Setup(laps: 2, items: true, lastma: false, road: false), 4, perTick: s =>
            {
                events.Clear();
                s.DrainEvents(events);
                foreach (var e in events) { if (e.Type == RaceEventType.ItemGranted) granted++; if (e.Type == RaceEventType.ItemUsed) used++; }
            });
            Assert.That(granted, Is.GreaterThan(4));
            Assert.That(used, Is.GreaterThan(0));
        }

        [Test]
        public void HazardBudgetIsEnforced()
        {
            var hs = new HazardSystem(new ItemSystemConfig { maxActiveHazards = 3 });
            for (int i = 0; i < 5; i++) hs.Spawn(HazardKind.Pothole, Core.Math.Vec3.Zero, 1f, 10f, 0f);
            Assert.That(hs.All.Count, Is.EqualTo(3));
        }
    }
}
