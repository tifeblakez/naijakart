using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NaijaKart.Core.Net;
using Newtonsoft.Json;

namespace NaijaKart.Unity.Net
{
    /// <summary>
    /// Client side of the length-prefixed JSON/TCP transport (mirrors Server/Transport/TcpJsonTransport).
    /// Socket I/O happens on background tasks; MessageReceived fires on the Unity main thread from
    /// PumpIncoming(). Swappable for a UDP transport behind IClientTransport (ADR-0003).
    /// </summary>
    public sealed class TcpJsonClientTransport : IClientTransport, IDisposable
    {
        private const int MaxFrameBytes = 256 * 1024;
        private readonly string _host;
        private readonly int _port;
        private readonly ConcurrentQueue<ServerEnvelope> _inbox = new ConcurrentQueue<ServerEnvelope>();
        private readonly ConcurrentQueue<string> _disconnects = new ConcurrentQueue<string>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private volatile bool _connected;
        private volatile bool _connectedPending;

        public bool IsConnected => _connected;
        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<ServerEnvelope> MessageReceived;

        public TcpJsonClientTransport(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Connect()
        {
            if (_connected) return;
            _cts = new CancellationTokenSource();
            _ = ConnectAsync(_cts.Token);
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            try
            {
                _client = new TcpClient { NoDelay = true };
                await _client.ConnectAsync(_host, _port);
                _stream = _client.GetStream();
                _connected = true;
                _connectedPending = true;
                _ = ReadLoop(ct);
            }
            catch (Exception ex)
            {
                _disconnects.Enqueue("connect failed: " + ex.Message);
            }
        }

        private async Task ReadLoop(CancellationToken ct)
        {
            var header = new byte[4];
            try
            {
                while (!ct.IsCancellationRequested && _connected)
                {
                    if (!await ReadExact(header, 4)) break;
                    int len = BitConverter.ToInt32(header, 0);
                    if (len <= 0 || len > MaxFrameBytes) break;
                    var body = new byte[len];
                    if (!await ReadExact(body, len)) break;
                    try
                    {
                        var msg = JsonConvert.DeserializeObject<ServerEnvelope>(Encoding.UTF8.GetString(body), StreamingAssetsConfigSource.JsonSettings);
                        if (msg != null) _inbox.Enqueue(msg);
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception) { }
            if (_connected)
            {
                _connected = false;
                _disconnects.Enqueue("connection lost");
            }
        }

        private async Task<bool> ReadExact(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = await _stream.ReadAsync(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        public void Disconnect()
        {
            if (!_connected && _client == null) return;
            _connected = false;
            _cts?.Cancel();
            try { _client?.Close(); } catch { }
            _client = null;
            _disconnects.Enqueue("client");
        }

        public void Send(ClientEnvelope message)
        {
            if (!_connected || _stream == null) return;
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, StreamingAssetsConfigSource.JsonSettings));
            var frame = new byte[4 + body.Length];
            BitConverter.GetBytes(body.Length).CopyTo(frame, 0);
            body.CopyTo(frame, 4);
            _ = SendAsync(frame);
        }

        private async Task SendAsync(byte[] frame)
        {
            await _sendLock.WaitAsync();
            try { if (_stream != null) await _stream.WriteAsync(frame, 0, frame.Length); }
            catch (Exception) { Disconnect(); }
            finally { _sendLock.Release(); }
        }

        public void PumpIncoming()
        {
            if (_connectedPending) { _connectedPending = false; Connected?.Invoke(); }
            while (_inbox.TryDequeue(out var msg)) MessageReceived?.Invoke(msg);
            while (_disconnects.TryDequeue(out var reason)) Disconnected?.Invoke(reason);
        }

        public void Dispose() => Disconnect();
    }
}
