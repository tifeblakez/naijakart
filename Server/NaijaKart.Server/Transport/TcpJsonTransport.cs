using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NaijaKart.Core.Net;
using NaijaKart.Core.Util;
using NaijaKart.Server.Config;

namespace NaijaKart.Server.Transport
{
    /// <summary>
    /// Length-prefixed JSON over TCP. The first real transport: simple, debuggable, usable from Unity
    /// with System.Net.Sockets. All socket I/O runs on background tasks; events are queued and
    /// delivered on the server thread by PumpIncoming(). ADR-0003 covers the planned UDP upgrade;
    /// nothing above this class depends on TCP.
    /// </summary>
    public sealed class TcpJsonServerTransport : IServerTransport, IDisposable
    {
        private const int MaxFrameBytes = 256 * 1024;

        private sealed class Connection
        {
            public string Id;
            public TcpClient Client;
            public NetworkStream Stream;
            public SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
            public volatile bool Closed;
        }

        private readonly TcpListener _listener;
        private readonly ILogger _log;
        private readonly ConcurrentDictionary<string, Connection> _connections = new ConcurrentDictionary<string, Connection>();
        private readonly ConcurrentQueue<Action> _inbox = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _nextId;

        public event Action<string> ClientConnected;
        public event Action<string> ClientDisconnected;
        public event Action<string, ClientEnvelope> MessageReceived;

        public int Port { get; private set; }

        public TcpJsonServerTransport(int port, ILogger log)
        {
            _log = log;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoop();
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.Warn("tcp", "accept failed: " + ex.Message); continue; }
                client.NoDelay = true;
                var conn = new Connection { Id = "c" + Interlocked.Increment(ref _nextId), Client = client, Stream = client.GetStream() };
                _connections[conn.Id] = conn;
                _inbox.Enqueue(() => ClientConnected?.Invoke(conn.Id));
                _ = ReadLoop(conn);
            }
        }

        private async Task ReadLoop(Connection conn)
        {
            var header = new byte[4];
            try
            {
                while (!_cts.IsCancellationRequested && !conn.Closed)
                {
                    if (!await ReadExact(conn.Stream, header, 4)) break;
                    int len = BitConverter.ToInt32(header, 0);
                    if (len <= 0 || len > MaxFrameBytes) break;
                    var body = new byte[len];
                    if (!await ReadExact(conn.Stream, body, len)) break;
                    ClientEnvelope msg;
                    try { msg = JsonSerializer.Deserialize<ClientEnvelope>(body, JsonConfigSource.Options); }
                    catch (JsonException) { continue; }
                    if (msg == null) continue;
                    _inbox.Enqueue(() => MessageReceived?.Invoke(conn.Id, msg));
                }
            }
            catch (Exception) { /* connection dropped */ }
            Close(conn);
        }

        private static async Task<bool> ReadExact(NetworkStream s, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private void Close(Connection conn)
        {
            if (conn.Closed) return;
            conn.Closed = true;
            try { conn.Client.Close(); } catch { }
            _connections.TryRemove(conn.Id, out _);
            _inbox.Enqueue(() => ClientDisconnected?.Invoke(conn.Id));
        }

        public void Send(string connectionId, ServerEnvelope message)
        {
            if (_connections.TryGetValue(connectionId, out var conn)) _ = SendAsync(conn, Encode(message));
        }

        public void Broadcast(ServerEnvelope message)
        {
            var bytes = Encode(message);
            foreach (var conn in _connections.Values) _ = SendAsync(conn, bytes);
        }

        private static byte[] Encode(ServerEnvelope message)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonConfigSource.Options);
            var frame = new byte[4 + body.Length];
            BitConverter.GetBytes(body.Length).CopyTo(frame, 0);
            body.CopyTo(frame, 4);
            return frame;
        }

        private async Task SendAsync(Connection conn, byte[] frame)
        {
            if (conn.Closed) return;
            await conn.SendLock.WaitAsync();
            try { await conn.Stream.WriteAsync(frame, 0, frame.Length); }
            catch (Exception) { Close(conn); }
            finally { conn.SendLock.Release(); }
        }

        public void Disconnect(string connectionId, string reason)
        {
            if (_connections.TryGetValue(connectionId, out var conn)) Close(conn);
        }

        public void PumpIncoming()
        {
            while (_inbox.TryDequeue(out var action)) action();
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            foreach (var c in _connections.Values) Close(c);
        }
    }
}
