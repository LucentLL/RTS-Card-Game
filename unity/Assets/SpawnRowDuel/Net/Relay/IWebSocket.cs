using System;

namespace SpawnRowDuel.Net
{
    public enum SocketState : byte { Closed = 0, Connecting = 1, Open = 2, Failed = 3 }

    /// <summary>
    /// A binary WebSocket, reduced to what a pumped main loop needs: open it, push bytes at it,
    /// take bytes off it, ask how it is doing. No async, no callbacks, no threads visible to the
    /// caller - each implementation hides its own.
    ///
    /// Two implementations exist because there is no single API that works on both targets:
    /// desktop and the editor use ClientWebSocket, and WebGL uses the browser's own WebSocket
    /// through a jslib, because a WebAssembly build has no sockets at all. The MQTT client above
    /// never learns which it has.
    /// </summary>
    public interface IWebSocket : IDisposable
    {
        SocketState State { get; }
        string LastError { get; }

        void Connect(string url, string subProtocol);

        void Send(byte[] bytes);

        /// <summary>The next received message, or null. Binary frames only.</summary>
        byte[] Receive();

        void Close();
    }

    /// <summary>Builds sockets. Injected so the tests can supply one that never touches a network.</summary>
    public interface IWebSocketFactory
    {
        IWebSocket Create();
    }
}
