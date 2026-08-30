# Design 04 — Netcode: password-linked 1v1 over no server of ours

M17. The JS build shipped multiplayer twice (`c72bd95` host-authoritative WebRTC snapshots,
`fd1fe44` the public-broker relay that is what actually worked); PORT_PLAN discarded
`40_mp_net.js` on purpose and said netcode would be "layered on the command pipeline later,
from scratch". This is that layer. It reuses none of the JS wire format and all of its
hard-won operational lessons.

## 0. What "no server" means here, honestly

A pure serverless link between two arbitrary machines does not exist. Two peers behind
consumer NAT cannot find each other without a rendezvous, and when hole-punching fails the
only fix is a relay. The JS build proved both halves of that the hard way: the user's own
PC-to-mobile pair failed to connect **on shared wifi**, and every free public TURN server was
verified dead (2026-07-10).

So the promise this design keeps is the one that matters: **there is no server we run, deploy,
pay for, or can take down.** Two peers meet on free public pub/sub infrastructure, and the
relay never learns anything — every byte it carries is sealed with a key derived from the
password, and the topic it is filed under is a one-way hash of that same password. The relay
sees opaque ciphertext on a random-looking topic name.

Direct WebRTC is deliberately **not** attempted. It is two implementations (a native package
that does not support WebGL, plus a browser `.jslib`), it still needs this exact rendezvous to
sign, and the one time this user tested it end to end it did not connect. It stays a possible
third `IMessageTransport` behind the same interface, worth adding only if the relay's latency
ever becomes the complaint. `SteamNetworkingSockets` is the more likely second transport —
free relay included, and PC/Steam is the shipping target (M16).

## 1. Sync model — deterministic command lockstep, not snapshots

The JS was host-authoritative: the host held canonical state, the guest sent intents, and
after every change the host broadcast a **full 25-40 KB snapshot** which the guest adopted
wholesale with a you-foe perspective swap. That design existed because the JS engine was not
deterministic and its state was not reconstructible — the snapshot *was* the desync heal.

The port has the opposite problem, which is to say no problem. M12 proved this engine is
bit-deterministic: 10,000 random legal commands across 25 matches and 6 commander pairings
replay with zero divergence, the RNG stream position is part of serialized state, and
`StateCodec.Hash` collapses a whole match to 64 bits. So:

> **Both peers run the same engine from the same seed and apply the same command stream.
> The wire carries commands, never state.**

What this buys, in order of importance:

1. **A frame is ~10 bytes.** The snapshot model's 25-40 KB per change is unaffordable over a
   public relay with per-visitor rate limits; a command stream is affordable over anything.
2. **Desync detection is free and exact.** Every frame carries the sender's state hash *before*
   applying. A receiver whose hash differs knows immediately, and knows it at the ply.
3. **Reconnect is replay.** The log is small enough that each peer simply keeps it, so a peer
   that drops is handed the whole match back by the one that did not and replays into it.
   There is no resync protocol to write and no snapshot codec to maintain — which is also why
   `StateCodec` still needs no read side.
4. **A match *is* a testable artifact.** Seed + deck lists + command log is a complete,
   replayable match in a few hundred bytes. That is exactly the shape `tools/diffjs/` already
   speaks, so recorded multiplayer games drop straight into the differential harness. Given
   that this mode exists partly to serve balance testing, this is not a side benefit.

**No host authority is needed for ordering**, because the rules very nearly serialize it
already: a parked `PendingRequest` rejects every command except a `RespondCommand` from
`Pending.Responder`, and otherwise each handler gates on the phase and on whose turn it is. So
two peers cannot normally produce interleaving commands, and a peer may apply its own command
**optimistically and immediately** — your own taps have zero network latency — while the remote
peer applies it on arrival into the same slot in the same order.

"Very nearly", not "always", and the exception is load-bearing. `GameState.IsInteractive` reads
like the invariant that guarantees this, and it excludes the End phase exactly as it should —
but **nothing calls it**; `CommandProcessor.CanExecute` gates on `IsOver` and `Pending` only.
The invariant is emergent from the per-handler gates, and `SendBankedManaHandler` deliberately
has no phase gate. So at `Phase == End` the OUTGOING side can still legally move a bank at the
same moment the INCOMING side is told to begin its turn: two legal commands, from two peers, on
one state, that do not commute. Not theoretical — the M12 fuzz corpus contains **218 commands
landing between an EndTurn and the next BeginTurn, and every one of them is a SendBankedMana**.

`NetSession.LocalGate` closes it: in a networked match, at `Phase == End` with nothing parked,
the only command a peer may submit is its own `BeginTurn`. The fix lives in the netcode rather
than in `SendBankedManaHandler` because adding a phase gate there would change the game and
force the M12 golden corpus to be re-cut — a rules decision that belongs to M16. Solo play is
untouched. See DECISIONS D20.

### 1.1 The trade this makes

Lockstep means both peers hold the whole state, including the opponent's hand and deck order.
A determined opponent with a debugger can read it. This is the same friend-play model the JS
shipped and documented, and for a game whose multiplayer exists to let two people who know
each other play and to generate balance data, it is the right trade — it buys the four
properties above at the cost of an attack only a cheater who already has your password can
mount.

It is not a dead end. The upgrade path is host-authoritative redaction: the host runs the only
true state, the guest runs a redacted mirror, and hidden zones sync as counts. That needs
`StateCodec`'s read side plus a redaction pass, and it can be built later **without touching
the transport, the crypto, the lobby, or the view** — which is why those four are the layers
this milestone actually builds.

## 2. Layering

```
SpawnRowDuel.Net            noEngineReferences: true, references Rules only, zero packages
  Crypto/     SHA-256, HMAC, HKDF, PBKDF2, ChaCha20-Poly1305 — all hand-written
  Wire/       varint framing, ICommand codec, message envelopes, MatchConfig
  Session/    handshake + lockstep + desync state machine, behind IMessageTransport
  Relay/      MQTT 3.1.1 over WebSocket, several public brokers at once, both socket backends
SpawnRowDuel.View           Seat, the lobby screen, and MatchController's Submit/Probe funnel
```

`SpawnRowDuel.Net` is engine-free for the same reason `Rules` is: the whole protocol — two
peers, a full handshake, a whole match, packet loss, reordering, duplicate delivery, a
poisoned frame, a desync — runs inside the EditMode gate against an in-memory
`LoopbackTransport`, with no network, no Unity player, and no wall clock. **The gate for this
milestone is an entire AI-vs-AI match played through the protocol between two independent
engines, asserting equal hashes at every ply.**

### 2.1 Why the crypto is hand-written

`apiCompatibilityLevel: 6` is .NET Standard 2.1, which has no `AesGcm`, and the managed crypto
that *is* nominally present has a history of platform-specific behaviour under IL2CPP and
WebGL — the two backends this has to work on. A hand-written SHA-256 / HMAC / HKDF / PBKDF2 /
ChaCha20-Poly1305 has no platform surface at all, is ~450 lines, and is pinned to the RFC test
vectors (6234, 6070, 8439) inside the same EditMode gate as everything else. This matches the
choice `StateCodec` already made and for the same reason: a package bump must not be able to
silently change bytes we depend on.

## 3. The password

One shared secret does three jobs, and must not leak between them.

```
root      = PBKDF2-HMAC-SHA256(password, salt = "srd.mp.v2", 60_000 iterations) -> 32 bytes
topicId   = HKDF(root, info = "topic")  -> 10 bytes -> base32  (PUBLIC: it names the channel)
sealKey   = HKDF(root, info = "seal")   -> 32 bytes            (SECRET: never leaves the peer)
```

The relay learns `topicId` and nothing else; `topicId` is a one-way function of the password
and cannot be walked back to it. Channels: `srd2-<topicId>-h` (host to guest) and
`srd2-<topicId>-g` (guest to host), so neither peer has to filter out its own echo.

Every frame is `ChaCha20-Poly1305(sealKey, nonce = random 12 bytes)` with the protocol version
and the sender's role as associated data. A wrong password therefore fails as an **auth-tag
mismatch, not as a garbled parse** — the difference between "Wrong password" and a crash, and
the same clean failure the JS build got right.

## 4. Protocol

`PROTO = 1`. A version mismatch is refused at the handshake with a message naming both
versions, never negotiated — two builds with different rules must not attempt to lockstep.

### 4.1 Handshake (three messages, seed contributed by both)

| # | From | Message | Carries |
|---|---|---|---|
| 1 | host | `Hello` | proto, hostNonce, commander, deck list, `RulesOptions.FlagBits` |
| 2 | guest | `Join` | proto, guestNonce, commander, deck list |
| 3 | host | `Start` | the resolved `MatchConfig` + its config hash |

`seed = SHA-256(hostNonce || guestNonce)[0..8]`. Neither side can grind a favourable shuffle,
because neither picks the seed alone. Both peers then call the *same*
`MatchSetup.NewMatch(cat, hostCmdr, guestCmdr, hostDeck, guestDeck, seed, options)` and assert
their opening `Hash()` agree — a mismatch here means mismatched card data or build, and is
reported as that rather than as a desync ten plies later.

Deck lists cross the wire as `DeckKey` strings; an unknown key is refused at the handshake,
which is how a peer running stale card data is caught before it can matter.

### 4.2 Steady state

```
Frame { seq: varint, hashBefore: u64, command: bytes }
```

`seq` is per-sender and monotonic: the receiver drops duplicates and buffers anything ahead of
its cursor, which is what makes an at-least-once, occasionally-reordering relay safe. Applying
a frame:

1. reject unless `frame.hashBefore == myEngine.Hash()` -> otherwise **desync**, halt, report the ply
2. `Apply(cmd)`; a `Rejected` result is also a desync (both engines validate identically), and
   is reported as the rejection reason, which is the single most useful debugging line this
   layer can print

Seat assignment: the host takes `Side.You` and the guest takes `Side.Foe`, **in both engines**.
There is no perspective swap on the wire. The JS swapped because its state was per-player-row
and its view was hard-coded; this state is one positional board with ownership on the object,
so the honest fix is to make the *view* take a seat — see section 6.

### 4.3 The turn hand-off

`BeginTurn(next)` was issued by whoever was pumping the AI. In a duel the rule is: **the peer
that owns the incoming side issues its own `BeginTurn`**, automatically, on seeing the phase
reach `End`. Unambiguous, needs no negotiation, and keeps the "a side's commands come from that
side's peer" invariant that makes the sequence numbers meaningful.

### 4.4 Liveness, leaving, and coming back

The relay is a live broker, not a mailbox: it delivers to whoever is connected at that instant
and retains nothing. So a `Ping` every 10 s and a 30 s silence threshold stand in for a socket's
close event, and a clean `Bye` skips the wait. Silence is REPORTED, never acted on - a friend who
put their phone down has not forfeited.

**Reconnection is peer-to-peer, not relay-to-peer.** Each peer keeps the whole command log (about
fifteen bytes a frame; a four-hundred-ply match is six kilobytes), and `Hello` and `Join` are the
same message in opposite directions. So whichever end still holds a live match answers an
introduction by handing the match back: the agreed `Start`, then every frame since. The peer that
reloaded replays them - the frame buffer that already existed for a reordering relay is exactly
the machinery a replayed log needs - and carries on at the same ply and the same hash. It works in
both directions, needs no extra message kind, and depends on no relay's memory.

A `Ping` carries the sender's ply, so a peer that merely fell behind while its link was down is
caught up by the difference rather than by a full replay.

While catching up, the "a peer may only move its own side" rule stands down - the log being
replayed legitimately contains the receiver's own past commands - and local input is refused. Both
windows close the moment the log is drained.

**No wall-clock timers gate any decision.** The JS had to auto-pass response windows on a timer,
and its two budgets (5.5 s -> 21.5 s trap, 25 s block) had to be re-derived every time a duration
changed, with a late answer silently dropped. Here a `PendingRequest` simply waits for its
`Responder`, forever if need be. (A shot clock, if it is ever wanted, is a view-layer concern that
ends in an ordinary `RespondCommand` and needs nothing from this layer.)

### 4.5 Anti-tell

D7 kept the neutral presentation hooks and B18 locked the response window ON at 4 s "whenever
netcode arrives". That holds, and it is a **view** obligation: the window shows for a constant
duration whether or not a trap is held, because the decision's *existence* is what leaks, not
its answer. The protocol carries no timing at all, which is the strongest form of this
guarantee — there is nothing on the wire to measure.

## 5. Transport

`IMessageTransport` is topics in, text out: `Publish(topic, text)`, `Subscribe(topic)`, `Poll()`,
`Pump(dt)`, plus status. Pull-based, no callbacks, no threads, no async - so the loopback the tests
use and the real thing are the same shape and the session never learns which one it has.

### 5.1 What shipped, and what did not

The first draft of this used **ntfy.sh**: publish with an HTTP POST, receive by polling a
`?poll=1&since=<cursor>` endpoint. One code path on every platform, no `.jslib`, and the relay's
12-hour cache would have made reconnection a matter of re-reading it. It was very nearly right.

It is also unaffordable, and that is measured rather than argued:

* ntfy's free tier allows a **60-request burst refilled at one request per five seconds**, and
  **250 published messages per day**. A 350 ms poll is 2.9 requests a second - the burst is gone
  in about twenty seconds, and a 5-second keepalive alone exhausts the day's publishes in under
  twenty-one minutes.
* While this was being built, **ntfy.sh stopped answering this machine entirely** after a few
  dozen probe requests, and had not come back an hour later, with every other host reachable.

A relay that playing the game can get you cut off from is not a transport. So ntfy is not used at
all - not even for the handshake, because once the match needs a push transport the handshake may
as well travel on it.

### 5.2 MQTT over WebSocket, to several brokers at once

The shipped transport is a hand-written **MQTT 3.1.1 client over a WebSocket**, connected to
several free public brokers simultaneously - `broker.emqx.io`, `broker.hivemq.com` and
`test.mosquitto.org`, all verified reachable on 2026-08-30 and all confirmed carrying a real
duel end to end. This is where the JS build ended up too, for the same reasons, after its
direct-P2P attempt failed and every free TURN server turned out to be dead.

A broker connection costs one socket and has **no per-message budget at all**, which removes the
entire class of problem above. QoS 0 throughout: the protocol already tolerates loss, duplication
and reordering, so QoS 1 would add packet ids, retransmission and an acknowledgement state machine
to re-solve a problem the ply counter already solves.

**Every message goes to every broker we are connected to, and we read all of them.** Two peers
meet as long as ONE broker is reachable by both - no negotiation, no fallback ladder, no agreeing
on a rendezvous. The duplicate copies cost nothing: the protocol had to tolerate duplicates
anyway, and identical sealed text is deduplicated in the transport. One free relay is a single
point of failure that nobody is obliged to keep up for us; three is a service.

Public brokers are unauthenticated and anyone may publish to any topic. That is exactly why every
frame is sealed: an unauthenticated frame fails its Poly1305 tag and never reaches the decoder.

### 5.3 Two sockets, one interface

There is no single WebSocket API that works on both targets, so `IWebSocket` has two
implementations and the MQTT client never learns which it has:

* **`SystemWebSocket`** (editor, Windows, any native player) wraps `ClientWebSocket`. Its threads
  are entirely internal - one send task, one receive task, two concurrent queues - so everything
  above stays single-threaded and pumped.
* **`BrowserWebSocket`** (WebGL) calls the browser's own `WebSocket` through
  `Plugins/WebGL/SrdWebSocket.jslib`. A WebAssembly player has no sockets at all, so this is not
  an optimisation but the only way the web build can reach a relay. The jslib exposes a POLLED
  surface rather than calling back into C#, so nothing in the netcode can land at an arbitrary
  point in a frame.

`SteamNetworkingSockets` is the natural third implementation for the Steam build - free relay
included - and drops in behind the same interface without touching a line above it.

## 6. The view takes a seat

The rules core has been perspective-neutral since M4 — one positional board, ownership on the
object, `Board.RowFor(owner, which)` owner-generic. The **view** is not: ~90 sites across
`MatchHud`, `MatchController`, `WallBands`, `CardPlateLayer`, `HandBar`, `StandeeLayer`,
`UnitVitals` and `CombatTheatre` read `Side.You` to mean "me".

That is the real work of this milestone, and it is worth doing properly rather than mirroring
the wire: `Seat.Local` / `Seat.Remote` replace those reads, defaulting to `You`/`Foe` so solo
play is bit-identical to today. The guest sets `Seat.Local = Side.Foe`, and the camera rig in
`BoardInput` gains a 180 degree yaw so the guest's own rows are at the bottom of their screen
where their hand is. Everything downstream — which wall is yours, which way a card plate lies,
whose hand renders face-up, whose vitals are warm-tinted — then follows from the seat.

The alternative (mirror every command as it crosses the wire, so both peers think they are
`Side.You`) is what the JS did, and it is unavailable here even in principle: `NewMatch` draws
you's deck before foe's off one shared RNG stream, so the two mirrored states diverge on the
first shuffle. Determinism is the thing being protected, so the view is what moves.

## 7. What is deliberately not in scope

* **Reconnect after a process restart.** The cursor replay handles a dropped connection within
  a session. Surviving a quit means persisting the command log, which is the same feature as
  save/load (M15) and should be built once, there.
* **Matchmaking, lobbies, rooms, spectating.** A shared password is the whole social layer.
* **Hidden-information enforcement.** Section 1.1.
* **Campaign or 2v2.** 1v1 skirmish only.
