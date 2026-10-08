using System;
using System.Diagnostics;
using System.Threading;
using NaijaKart.Core.Config;
using NaijaKart.Core.Net;
using NaijaKart.Core.Race;
using NaijaKart.Core.Simulation;
using NaijaKart.Core.Util;
using NaijaKart.Server.Config;
using NaijaKart.Server.Hosting;
using NaijaKart.Server.Persistence;
using NaijaKart.Server.Transport;

namespace NaijaKart.Server
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "run";
            string configDir = GetOption(args, "--config") ?? JsonConfigSource.ResolveDefaultDirectory();
            var log = new ConsoleLogger(HasFlag(args, "--verbose") ? LogLevel.Debug : LogLevel.Info);

            switch (command)
            {
                case "export-defaults":
                    JsonConfigSource.WriteDefaults(configDir);
                    Console.WriteLine("Wrote default game-config.json to " + configDir);
                    return 0;
                case "validate-config":
                    return ValidateConfig(configDir);
                case "simulate":
                    return Simulate(configDir, log, int.Parse(GetOption(args, "--players") ?? "8"), GetOption(args, "--track"), GetOption(args, "--replay"), ulong.Parse(GetOption(args, "--seed") ?? "7"));
                case "export-world":
                    return ExportWorld(configDir, GetOption(args, "--track"), GetOption(args, "--out") ?? "world.json", ulong.Parse(GetOption(args, "--seed") ?? "1"), GetOption(args, "--theme") ?? "day");
                case "run":
                    return Run(configDir, log, int.Parse(GetOption(args, "--port") ?? "7777"), GetOption(args, "--data"));
                default:
                    Console.Error.WriteLine("Usage: naijakart-server [run|simulate|validate-config|export-defaults] [--config DIR] [--port N] [--data FILE.json] [--players N] [--track ID] [--verbose]");
                    return 2;
            }
        }

        private static int ValidateConfig(string configDir)
        {
            var content = new JsonConfigSource(configDir);
            var errors = ConfigValidator.Validate(content);
            if (errors.Count == 0)
            {
                Console.WriteLine($"Config OK: {content.Items.items.Length} items, {content.Vehicles.vehicles.Length} vehicles, {content.Characters.characters.Length} characters, {content.TrackIds.Length} tracks");
                return 0;
            }
            foreach (var e in errors) Console.Error.WriteLine("CONFIG ERROR: " + e);
            return 1;
        }

        /// <summary>Headless bot race: a smoke test of the whole simulation that runs faster than real time.</summary>
        private static int ExportWorld(string configDir, string trackId, string outPath, ulong seed, string theme)
        {
            var content = new JsonConfigSource(configDir);
            var track = content.GetTrack(trackId ?? content.TrackIds[0]);
            if (track == null) { Console.Error.WriteLine("Unknown track"); return 1; }
            var world = NaijaKart.Core.World.WorldBuilder.Build(track, seed, theme);
            int verts = 0; foreach (var b in world.batches) verts += b.VertexCount;
            System.IO.File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(world, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true }));
            Console.WriteLine($"Wrote {outPath}: {world.batches.Count} batches, {verts} vertices, {world.templates.Count} templates, {world.props.Count} props");
            return 0;
        }

        private static int Simulate(string configDir, ILogger log, int players, string trackId, string replayPath, ulong seed)
        {
            var content = new JsonConfigSource(configDir);
            var hub = new LoopbackTransportHub(seed);
            var server = new GameServer(content, hub.Server, log, seed: seed);
            var room = server.CreateRoom(RaceMode.Practice, trackId ?? content.TrackIds[0], content.Game.raceRules.defaultLaps, null);
            // bot_1 drives the Danfo (the hero kart of the previews); the rest cycle through the garage.
            for (int i = 0; i < players; i++) room.AddBot("bot_" + (i + 1), 0.4f + 0.08f * i, i == 0 ? "danfo" : null);
            room.Race.BeginCountdown();

            var replay = replayPath != null ? new ReplayWriter(room.Race, content) : null;
            if (replay != null) room.EventsDrained += replay.AddEvents;
            var sw = Stopwatch.StartNew();
            int ticks = 0;
            while (room.Race.State != RaceState.Results && ticks < content.Game.simulation.tickRate * 600)
            {
                server.Tick();
                replay?.Capture();
                ticks++;
            }
            sw.Stop();
            if (replay != null)
            {
                replay.Finish(replayPath);
                Console.WriteLine($"Wrote replay {replayPath}: {replay.FrameCount} frames");
            }
            var results = room.Race.Results;
            if (results == null) { Console.Error.WriteLine("Race did not finish"); return 1; }
            Console.WriteLine($"Race {results.RaceId} on {results.TrackId}: {results.RaceDuration:0.0}s race time, {ticks} ticks simulated in {sw.ElapsedMilliseconds} ms");
            foreach (var e in results.Entries)
            {
                Console.WriteLine($"  {e.FinishPosition}. {e.DisplayName,-10} {(e.Finished ? e.TotalTime.ToString("0.00") + "s" : e.Status.ToString()),-14} laps {e.LapsCompleted} best {e.BestLap:0.00}  ovt {e.Stats.Overtakes} items {e.Stats.ItemsUsed} hit {e.Stats.ItemHitsLanded} taken {e.Stats.ItemHitsTaken} hz {e.Stats.HazardHits} col {e.Stats.Collisions} lastma t/e/f {e.Stats.LastmaTargeted}/{e.Stats.LastmaEscapes}/{e.Stats.LastmaFinesPaid} rec {e.Stats.Recoveries}");
            }
            return 0;
        }

        private static int Run(string configDir, ILogger log, int port, string dataFile)
        {
            var content = new JsonConfigSource(configDir);
            using var transport = new TcpJsonServerTransport(port, log);
            transport.Start();
            using var state = dataFile != null ? new JsonFileStateStore(dataFile, content.Game.progression) : null;
            var server = state != null
                ? new GameServer(content, transport, log, state.Coins, state.Profiles, state.Rivalries, challengeProgress: state.Challenges)
                : new GameServer(content, transport, log);
            log.Info("server", $"Naija Kart server listening on TCP {transport.Port}, tick {content.Game.simulation.tickRate} Hz, config {configDir}, data {(dataFile ?? "in-memory")}");
            if (state != null) log.Info("server", $"Loaded {state.PlayerCount} player profiles");

            var running = true;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };
            double tickMs = 1000.0 / content.Game.simulation.tickRate;
            var sw = Stopwatch.StartNew();
            double next = 0;
            while (running)
            {
                double now = sw.Elapsed.TotalMilliseconds;
                if (now >= next)
                {
                    server.Tick();
                    state?.Tick();
                    next += tickMs;
                    if (now - next > tickMs * 10) next = now; // fell far behind: resync rather than spiral
                }
                else
                {
                    Thread.Sleep((int)Math.Max(0, Math.Min(5, next - now)));
                }
            }
            log.Info("server", "Shutting down");
            return 0;
        }

        /// <summary>Records positions, hazards and events each tick for the world previewer.</summary>
        private sealed class ReplayWriter
        {
            private readonly RaceSimulation _race;
            private readonly JsonConfigSource _content;
            private readonly System.Collections.Generic.List<object> _frames = new System.Collections.Generic.List<object>();
            private readonly System.Collections.Generic.List<object> _events = new System.Collections.Generic.List<object>();
            private readonly System.Collections.Generic.List<RaceEvent> _buffer = new System.Collections.Generic.List<RaceEvent>();
            private readonly System.Collections.Generic.Dictionary<string, (string vehicle, string name)> _karts = new System.Collections.Generic.Dictionary<string, (string, string)>();

            public int FrameCount => _frames.Count;

            public ReplayWriter(RaceSimulation race, JsonConfigSource content)
            {
                _race = race; _content = content;
                foreach (var p in race.Participants) _karts[p.PlayerId] = (p.Vehicle.id, p.DisplayName);
            }

            public void Capture()
            {
                if (!_race.StateMachine.IsRacing && _race.State != RaceState.Countdown) return;
                float t = _race.State == RaceState.Countdown ? -(_content.Game.raceRules.countdownSeconds - _race.StateMachine.TimeInState) : _race.RaceTime;
                var karts = new System.Collections.Generic.List<object>();
                foreach (var p in _race.Participants)
                {
                    var s = p.State;
                    karts.Add(new { id = p.PlayerId, x = R(s.Position.X), y = R(s.Position.Y), z = R(s.Position.Z), h = R(s.Heading), v = R(s.Speed), lat = R(s.LateralVelocity),
                        d = s.IsDrifting, dd = s.DriftDirection, b = s.IsBoosting, l = p.Checkpoints?.LapsCompleted ?? 0, p = p.Position, st = p.Status.ToString(),
                        it = _race.InventoryOf(p.PlayerId)?.SnapshotIds(), bc = s.BoostCharges, stun = s.IsStunned, sh = s.ShieldTimeRemaining > 0f });
                }
                var hazards = new System.Collections.Generic.List<object>();
                foreach (var h in _race.Hazards.All)
                    hazards.Add(new { id = h.Id, k = h.Kind.ToString(), x = R(h.Position.X), y = R(h.Position.Y), z = R(h.Position.Z), dx = R(h.IsZone ? h.ZoneDirection.X : h.Velocity.X), dz = R(h.IsZone ? h.ZoneDirection.Z : h.Velocity.Z), armed = h.IsArmed });
                _frames.Add(new { t = R(t), karts, hazards });
                _buffer.Clear();
                // Events are drained by the room for networking; peek via a second drain is not possible, so we
                // reconstruct the ones the previewer needs from state deltas is overkill: the room re-emits them
                // through the server transport. Here we subscribe to the simulation's event list before the room
                // drains it by capturing right after Step (RaceRoom drains after Tick); see Program.Simulate order.
            }

            public void AddEvents(System.Collections.Generic.List<RaceEvent> events)
            {
                foreach (var e in events) _events.Add(new { t = R(e.Time), type = e.Type.ToString(), p = e.PlayerId, target = e.TargetPlayerId, payload = e.Payload, i = e.IntValue });
            }

            public void Finish(string path)
            {
                string follow = null;
                foreach (var kv in _karts) if (kv.Value.vehicle == "danfo") follow = kv.Key;
                if (follow == null && _race.Results != null && _race.Results.Entries.Count > 0) follow = _race.Results.Entries[0].PlayerId;
                var kartList = new System.Collections.Generic.List<object>();
                foreach (var kv in _karts) kartList.Add(new { id = kv.Key, name = kv.Value.name, vehicle = kv.Value.vehicle, template = NaijaKart.Core.World.WorldBuilder.KartTemplateFor(kv.Value.vehicle) });
                var doc = new { trackId = _race.Setup.TrackId, laps = _race.Setup.Laps, sampleRate = _content.Game.simulation.tickRate, followKartId = follow, karts = kartList, frames = _frames, events = _events };
                System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(doc));
            }

            private static float R(float v) => (float)System.Math.Round(v, 3);
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;
    }
}
