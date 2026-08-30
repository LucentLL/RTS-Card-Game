#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// ClientWebSocket, for the editor and every native player.
    ///
    /// The threads are entirely inside this class. Two tasks - one sending, one receiving,
    /// because ClientWebSocket permits exactly one of each concurrently - move bytes between the
    /// socket and two concurrent queues, and the game thread only ever touches the queues. So the
    /// layers above stay single-threaded and pumped, and nothing above <see cref="IWebSocket"/>
    /// has to know a thread exists.
    /// </summary>
    public sealed class SystemWebSocket : IWebSocket
    {
        readonly ConcurrentQueue<byte[]> _inbound = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<byte[]> _outbound = new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim _outboundReady = new SemaphoreSlim(0);
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        ClientWebSocket _ws;
        volatile int _state = (int)SocketState.Closed;
        volatile string _error;

        public SocketState State { get { return (SocketState)_state; } }
        public string LastError { get { return _error; } }

        public void Connect(string url, string subProtocol)
        {
            if (_state != (int)SocketState.Closed) return;
            _state = (int)SocketState.Connecting;

            _ws = new ClientWebSocket();
            if (!string.IsNullOrEmpty(subProtocol))
                _ws.Options.AddSubProtocol(subProtocol);
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            Task.Run(async delegate
            {
                try
                {
                    await _ws.ConnectAsync(new Uri(url), _cancel.Token).ConfigureAwait(false);
                    _state = (int)SocketState.Open;
                }
                catch (Exception e)
                {
                    Fail(Root(e));
                    return;
                }

                var send = SendLoop();
                var receive = ReceiveLoop();
                await Task.WhenAny(send, receive).ConfigureAwait(false);
            });
        }

        async Task SendLoop()
        {
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    await _outboundReady.WaitAsync(_cancel.Token).ConfigureAwait(false);

                    byte[] bytes;
                    while (_outbound.TryDequeue(out bytes))
                    {
                        await _ws.SendAsync(new ArraySegment<byte>(bytes),
                                            WebSocketMessageType.Binary, true, _cancel.Token)
                                 .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Fail(Root(e)); }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (!_cancel.IsCancellationRequested
                       && _ws.State == WebSocketState.Open)
                {
                    // One WebSocket message may arrive in several frames; join them before
                    // handing anything up, because an MQTT packet must not be split by our own
                    // buffering choices.
                    var whole = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token)
                                          .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Fail("the relay closed the connection");
                            return;
                        }
                        whole.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (whole.Length > 0) _inbound.Enqueue(whole.ToArray());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Fail(Root(e)); }
        }

        void Fail(string why)
        {
            _error = why;
            _state = (int)SocketState.Failed;
        }

        static string Root(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e.Message;
        }

        public void Send(byte[] bytes)
        {
            if (_state == (int)SocketState.Failed) return;
            _outbound.Enqueue(bytes);
            try { _outboundReady.Release(); } catch (ObjectDisposedException) { }
        }

        public byte[] Receive()
        {
            byte[] bytes;
            return _inbound.TryDequeue(out bytes) ? bytes : null;
        }

        public void Close()
        {
            if (_state == (int)SocketState.Closed) return;
            _state = (int)SocketState.Closed;
            try { _cancel.Cancel(); } catch (Exception) { }
            try { if (_ws != null) _ws.Abort(); } catch (Exception) { }
        }

        public void Dispose()
        {
            Close();
            try { if (_ws != null) _ws.Dispose(); } catch (Exception) { }
            try { _cancel.Dispose(); } catch (Exception) { }
            try { _outboundReady.Dispose(); } catch (Exception) { }
        }
    }
}
#endif
