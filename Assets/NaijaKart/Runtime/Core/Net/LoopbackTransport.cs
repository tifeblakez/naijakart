using System;
using System.Collections.Generic;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.Net
{
    /// <summary>
    /// In-process transport with simulated latency, jitter, packet loss and disconnects. Used for tests
    /// of the PRD §66 network matrix and for local single-device play (client and server in one process).
    /// Time is driven explicitly through Advance(dt) so tests are deterministic.
    /// </summary>
    public sealed class LoopbackTransportHub
    {
        public sealed class NetworkConditions
        {
            public float LatencySeconds;
            public float JitterSeconds;
            /// <summary>0..1 probability that any given message is dropped.</summary>
            public float PacketLoss;
        }

        private struct Pending<T>
        {
            public float DeliverAt;
            public T Message;
        }

        private readonly LoopbackServerTransport _server;
        private readonly Dictionary<string, LoopbackClientTransport> _clients = new Dictionary<string, LoopbackClientTransport>();
        private readonly DeterministicRandom _rng;
        private float _now;

        public NetworkConditions Conditions { get; } = new NetworkConditions();
        public IServerTransport Server => _server;
        public float Now => _now;

        public LoopbackTransportHub(ulong seed = 7)
        {
            _rng = new DeterministicRandom(seed);
            _server = new LoopbackServerTransport(this);
        }

        public IClientTransport CreateClient(string connectionId)
        {
            var c = new LoopbackClientTransport(this, connectionId);
            _clients[connectionId] = c;
            return c;
        }

        /// <summary>Advances the simulated network clock; messages whose delay elapsed become deliverable.</summary>
        public void Advance(float dt) => _now += dt;

        private float Delay()
        {
            float jitter = Conditions.JitterSeconds > 0f ? _rng.Range(-Conditions.JitterSeconds, Conditions.JitterSeconds) : 0f;
            return System.Math.Max(0f, Conditions.LatencySeconds + jitter);
        }

        private bool Dropped() => Conditions.PacketLoss > 0f && _rng.Chance(Conditions.PacketLoss);

        // ---- server side ----
        private sealed class LoopbackServerTransport : IServerTransport
        {
            private readonly LoopbackTransportHub _hub;
            private readonly List<Pending<KeyValuePair<string, ClientEnvelope>>> _inbox = new List<Pending<KeyValuePair<string, ClientEnvelope>>>();
            private readonly List<Pending<string>> _connects = new List<Pending<string>>();
            private readonly List<Pending<string>> _disconnects = new List<Pending<string>>();

            public event Action<string> ClientConnected;
            public event Action<string> ClientDisconnected;
            public event Action<string, ClientEnvelope> MessageReceived;

            public LoopbackServerTransport(LoopbackTransportHub hub)
            {
                _hub = hub;
            }

            internal void QueueConnect(string id) => _connects.Add(new Pending<string> { DeliverAt = _hub._now + _hub.Delay(), Message = id });
            internal void QueueDisconnect(string id) => _disconnects.Add(new Pending<string> { DeliverAt = _hub._now + _hub.Delay(), Message = id });

            internal void QueueMessage(string id, ClientEnvelope msg)
            {
                if (_hub.Dropped()) return;
                _inbox.Add(new Pending<KeyValuePair<string, ClientEnvelope>>
                {
                    DeliverAt = _hub._now + _hub.Delay(),
                    Message = new KeyValuePair<string, ClientEnvelope>(id, msg)
                });
            }

            public void Send(string connectionId, ServerEnvelope message)
            {
                if (_hub._clients.TryGetValue(connectionId, out var c) && c.IsConnected) c.QueueFromServer(message);
            }

            public void Broadcast(ServerEnvelope message)
            {
                foreach (var c in _hub._clients.Values) if (c.IsConnected) c.QueueFromServer(message);
            }

            public void Disconnect(string connectionId, string reason)
            {
                if (_hub._clients.TryGetValue(connectionId, out var c)) c.ForceDisconnect(reason);
            }

            public void PumpIncoming()
            {
                Deliver(_connects, id => ClientConnected?.Invoke(id));
                Deliver(_inbox, kv => MessageReceived?.Invoke(kv.Key, kv.Value));
                Deliver(_disconnects, id => ClientDisconnected?.Invoke(id));
            }

            private void Deliver<T>(List<Pending<T>> list, Action<T> handler)
            {
                // Preserve order per sender: stable sort by DeliverAt.
                list.Sort((a, b) => a.DeliverAt.CompareTo(b.DeliverAt));
                int delivered = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].DeliverAt > _hub._now) break;
                    handler(list[i].Message);
                    delivered++;
                }
                if (delivered > 0) list.RemoveRange(0, delivered);
            }
        }

        // ---- client side ----
        private sealed class LoopbackClientTransport : IClientTransport
        {
            private readonly LoopbackTransportHub _hub;
            private readonly string _id;
            private readonly List<Pending<ServerEnvelope>> _inbox = new List<Pending<ServerEnvelope>>();
            private string _pendingDisconnectReason;

            public bool IsConnected { get; private set; }
            public event Action Connected;
            public event Action<string> Disconnected;
            public event Action<ServerEnvelope> MessageReceived;

            public LoopbackClientTransport(LoopbackTransportHub hub, string id)
            {
                _hub = hub;
                _id = id;
            }

            public void Connect()
            {
                if (IsConnected) return;
                IsConnected = true;
                _hub._server.QueueConnect(_id);
                Connected?.Invoke();
            }

            public void Disconnect()
            {
                if (!IsConnected) return;
                IsConnected = false;
                _inbox.Clear();
                _hub._server.QueueDisconnect(_id);
                Disconnected?.Invoke("client");
            }

            internal void ForceDisconnect(string reason)
            {
                if (!IsConnected) return;
                IsConnected = false;
                _inbox.Clear();
                _pendingDisconnectReason = reason ?? "server";
                _hub._server.QueueDisconnect(_id);
            }

            public void Send(ClientEnvelope message)
            {
                if (!IsConnected) return;
                if (message.PlayerId == null) message.PlayerId = _id;
                _hub._server.QueueMessage(_id, message);
            }

            internal void QueueFromServer(ServerEnvelope msg)
            {
                if (_hub.Dropped()) return;
                _inbox.Add(new Pending<ServerEnvelope> { DeliverAt = _hub._now + _hub.Delay(), Message = msg });
            }

            public void PumpIncoming()
            {
                if (_pendingDisconnectReason != null)
                {
                    string r = _pendingDisconnectReason;
                    _pendingDisconnectReason = null;
                    Disconnected?.Invoke(r);
                }
                _inbox.Sort((a, b) => a.DeliverAt.CompareTo(b.DeliverAt));
                int delivered = 0;
                for (int i = 0; i < _inbox.Count; i++)
                {
                    if (_inbox[i].DeliverAt > _hub._now) break;
                    MessageReceived?.Invoke(_inbox[i].Message);
                    delivered++;
                }
                if (delivered > 0) _inbox.RemoveRange(0, delivered);
            }
        }
    }
}
