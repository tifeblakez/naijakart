using System;

namespace NaijaKart.Core.Net
{
    /// <summary>
    /// Server-side transport abstraction. The production implementation wraps a UDP library; tests and
    /// local play use LoopbackTransportHub. All callbacks are raised on the server's tick thread via
    /// PumpIncoming(), never from socket threads, so the simulation stays single-threaded.
    /// </summary>
    public interface IServerTransport
    {
        event Action<string> ClientConnected;
        event Action<string> ClientDisconnected;
        event Action<string, ClientEnvelope> MessageReceived;
        void Send(string connectionId, ServerEnvelope message);
        void Broadcast(ServerEnvelope message);
        void Disconnect(string connectionId, string reason);
        /// <summary>Dispatches queued inbound messages/connection events on the calling thread.</summary>
        void PumpIncoming();
    }

    public interface IClientTransport
    {
        bool IsConnected { get; }
        event Action Connected;
        event Action<string> Disconnected;
        event Action<ServerEnvelope> MessageReceived;
        void Connect();
        void Disconnect();
        void Send(ClientEnvelope message);
        void PumpIncoming();
    }
}
