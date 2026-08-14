// The sample's worker module. Everything below the SignalR routing is generated; this file exists
// because a hub needs two things the emitter is structurally unable to produce, and nothing else.
//
// 1. The Durable Object hosting a hub must answer workerd's hibernation callbacks —
//    `webSocketMessage`, `webSocketClose`, `webSocketError`, `alarm`. Those names are reserved
//    entrypoint prototype members, and the Bootsharp entrypoint generator refuses to project a C#
//    method onto any of them (Projection/Rules.cs). `hubDurableObject` is the shipped subclass that
//    supplies them and forwards each to the ordinary RPC methods `HubDurableObject` declares.
// 2. A WebSocket upgrade answers 101 carrying a live socket object. That is not a shape the C#
//    response snapshot (status + headers + body) can express, so the upgrade is routed here and
//    handed to the room's stub; every other request goes to the generated fetch unchanged, which is
//    where the whole Minimal API lives.
//
// A worker with no hub needs none of this and keeps pointing wrangler's `main` straight at the
// emitted module.

import Worker, { ChatRoom } from "../dist/worker/entrypoints.ts";
import { hubDurableObject } from "../dist/worker/signalr.mjs";

export { Counter, DemoWorkflow } from "../dist/worker/entrypoints.ts";

/// The wrangler `CHAT` binding's class. The generated `ChatRoom` is the base; the hibernation
/// handlers are the wrapper's, so the guest calls still take the same gated path as every other
/// actor call.
export const ChatRoomHub = hubDurableObject(ChatRoom);

/// Where a room name starts in `/chat/<room>`, and where the client appends `/negotiate`.
const chatPrefix = "/chat/";
const negotiateSuffix = "/negotiate";

export default class extends Worker {
  async fetch (request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname.startsWith(chatPrefix)) return this.chat(request, url);
    return super.fetch(request);
  }

  /**
   * The two halves of a SignalR connection, both addressed at the same Durable Object instance.
   * The client POSTs `{url}/negotiate?negotiateVersion=1` and then opens a socket to `{url}?id=…`
   * (HttpConnection.ts), so the room is the path segment before the optional suffix and both halves
   * route identically — which is the point of putting the routing key in the token.
   */
  private async chat (request: Request, url: URL): Promise<Response> {
    const negotiating = url.pathname.endsWith(negotiateSuffix);
    const path = negotiating ? url.pathname.slice(0, -negotiateSuffix.length) : url.pathname;
    const room = path.slice(chatPrefix.length).split("/").filter(Boolean)[0];
    if (!room) return new Response("Expected /chat/<room>.", { status: 404 });

    const stub = this.env.CHAT.getByName(room);
    if (negotiating) {
      // Answered by the actor, not here: the room is the routing decision the token carries.
      const body = await stub.negotiateResponse(crypto.randomUUID());
      return new Response(body, { headers: { "content-type": "application/json" } });
    }
    if (request.headers.get("Upgrade") !== "websocket")
      return new Response("Expected a WebSocket upgrade.", { status: 426 });
    return stub.fetch(request);
  }
}
