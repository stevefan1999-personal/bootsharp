// The Durable Object host half of Bootsharp.Cloudflare.SignalR.
//
// workerd owns `fetch`, `webSocketMessage`, `webSocketClose`, `webSocketError` and `alarm` as
// entrypoint prototype members, and the Bootsharp entrypoint generator refuses to project a C#
// method onto any of them (Projection/Rules.cs `Reserved`). So the hibernation handlers cannot be
// C# methods — they are written here, once, and forwarded to the four ordinary RPC methods
// `HubDurableObject<THub, TEnv>` declares (accept/deliver/disconnect/sweep). The emitted module
// imports this file and wraps each hub-hosting class; an app with a hub writes no JavaScript.
//
// Research harnesses that need a different sweep interval still call `hubDurableObject` themselves
// against the unwrapped generated class.

/// The SignalR ping, in both directions. `{"type":6}` followed by the 0x1E record separator is the
/// entire frame — the client sends it on its keepalive timer and expects nothing back but the same
/// shape. Handing the pair to workerd means the ping is answered by the runtime with the actor
/// still hibernated: measured at zero deliveries to the actor (src/js/test/do-interleave finding 7),
/// which is what makes an idle hub free rather than a per-client timer bill.
const pingFrame = '{"type":6}\x1e';

/// How often the alarm sweep runs by default. It is the mechanism behind HubOptions.HandshakeTimeout
/// (15 s) and ClientTimeoutInterval (30 s): a timer would not survive hibernation, an alarm does.
/// The cost is one actor wake per interval while any socket is open — so the alarm is armed only
/// while sockets exist, and re-armed only if any survived the sweep.
const defaultSweepIntervalMs = 15_000;

const decoder = new TextDecoder();

/**
 * Subclasses a generated Durable Object with the WebSocket hibernation handlers a hub needs.
 * @param {Function} Base the class the publish task emitted for the C# `HubDurableObject` subclass.
 * @param {object} [options]
 * @param {number} [options.sweepIntervalMs] alarm period; 0 disables the sweep entirely.
 * @param {(request: Request) => string} [options.connectionId] assigns the connection id. The
 * default is `crypto.randomUUID()`. The negotiate token is on the request as `?id=`, but it is
 * NOT the default: lets an app sign a Durable Object routing key into that token, and
 * a routing key is shared by every client of the same scope — using it as an identity would make
 * two connections one socket. An app whose token is per-client can opt in here.
 */
export function hubDurableObject (Base, options = {}) {
  const sweepIntervalMs = options.sweepIntervalMs ?? defaultSweepIntervalMs;
  const assignId = options.connectionId ?? (() => crypto.randomUUID());

  return class HubDurableObject extends Base {
    constructor (ctx, env) {
      super(ctx, env);
      // Registered per incarnation rather than once per socket: the pair is Durable Object state,
      // it is idempotent, and a hibernation wake builds a fresh JS object whose auto-responder has
      // to be in place before the next ping arrives.
      ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair(pingFrame, pingFrame));
    }

    async fetch (request) {
      if (request.headers.get("Upgrade") !== "websocket")
        return new Response("Expected a WebSocket upgrade.", { status: 426 });
      const pair = new WebSocketPair();
      const connectionId = assignId(request);
      // The connection id is the socket's FIRST tag, and tags are immutable after accept — which is
      // what makes them a safe identity and an unsafe group membership (runtime.mjs `socketBy`).
      this.ctx.acceptWebSocket(pair[1], [connectionId]);
      // Awaited before the 101 goes out: the client's first frame is the handshake, and it must not
      // be able to arrive before the guest knows the socket exists.
      await this.accept(connectionId);
      await arm(this, sweepIntervalMs);
      return new Response(null, { status: 101, webSocket: pair[0] });
    }

    async webSocketMessage (ws, message) {
      const connectionId = this.ctx.getTags(ws)[0];
      if (connectionId === undefined) return;
      // Awaiting keeps the workerd event alive for the whole dispatch, which is the connection's
      // only backpressure. The guest chains the frame behind whatever is already in flight for this
      // connection (ConnectionDispatchQueue) — workerd does not, and would otherwise interleave two
      // frames of one socket (src/js/test/do-interleave findings 1-3).
      await this.deliver(connectionId, typeof message === "string" ? message : decoder.decode(message));
    }

    async webSocketClose (ws, code, reason) {
      await closed(this, ws, reason || null);
    }

    async webSocketError (ws, error) {
      await closed(this, ws, String(error?.message ?? error ?? "socket error"));
    }

    async alarm () {
      await this.sweep();
      await arm(this, sweepIntervalMs);
    }
  };
}

// Free functions rather than methods: everything on the prototype shares one namespace with the
// generated RPC methods, and the app — not this file — chooses those names.

async function closed (self, ws, reason) {
  const connectionId = self.ctx.getTags(ws)[0];
  if (connectionId === undefined) return;
  await self.disconnect(connectionId, reason);
}

// Arms the sweep while sockets exist and disarms it when the last one goes, so an empty hub costs
// nothing. `getAlarm` is a Durable Object storage read, and storage is the one thing workerd holds
// the actor input gate across (src/js/test/do-interleave finding 3) — so two concurrent entrants
// cannot both observe the alarm unset and both set it.
async function arm (self, sweepIntervalMs) {
  if (sweepIntervalMs <= 0) return;
  const open = self.ctx.getWebSockets().length > 0;
  const scheduled = await self.ctx.storage.getAlarm();
  if (open && scheduled === null) await self.ctx.storage.setAlarm(Date.now() + sweepIntervalMs);
  else if (!open && scheduled !== null) await self.ctx.storage.deleteAlarm();
}

const negotiateSuffix = "/negotiate";

/**
 * Worker-side half of a hub URL: negotiate is answered on the actor (the room is the routing
 * decision the token carries) and the upgrade is forwarded as a live Request, because a 101 with a
 * socket is not a shape the C# response snapshot can express.
 * @param {Request} request
 * @param {URL} url
 * @param {string} prefix normalized path prefix, with a trailing slash.
 * @param {{ getByName: (name: string) => { negotiateResponse: (id: string) => Promise<string>, fetch: (request: Request) => Promise<Response> } }} ns
 */
export async function routeHub (request, url, prefix, ns) {
  const negotiating = url.pathname.endsWith(negotiateSuffix);
  const path = negotiating ? url.pathname.slice(0, -negotiateSuffix.length) : url.pathname;
  const room = path.slice(prefix.length).split("/").filter(Boolean)[0];
  if (!room) return new Response(`Expected ${prefix}<room>.`, { status: 404 });
  const stub = ns.getByName(room);
  if (negotiating) {
    const body = await stub.negotiateResponse(crypto.randomUUID());
    return new Response(body, { headers: { "content-type": "application/json" } });
  }
  if (request.headers.get("Upgrade") !== "websocket")
    return new Response("Expected a WebSocket upgrade.", { status: 426 });
  return stub.fetch(request);
}
