using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Net
{
    public enum TransportStatus : byte
    {
        Idle = 0,
        Connecting = 1,
        Connected = 2,
        Retrying = 3,     // the link dropped; reconnecting, not yet given up
        Failed = 4,       // every relay refused or is unreachable
    }

    /// <summary>One message as the relay handed it back.</summary>
    public struct InboundMessage
    {
        public string Topic;
        public string Text;
    }

    /// <summary>
    /// The one thing the session knows about the outside world: named topics, text in, text out.
    ///
    /// Pull-based on purpose - no callbacks, no threads, no async, no wall clock of its own. The
    /// session calls Pump with a delta and then Poll for whatever arrived, which is the same
    /// shape whether the bytes came off a WebSocket or out of a list in a test. That is what lets
    /// an entire match - handshake, four hundred plies, a dropped peer, a reconnect, a desync -
    /// run inside the EditMode gate with no network and no player loop.
    ///
    /// Delivery is best-effort and at-least-once. The protocol above assumes nothing better:
    /// frames carry a global ply so duplicates are dropped and reordering is buffered, and a peer
    /// that fell behind is caught up from the other peer's own log rather than from any relay's
    /// memory. No implementation is required to retain anything.
    /// </summary>
    public interface IMessageTransport : IDisposable
    {
        /// <summary>Queue a message. Fire and forget.</summary>
        void Publish(string topic, string text);

        /// <summary>Begin receiving a topic. Idempotent.</summary>
        void Subscribe(string topic);

        void Unsubscribe(string topic);

        /// <summary>Everything that arrived since the last call, in arrival order. Never null.</summary>
        IList<InboundMessage> Poll();

        /// <summary>Drive the implementation: connect, retry, read sockets, flush the outbox.</summary>
        void Pump(double deltaSeconds);

        TransportStatus Status { get; }

        /// <summary>Human-readable, for the lobby. Null when nothing has gone wrong.</summary>
        string LastError { get; }

        /// <summary>Where we actually ended up - "broker.emqx.io", "loopback". The lobby shows it
        /// because "connected, but to what" is the first question when a link misbehaves.</summary>
        string Description { get; }
    }
}
