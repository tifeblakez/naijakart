using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NaijaKart.Core.Challenges;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Progression;
using NaijaKart.Core.Social;
using NaijaKart.Server.Config;

namespace NaijaKart.Server.Persistence
{
    /// <summary>
    /// Durable JSON-file persistence for development and small deployments (ADR-0004). Everything the
    /// game loop needs survives a server restart: balances, the transaction log, profiles, rivalries,
    /// challenge progress. Writes are coalesced and flushed on a timer and on shutdown; files are
    /// written atomically (temp + rename). A database adapter implements the same interfaces.
    /// </summary>
    public sealed class JsonFileStateStore : IDisposable
    {
        private sealed class State
        {
            public Dictionary<string, long> Balances = new Dictionary<string, long>();
            public List<CoinTransaction> Transactions = new List<CoinTransaction>();
            public Dictionary<string, PlayerProfile> Profiles = new Dictionary<string, PlayerProfile>();
            public Dictionary<string, Rivalry> Rivalries = new Dictionary<string, Rivalry>();
            public Dictionary<string, List<ChallengeProgress>> Challenges = new Dictionary<string, List<ChallengeProgress>>();
        }

        private readonly string _path;
        private readonly State _state;
        private readonly object _lock = new object();
        private bool _dirty;
        private DateTime _lastFlush = DateTime.UtcNow;

        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);
        public ICoinStore Coins { get; }
        public IProfileStore Profiles { get; }
        public IRivalryStore Rivalries { get; }
        public IChallengeProgressStore Challenges { get; }

        public JsonFileStateStore(string path, ProgressionConfig progression)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _state = File.Exists(path)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(path), JsonConfigSource.Options) ?? new State()
                : new State();
            Coins = new CoinStore(this);
            Profiles = new ProfileStore(this, progression);
            Rivalries = new RivalryStore(this);
            Challenges = new ChallengeStore(this);
        }

        public int PlayerCount { get { lock (_lock) return _state.Profiles.Count; } }

        private void MarkDirty() => _dirty = true;

        /// <summary>Call from the server tick; flushes when dirty and the interval elapsed.</summary>
        public void Tick()
        {
            if (!_dirty || DateTime.UtcNow - _lastFlush < FlushInterval) return;
            Flush();
        }

        public void Flush()
        {
            string json;
            lock (_lock)
            {
                if (!_dirty) return;
                json = JsonSerializer.Serialize(_state, JsonConfigSource.Options);
                _dirty = false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)) ?? ".");
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
            _lastFlush = DateTime.UtcNow;
        }

        public void Dispose() => Flush();

        private sealed class CoinStore : ICoinStore
        {
            private readonly JsonFileStateStore _s;
            private readonly HashSet<string> _keys = new HashSet<string>();

            public CoinStore(JsonFileStateStore s)
            {
                _s = s;
                foreach (var tx in s._state.Transactions) _keys.Add(tx.IdempotencyKey);
            }

            public bool TryGetBalance(string playerId, out long balance) { lock (_s._lock) return _s._state.Balances.TryGetValue(playerId, out balance); }
            public void SetBalance(string playerId, long balance) { lock (_s._lock) { _s._state.Balances[playerId] = balance; _s.MarkDirty(); } }
            public bool HasIdempotencyKey(string key) { lock (_s._lock) return _keys.Contains(key); }
            public void Append(CoinTransaction tx) { lock (_s._lock) { _keys.Add(tx.IdempotencyKey); _s._state.Transactions.Add(tx); _s.MarkDirty(); } }

            public IReadOnlyList<CoinTransaction> History(string playerId)
            {
                lock (_s._lock)
                {
                    var list = new List<CoinTransaction>();
                    foreach (var tx in _s._state.Transactions) if (tx.PlayerId == playerId) list.Add(tx);
                    return list;
                }
            }
        }

        private sealed class ProfileStore : IProfileStore
        {
            private readonly JsonFileStateStore _s;
            private readonly ProgressionConfig _cfg;

            public ProfileStore(JsonFileStateStore s, ProgressionConfig cfg) { _s = s; _cfg = cfg; }

            public PlayerProfile Get(string playerId)
            {
                lock (_s._lock)
                {
                    if (!_s._state.Profiles.TryGetValue(playerId, out var p))
                    {
                        p = new PlayerProfile { PlayerId = playerId, DisplayName = playerId, Rating = _cfg.ratingStart };
                        _s._state.Profiles[playerId] = p;
                        _s.MarkDirty();
                    }
                    return p;
                }
            }

            public void Save(PlayerProfile profile) { lock (_s._lock) { _s._state.Profiles[profile.PlayerId] = profile; _s.MarkDirty(); } }

            public IEnumerable<PlayerProfile> All()
            {
                lock (_s._lock) return new List<PlayerProfile>(_s._state.Profiles.Values);
            }
        }

        private sealed class RivalryStore : IRivalryStore
        {
            private readonly JsonFileStateStore _s;
            public RivalryStore(JsonFileStateStore s) { _s = s; }

            public Rivalry GetOrCreate(string a, string b)
            {
                lock (_s._lock)
                {
                    string key = Rivalry.KeyFor(a, b);
                    if (!_s._state.Rivalries.TryGetValue(key, out var r))
                    {
                        r = Rivalry.Create(a, b);
                        _s._state.Rivalries[key] = r;
                        _s.MarkDirty();
                    }
                    return r;
                }
            }

            public void Save(Rivalry rivalry) { lock (_s._lock) { _s._state.Rivalries[Rivalry.KeyFor(rivalry.PlayerA, rivalry.PlayerB)] = rivalry; _s.MarkDirty(); } }

            public IEnumerable<Rivalry> For(string playerId)
            {
                lock (_s._lock)
                {
                    var list = new List<Rivalry>();
                    foreach (var r in _s._state.Rivalries.Values) if (r.PlayerA == playerId || r.PlayerB == playerId) list.Add(r);
                    return list;
                }
            }
        }

        private sealed class ChallengeStore : IChallengeProgressStore
        {
            private readonly JsonFileStateStore _s;
            public ChallengeStore(JsonFileStateStore s) { _s = s; }

            public List<ChallengeProgress> Get(string playerId)
            {
                lock (_s._lock)
                {
                    if (!_s._state.Challenges.TryGetValue(playerId, out var l)) { l = new List<ChallengeProgress>(); _s._state.Challenges[playerId] = l; }
                    return l;
                }
            }

            public void Save(string playerId, List<ChallengeProgress> progress) { lock (_s._lock) { _s._state.Challenges[playerId] = progress; _s.MarkDirty(); } }
        }
    }
}
