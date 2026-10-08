using NaijaKart.Core.Net;
using UnityEngine;

namespace NaijaKart.Unity.Net
{
    /// <summary>
    /// Runs an authoritative RaceSimulation in-process for offline practice and the driving prototype
    /// (Phase 1). It speaks the real wire protocol over a LoopbackTransportHub, so the client code path
    /// is identical to online play and nothing competitive is ever trusted from the presentation
    /// layer. Practice rewards are not persisted: the server owns the ledger.
    /// </summary>
    public sealed class LocalPracticeHost : MonoBehaviour
    {
        [SerializeField] private string _trackId = "third_mainland_rush";
        [SerializeField] private int _laps = 3;
        [SerializeField] private int _bots = 7;
        [SerializeField] private bool _items = true;
        [SerializeField] private bool _lastma = true;
        [SerializeField] private bool _roadEvents = true;

        private LoopbackTransportHub _hub;
        private PracticeServer _server;
        private float _accumulator;
        private float _dt;

        public bool IsRunning => _server != null;
        public PracticeServer Server => _server;

        public void StartPractice()
        {
            var content = GameBootstrap.Content;
            _hub = new LoopbackTransportHub((ulong)System.Environment.TickCount);
            _server = new PracticeServer(content, _hub.Server, _trackId, _laps, _bots, _items, _lastma, _roadEvents);
            _dt = 1f / content.Game.simulation.tickRate;
            NetworkClient.Instance.Connect(_hub.CreateClient(PlayerSettingsStore.PlayerId));
        }

        private void Update()
        {
            if (_server == null) return;
            _accumulator += Time.unscaledDeltaTime;
            int steps = 0;
            while (_accumulator >= _dt && steps++ < 8)
            {
                _accumulator -= _dt;
                _hub.Advance(_dt);
                _server.Tick();
            }
        }

        public void Stop()
        {
            if (_server == null) return;
            NetworkClient.Instance?.Disconnect();
            _server = null;
            _hub = null;
        }

        private void OnDestroy() => Stop();
    }
}
