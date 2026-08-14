// PACKAGE H — the Durable Object event-interleaving harness.
//
// This module is hand-written, unlike every other worker module in the repo, for one reason: the
// Durable Object generator projects RPC methods only. `webSocketMessage`, `webSocketClose` and
// `alarm` are on its reserved-name list (Bootsharp.Cloudflare.Generate/Projection/Rules.cs:80) and
// no handler slot maps to them, so there is nothing to generate against yet. Everything else here
// is copied from what the generator emits — `enableWorkerTimers()` then `ensureBoot()` then
// `reentrant(...)` around the guest call, `wrapState(this.ctx)` and `wrapEnv(this.env)` for the
// handles — precisely so the findings transfer to the generated shape unchanged.
//
// The one deliberate divergence from a real hub: nothing serializes the guest calls. That is the
// question under test.
//
// Routing is `/<scope>/<route>`, and every scenario uses its own scope, i.e. its own Durable Object
// instance. Cross-scenario contamination is otherwise unavoidable: a scenario's last handler is
// still resolving when the next one starts, and a shared trace would record it.

import { DurableObject } from "cloudflare:workers";
import { ensureBoot, enableWorkerTimers, reentrant, wrapState } from "../dist/worker/runtime.mjs";
import { wrapEnv } from "../dist/worker/entrypoints.ts";

// Isolate-lifetime, like the C# statics: a wake that rebuilds the actor but keeps the isolate
// leaves these alone, and a wake that lost the isolate resets them. The pair separates the two.
let incarnations = 0;

// SignalR's keepalive frame verbatim: {"type":6} plus the 0x1E record separator.
const PING = '{"type":6}\x1e';

export class Hub extends DurableObject {
  #actor: number | undefined;
  #incarnation: number;
  #arrivals = 0;

  constructor(ctx, env) {
    super(ctx, env);
    this.#incarnation = ++incarnations;
    // load-bearing claim, made testable: the ping is matched by exact string
    // equality inside workerd's hibernation read loop, which answers it and sets skip = true
    // without waking the actor. If that holds, no js:deliver is ever recorded for a ping.
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair(PING, PING));
  }

  // Mirrors the generated `this._id ??= IActorRuntime.constructDurableObject(...)` line, with the
  // scope name added so the guest can key its trace by Durable Object rather than by incarnation.
  async #api() {
    enableWorkerTimers();
    const api = await ensureBoot();
    this.#actor ??= api.IHub.construct(this.#scope(), wrapState(this.ctx), wrapEnv(this.env));
    return api;
  }

  #scope() {
    return String(this.ctx.id.name ?? this.ctx.id);
  }

  async fetch(request) {
    const url = new URL(request.url);
    const route = url.pathname.split("/").filter(Boolean).slice(1).join("/");
    if (request.headers.get("upgrade") === "websocket") {
      const conn = url.searchParams.get("conn") ?? "c";
      const pair = new WebSocketPair();
      // Hibernation acceptance, not `server.accept()`: the socket must outlive the isolate for
      // scenario 3 to mean anything.
      this.ctx.acceptWebSocket(pair[1]);
      pair[1].serializeAttachment({ conn, acceptedAt: Date.now() });
      const api = await this.#api();
      api.IHub.note(this.#actor, "js:accept", `conn=${conn} incarnation=${this.#incarnation}`);
      return new Response(null, { status: 101, webSocket: pair[0] });
    }
    const api = await this.#api();
    switch (route) {
      case "report":
        return Response.json({ cs: JSON.parse(api.IHub.report(this.#actor)), js: this.#state() });
      case "reset":
        api.IHub.reset(this.#actor);
        this.#arrivals = 0;
        return Response.json({ ok: true });
      case "alarm": {
        const delay = Number(url.searchParams.get("in") ?? 200);
        const program = url.searchParams.get("program") ?? "alarm|";
        await this.ctx.storage.put("alarm-program", program);
        await this.ctx.storage.setAlarm(Date.now() + delay);
        api.IHub.note(this.#actor, "js:alarm-scheduled", `in=${delay} program=${program}`);
        return Response.json({ ok: true });
      }
      default:
        return new Response(`no route ${route}`, { status: 404 });
    }
  }

  #state() {
    const sockets = this.ctx.getWebSockets();
    return {
      scope: this.#scope(),
      incarnation: this.#incarnation,
      incarnations,
      actor: this.#actor ?? null,
      sockets: sockets.length,
      attachments: sockets.map(ws => ws.deserializeAttachment())
    };
  }

  async webSocketMessage(ws, message) {
    // Synchronous, before any await: this is the delivery order workerd chose, uncontaminated by
    // whatever the boot promise does to the microtask queue.
    const arrival = ++this.#arrivals;
    const conn = ws.deserializeAttachment()?.conn ?? "?";
    const api = await this.#api();
    api.IHub.note(this.#actor, "js:deliver",
      `conn=${conn} arrival=${arrival} msg=${message} sockets=${this.ctx.getWebSockets().length}`
      + ` incarnation=${this.#incarnation}`);
    // The ack is what the driver waits on, so a guest failure has to become an ack too — a hung
    // settle() would cost every later scenario in the run.
    let label = null, failure = null;
    try { label = await reentrant(() => api.IHub.message(this.#actor, conn, String(message))); }
    catch (error) { failure = String(error?.message ?? error); }
    api.IHub.note(this.#actor, "js:settled", `conn=${conn} arrival=${arrival} label=${label} error=${failure}`);
    ws.send(JSON.stringify({ ack: label, arrival, error: failure }));
  }

  async webSocketClose(ws, code) {
    const api = await this.#api();
    const conn = ws.deserializeAttachment()?.conn ?? "?";
    await reentrant(() => api.IHub.closed(this.#actor, conn, code));
  }

  async alarm() {
    const api = await this.#api();
    api.IHub.note(this.#actor, "js:alarm-deliver", `incarnation=${this.#incarnation}`);
    const program = (await this.ctx.storage.get("alarm-program")) ?? "alarm|";
    await reentrant(() => api.IHub.alarm(this.#actor, program));
    api.IHub.note(this.#actor, "js:alarm-settled", "");
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/ready") return new Response("ready");
    // First path segment is the scope: one Durable Object instance per scenario, which is also
    // "one Durable Object per hub scope" as recommends for SignalR itself.
    const scope = url.pathname.split("/").filter(Boolean)[0];
    if (!scope) return new Response("expected /<scope>/<route>", { status: 400 });
    return env.HUB.getByName(scope).fetch(request);
  }
};
