using System;
using System.IO;
using NaijaKart.Core.Config;
using NaijaKart.Core.Net;
using NaijaKart.Core.Track;
using NaijaKart.Server.Config;
using NaijaKart.Server.Hosting;

namespace NaijaKart.Server.Tests
{
    public sealed class ServerHarness
    {
        public readonly LoopbackTransportHub Hub;
        public readonly GameServer Server;
        public readonly IConfigSource Content;
        public readonly TrackGeometry Track;
        private readonly float _dt;
        private int _clients;

        public static string ConfigDir()
        {
            string[] candidates = { Path.Combine(AppContext.BaseDirectory, "Config"), JsonConfigSource.ResolveDefaultDirectory() };
            foreach (var c in candidates) if (File.Exists(Path.Combine(c, "items.json"))) return c;
            throw new FileNotFoundException("Config directory not found");
        }

        public ServerHarness(Action<GameConfig> tweak = null, ulong seed = 1)
        {
            var json = new JsonConfigSource(ConfigDir());
            tweak?.Invoke(json.Game);
            Content = json;
            Hub = new LoopbackTransportHub(seed);
            Server = new GameServer(Content, Hub.Server, null, seed: seed);
            Track = new TrackGeometry(Content.GetTrack(Content.TrackIds[0]));
            _dt = Server.FixedDeltaTime;
        }

        public TestClient NewClient(string id = null, float skill = 0.6f)
        {
            id = id ?? "player" + (++_clients);
            return new TestClient(id, Hub.CreateClient(id), Track, StableSeed(id), skill);
        }

        /// <summary>string.GetHashCode is randomised per process in .NET; tests need a stable seed.</summary>
        public static ulong StableSeed(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h == 0 ? 1UL : h;
        }

        /// <summary>Advances server and clients by one tick.</summary>
        public void Tick(params TestClient[] clients)
        {
            Hub.Advance(_dt);
            Server.Tick();
            foreach (var c in clients) c.Tick(_dt);
        }

        public void Run(float seconds, params TestClient[] clients)
        {
            int ticks = (int)Math.Ceiling(seconds / _dt);
            for (int i = 0; i < ticks; i++) Tick(clients);
        }

        public bool RunUntil(Func<bool> condition, float maxSeconds, params TestClient[] clients)
        {
            int ticks = (int)Math.Ceiling(maxSeconds / _dt);
            for (int i = 0; i < ticks; i++)
            {
                if (condition()) return true;
                Tick(clients);
            }
            return condition();
        }
    }
}
