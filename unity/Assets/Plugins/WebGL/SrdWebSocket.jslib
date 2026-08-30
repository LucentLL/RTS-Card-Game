// The browser's own WebSocket, exposed to the WebGL player as a handful of pollable calls.
//
// A WebAssembly build has no sockets, so ClientWebSocket is not merely slow there - it does not
// exist. This is the whole of the WebGL half of the relay transport: open a socket, queue what
// arrives, hand it up when the game loop asks. Nothing here calls back into C#, because the
// layers above are pumped and a callback would land at an arbitrary point in a frame.

var SrdWebSocketLibrary = {

  $SRDWS: {
    sockets: {},
    next: 1,

    get: function (id) { return SRDWS.sockets[id]; },
  },

  // Returns a handle, or 0 if the browser refused to construct the socket at all.
  SrdWsOpen: function (urlPtr, protoPtr) {
    var url = UTF8ToString(urlPtr);
    var proto = UTF8ToString(protoPtr);
    var id = SRDWS.next++;

    var entry = { ws: null, queue: [], state: 1, code: 0 };   // 1 = connecting
    SRDWS.sockets[id] = entry;

    try {
      entry.ws = proto ? new WebSocket(url, proto) : new WebSocket(url);
    } catch (e) {
      entry.state = 3;                                        // 3 = failed
      return id;
    }

    entry.ws.binaryType = 'arraybuffer';

    entry.ws.onopen = function () { entry.state = 2; };       // 2 = open

    entry.ws.onmessage = function (ev) {
      if (typeof ev.data === 'string') return;                // MQTT is binary; ignore text
      entry.queue.push(new Uint8Array(ev.data));
    };

    entry.ws.onerror = function () {
      if (entry.state !== 0) entry.state = 3;
    };

    entry.ws.onclose = function (ev) {
      entry.code = ev && ev.code ? ev.code : 0;
      if (entry.state !== 0) entry.state = 3;
    };

    return id;
  },

  // 0 closed, 1 connecting, 2 open, 3 failed
  SrdWsState: function (id) {
    var e = SRDWS.get(id);
    return e ? e.state : 0;
  },

  SrdWsCloseCode: function (id) {
    var e = SRDWS.get(id);
    return e ? e.code : 0;
  },

  SrdWsSend: function (id, ptr, len) {
    var e = SRDWS.get(id);
    if (!e || e.state !== 2) return 0;
    try {
      // A copy, not a view: HEAPU8 can be detached by a later growth of the heap, and the
      // browser may not have finished with the buffer by then.
      e.ws.send(HEAPU8.slice(ptr, ptr + len));
      return 1;
    } catch (err) {
      e.state = 3;
      return 0;
    }
  },

  // Size of the next queued message, or 0 when there is nothing waiting.
  SrdWsPeek: function (id) {
    var e = SRDWS.get(id);
    return e && e.queue.length ? e.queue[0].length : 0;
  },

  // Copy the next message out and drop it. Returns how many bytes were written.
  SrdWsTake: function (id, ptr, max) {
    var e = SRDWS.get(id);
    if (!e || !e.queue.length) return 0;
    var msg = e.queue[0];
    if (msg.length > max) return 0;                           // caller must size from SrdWsPeek
    e.queue.shift();
    HEAPU8.set(msg, ptr);
    return msg.length;
  },

  SrdWsClose: function (id) {
    var e = SRDWS.get(id);
    if (!e) return;
    e.state = 0;
    try { if (e.ws) e.ws.close(); } catch (err) { }
    delete SRDWS.sockets[id];
  },
};

autoAddDeps(SrdWebSocketLibrary, '$SRDWS');
mergeInto(LibraryManager.library, SrdWebSocketLibrary);
