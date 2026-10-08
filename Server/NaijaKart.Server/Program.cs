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
                    return Simulate(configDir, log, int.Parse(GetOption(args, "--players") ?? "8"), GetOption(args, "--track"));
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
        private static int Simulate(string configDir, ILogger log, int players, string trackId)
        {
            var content = new JsonConfigSource(configDir);
            var hub = new LoopbackTransportHub();
            var server = new GameServer(content, hub.Server, log);
            var room = server.CreateRoom(RaceMode.Practice, trackId ?? content.TrackIds[0], content.Game.raceRules.defaultLaps, null);
            for (int i = 0; i < players; i++) room.AddBot("bot_" + (i + 1), 0.4f + 0.08f * i);
            room.Race.BeginCountdown();

            var sw = Stopwatch.StartNew();
            int ticks = 0;
            while (room.Race.State != RaceState.Results && ticks < content.Game.simulation.tickRate * 600)
            {
                server.Tick();
                ticks++;
            }
            sw.Stop();
            var results = room.Race.Results;
            if (results == null) { Console.Error.WriteLine("Race did not finish"); return 1; }
            Console.WriteLine($"Race {results.RaceId} on {results.TrackId}: {results.RaceDuration:0.0}s race time, {ticks} ticks simulated in {sw.ElapsedMilliseconds} ms");
            foreach (var e in results.Entries)
            {
                Console.WriteLine($"  {e.FinishPosition}. {e.DisplayName,-10} {(e.Finished ? e.TotalTime.ToString("0.00") + "s" : e.Status.ToString()),-14} best lap {e.BestLap:0.00}  overtakes {e.Stats.Overtakes} items {e.Stats.ItemsUsed} hits {e.Stats.ItemHitsLanded} lastma t/e {e.Stats.LastmaTargeted}/{e.Stats.LastmaEscapes} recoveries {e.Stats.Recoveries}");
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

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;
    }
}
