using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Track;

namespace NaijaKart.Tests
{
    /// <summary>Programmatic content for tests: no JSON, no engine, fully deterministic.</summary>
    public static class TestContent
    {
        public static InMemoryConfigSource Create(bool withShortcut = false)
        {
            var c = new InMemoryConfigSource();
            c.Game = new GameConfig();
            c.Vehicles = new VehicleRoster
            {
                vehicles = new[]
                {
                    new VehicleDefinition { id = "danfo", displayName = "Danfo", speed = 72, acceleration = 82, handling = 61, drift = 55, weight = 88, boost = 65, traction = 62 },
                    new VehicleDefinition { id = "keke", displayName = "Keke", speed = 48, acceleration = 78, handling = 86, drift = 72, weight = 28, boost = 60, traction = 70 },
                }
            };
            c.Characters = new CharacterRoster
            {
                characters = new[] { new CharacterDefinition { id = "tunde", displayName = "Tunde", strengthStat = "acceleration", strengthBonus = 6, weaknessStat = "handling", weaknessPenalty = 3 } }
            };
            c.Items = new ItemLibrary { items = Items() };
            var track = TrackFactory.Oval("oval", 200f, 40f, 7f, checkpointCount: 8, itemBoxRows: 2, referenceLap: 30f);
            if (withShortcut)
            {
                // Alternative gate for checkpoint 3 placed inside the oval: a legal "risk route".
                var g = track.checkpoints[3].gates[0].position;
                TrackFactory.AddShortcutGate(track, 3, new Core.Math.Vec3(g.X * 0.5f, 0, g.Z), 8f);
            }
            c.AddTrack(track);
            return c;
        }

        public static ItemDefinition[] Items() => new[]
        {
            new ItemDefinition { id = "jollof_boost", category = ItemCategory.Boost, effect = ItemEffectType.SpeedBoost, duration = 2.4f, magnitude = 1.3f, positionWeights = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "generator_shield", category = ItemCategory.Defense, effect = ItemEffectType.Shield, duration = 8f, positionWeights = new[] { 3f, 2f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "no_wahala", category = ItemCategory.Defense, effect = ItemEffectType.Cleanse, positionWeights = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "pure_water", category = ItemCategory.Attack, effect = ItemEffectType.BlindTarget, targeting = ItemTargeting.NearestAhead, duration = 2.5f, positionWeights = new[] { 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "egg", category = ItemCategory.Attack, effect = ItemEffectType.WobbleTarget, targeting = ItemTargeting.NearestAhead, duration = 2f, magnitude = 0.5f, positionWeights = new[] { 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "oil", category = ItemCategory.Attack, effect = ItemEffectType.DropOilPatch, duration = 15f, magnitude = 2f, positionWeights = new[] { 2f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "danfo", category = ItemCategory.Chaos, effect = ItemEffectType.SpawnDanfoCrossing, targeting = ItemTargeting.NearestAhead, magnitude = 30f, positionWeights = new[] { 0f, 0.5f, 1f, 1f, 2f, 2f, 2f, 2f } },
            new ItemDefinition { id = "okada", category = ItemCategory.Chaos, effect = ItemEffectType.SpawnOkadaCrossing, targeting = ItemTargeting.NearestAhead, magnitude = 22f, positionWeights = new[] { 0.5f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "sharp_guy", category = ItemCategory.Utility, effect = ItemEffectType.AutoAvoid, magnitude = 1f, positionWeights = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            new ItemDefinition { id = "suya_burst", category = ItemCategory.Boost, effect = ItemEffectType.SpeedBoost, duration = 0.9f, magnitude = 1.55f, positionWeights = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
        };

        public static RaceSetup Setup(string id = "race1", int laps = 2, bool items = true, bool lastma = true, bool road = true, ulong seed = 42, RaceMode mode = RaceMode.QuickRace) =>
            new RaceSetup { RaceId = id, TrackId = "oval", Laps = laps, ItemsEnabled = items, LastmaEnabled = lastma, RoadEventsEnabled = road, Seed = seed, Mode = mode };

        /// <summary>Runs a bot-driven race to completion. Returns the simulation in Results state.</summary>
        public static RaceSimulation RunBotRace(InMemoryConfigSource content, RaceSetup setup, int players, IWallet wallet = null,
            System.Action<RaceSimulation> perTick = null, int maxSeconds = 600)
        {
            var sim = new RaceSimulation(setup, content, wallet ?? new PracticeWallet());
            var geo = new TrackGeometry(content.GetTrack(setup.TrackId));
            var bots = new Dictionary<string, BotDriver>();
            for (int i = 0; i < players; i++)
            {
                string id = "p" + i;
                sim.AddParticipant(id, "Player " + i, i % 2 == 0 ? "danfo" : "keke", "tunde", isBot: true);
                bots[id] = new BotDriver(geo, (ulong)(i + 1) * 17, 0.5f + 0.05f * i);
            }
            sim.BeginCountdown();
            int maxTicks = content.Game.simulation.tickRate * maxSeconds;
            for (int t = 0; t < maxTicks && sim.State != RaceState.Results; t++)
            {
                if (sim.StateMachine.IsRacing)
                {
                    foreach (var kv in bots)
                    {
                        var p = sim.Find(kv.Key);
                        if (p.Status != ParticipantStatus.Connected) continue;
                        sim.SubmitInput(kv.Key, kv.Value.Think(p, sim.Hazards.All, sim.FixedDeltaTime));
                        var le = sim.Lastma.EventFor(kv.Key);
                        if (le != null && le.Phase == Core.Lastma.LastmaPhase.FinePending) sim.PayFine(kv.Key);
                    }
                }
                sim.Step();
                perTick?.Invoke(sim);
            }
            return sim;
        }

        public static void Drive(RaceSimulation sim, string playerId, int ticks, float steer = 0f, bool drift = false, bool useItem = false)
        {
            var p = sim.Find(playerId);
            for (int i = 0; i < ticks; i++)
            {
                sim.SubmitInput(playerId, new Core.Input.PlayerInputFrame { Sequence = p.LastInputSequence + 1, Steer = steer, Drift = drift, UseItem = useItem });
                sim.Step();
            }
        }
    }
}
