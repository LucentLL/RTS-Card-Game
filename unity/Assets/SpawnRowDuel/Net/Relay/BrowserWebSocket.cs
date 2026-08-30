#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// The browser's WebSocket, reached through Plugins/WebGL/SrdWebSocket.jslib.
    ///
    /// A WebAssembly player has no sockets, so this is not an optimisation - it is the only way
    /// the web build can reach a relay at all. The jslib deliberately exposes a POLLED surface
    /// rather than calling back into C#: everything above here is pumped once a frame, and a
    /// callback arriving mid-frame would be the one piece of the netcode that could not be
    /// reasoned about the same way as the rest.
    /// </summary>
    public sealed class BrowserWebSocket : IWebSocket
    {
        [DllImport("__Internal")] static extern int SrdWsOpen(string url, string protocol);
        [DllImport("__Internal")] static extern int SrdWsState(int id);
        [DllImport("__Internal")] static extern int SrdWsCloseCode(int id);
        [DllImport("__Internal")] static extern int SrdWsSend(int id, byte[] data, int length);
        [DllImport("__Internal")] static extern int SrdWsPeek(int id);
        [DllImport("__Internal")] static extern int SrdWsTake(int id, byte[] into, int max);
        [DllImport("__Internal")] static extern void SrdWsClose(int id);

        int _id;
        SocketState _lastState = SocketState.Closed;

        public string LastError { get; private set; }

        public SocketState State
        {
            get
            {
                if (_id == 0) return _lastState;
                var s = (SocketState)SrdWsState(_id);
                if (s == SocketState.Failed && _lastState != SocketState.Failed)
                {
                    int code = SrdWsCloseCode(_id);
                    LastError = code == 0 ? "the browser could not reach the relay"
                                          : "the relay closed the connection (" + code + ")";
                }
                _lastState = s;
                return s;
            }
        }

        public void Connect(string url, string subProtocol)
        {
            if (_id != 0) return;
            _id = SrdWsOpen(url, subProtocol ?? "");
            if (_id == 0)
            {
                _lastState = SocketState.Failed;
                LastError = "the browser refused to open a socket";
            }
        }

        public void Send(byte[] bytes)
        {
            if (_id == 0) return;
            SrdWsSend(_id, bytes, bytes.Length);
        }

        public byte[] Receive()
        {
            if (_id == 0) return null;
            int size = SrdWsPeek(_id);
            if (size <= 0) return null;

            var buffer = new byte[size];
            int got = SrdWsTake(_id, buffer, size);
            return got == size ? buffer : null;
        }

        public void Close()
        {
            if (_id == 0) return;
            SrdWsClose(_id);
            _id = 0;
            _lastState = SocketState.Closed;
        }

        public void Dispose() { Close(); }
    }
}
#endif
