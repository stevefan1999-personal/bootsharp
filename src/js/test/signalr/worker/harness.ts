// PACKAGE SR — the SignalR end-to-end lane.
//
// Everything here is what a real app writes, and nothing more. The Durable Object class is the
// generated one wrapped by the shipped `hubDurableObject`, which supplies the four hibernation
// handlers workerd reserves and the entrypoint generator therefore refuses to project. The worker
// does the two things puts in a worker: it answers negotiate, and it routes a
// connection to the Durable Object that owns its room.
//
// The point of the lane is that the client is the stock `@microsoft/signalr` npm package, speaking
// the real wire protocol to a NativeAOT-LLVM guest — no test double anywhere on the path.

import { ChatRoom } from "../dist/worker/entrypoints.ts";
import { hubDurableObject } from "../dist/worker/signalr.mjs";

// sweepIntervalMs: 0 disables the alarm. The sweep's arithmetic is asserted deterministically in
// Bootsharp.Cloudflare.SignalR.Test with an injected clock; here an alarm every 15 s would wake the
// actor during the idle window and destroy the one thing this lane can measure that unit tests
// cannot — what a hibernation wake does to a live connection.
export const ChatRoomHub = hubDurableObject(ChatRoom, { sweepIntervalMs: 0 });

export default {
  async fetch(request: Request, env: any): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/ready") return new Response("ok");

    // The client posts to {url}/negotiate?negotiateVersion=1 and then connects to {url}?id=<token>
    // (HttpConnection.ts). The room is the path segment before it, so both halves route the same way.
    const negotiating = url.pathname.endsWith("/negotiate");
    const room = (negotiating ? url.pathname.slice(0, -"/negotiate".length) : url.pathname)
      .split("/").filter(Boolean).pop();
    if (!room) return new Response("no room", { status: 404 });

    const stub = env.CHAT.getByName(room);
    if (negotiating) {
      const body = await stub.negotiateResponse(crypto.randomUUID());
      return new Response(body, { headers: { "content-type": "application/json" } });
    }
    if (request.headers.get("Upgrade") !== "websocket")
      return new Response("expected a websocket upgrade", { status: 426 });
    return stub.fetch(request);
  }
};
