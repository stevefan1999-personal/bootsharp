namespace Cloudflare.Workers.Generator;

internal static class JsRuntime
{
    public static string Host(string wasmImport) => $$"""
import "../timer-shim.ts";
import { DurableObject, WorkflowEntrypoint, WorkerEntrypoint } from "cloudflare:workers";
import { enableWorkerTimers } from "../timer-shim.ts";
import wasmModule from "{{wasmImport}}";

// workerd's type surface declares only the members workerd itself implements, so the
// compile hooks the.NET loader reaches for are absent from it; the patch goes through
// an untyped alias rather than weakening the checker for the whole module.
const wasmHost = WebAssembly as any;
const nativeInstantiate = WebAssembly.instantiate.bind(WebAssembly);
const NativeModule = WebAssembly.Module;
wasmHost.compile = async () => wasmModule;
wasmHost.compileStreaming = async () => wasmModule;
wasmHost.instantiate = (source, imports) => {
  if (source instanceof NativeModule) return nativeInstantiate(source, imports);
  return nativeInstantiate(wasmModule, imports);
};
function WrappedModule(bytes) {
  return bytes instanceof NativeModule ? bytes : wasmModule;
}
WrappedModule.prototype = NativeModule.prototype;
Object.setPrototypeOf(WrappedModule, NativeModule);
wasmHost.Module = WrappedModule;

let api: any = null;
let booting: Promise<any> | null = null;
let dotnetGate = Promise.resolve();

// Top-level events (fetch/queue/scheduled) are serialized: the isolate runs one independent
// invocation into.NET at a time.
function exclusive<T>(work: () => T | PromiseLike<T>): Promise<T> {
  const run = dotnetGate.then(work, work);
  dotnetGate = run.then(() => {}, () => {});
  return run;
}

// Actor entries must NOT take that gate. workerd hosts a Durable Object or Workflow in the
// isolate of whoever invoked it, so an actor call made from inside a gated.NET call arrives
// here while its own caller still holds the gate — queueing it behind the call that is waiting
// on it wedges the isolate permanently. Re-entering directly is safe: wasm is single-threaded,
// so a nested call can only interleave at the outer call's await points, and WorkerContext is
// AsyncLocal, i.e. per async flow rather than per isolate.
async function reentrant<T>(work: () => T | PromiseLike<T>): Promise<T> {
  return work();
}

export async function ensureBoot() {
  if (api) return api;
  if (!booting) {
    booting = (async () => {
      enableWorkerTimers();
      const mod = await import("../../../dist/js/index.mjs");
      await mod.default.boot({ wasm: new ArrayBuffer(8) });
      api = mod;
      return mod;
    })();
  }
  return booting;
}

// Returns any: callers read optional binding-option fields off the result, and the input
// shape is whatever the C# option record happened to serialize to.
function stripNulls(value: any): any {
  if (value == null || typeof value !== "object" || Array.isArray(value)) return value;
  const out: any = {};
  for (const [key, entry] of Object.entries(value)) {
    if (entry != null) out[key] = entry;
  }
  return out;
}

function missing(name) {
  throw new Error(`${name} binding is not configured`);
}

// JSON has no binary literal: JSON.stringify(new ArrayBuffer(4)) yields {}, silently
// dropping every SQLite BLOB. Binary cells and binds cross as {"$blob":"<base64>"};
// the C# side (SqlJson / CloudflareDbDataReader) reads and writes the same tag.
const blobTag = "$blob";

function encodeSqlValue(value) {
  if (value instanceof ArrayBuffer) return encodeBlob(new Uint8Array(value));
  if (ArrayBuffer.isView(value)) return encodeBlob(new Uint8Array(value.buffer, value.byteOffset, value.byteLength));
  return value === undefined ? null : value;
}

function encodeBlob(bytes) {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return { [blobTag]: btoa(binary) };
}

function decodeSqlBind(value) {
  const encoded = value != null && typeof value === "object" ? value[blobTag] : null;
  if (typeof encoded !== "string") return value;
  const binary = atob(encoded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function parseBindings(json) {
  if (json == null || json === "") return [];
  const value = typeof json === "string" ? JSON.parse(json) : json;
  return Array.isArray(value) ? value.map(decodeSqlBind) : [];
}

function emptyGrid() {
  return { columns: [], rowsJson: "[]", lastRowId: 0, changes: 0, rowsRead: 0, rowsWritten: 0 };
}

function d1RawToGrid(raw) {
  if (!Array.isArray(raw) || raw.length === 0) return emptyGrid();
  const columns = (raw[0] ?? []).map(String);
  const rows = raw.slice(1);
  return {
    columns,
    rowsJson: JSON.stringify(rows),
    lastRowId: 0,
    changes: 0,
    rowsRead: rows.length,
    rowsWritten: 0,
  };
}

function d1ResultToGrid(result) {
  const rows = result?.results ?? [];
  const columns = rows.length ? Object.keys(rows[0]) : [];
  const meta = result?.meta ?? {};
  return {
    columns,
    rowsJson: JSON.stringify(rows.map((row) => columns.map((column) => (row[column] ?? null)))),
    lastRowId: meta.last_row_id ?? 0,
    changes: meta.changes ?? 0,
    rowsRead: meta.rows_read ?? 0,
    rowsWritten: meta.rows_written ?? 0,
  };
}

function cursorToGrid(cursor) {
  if (!cursor) return emptyGrid();
  const columns = cursor.columnNames ? [...cursor.columnNames] : [];
  const rows = [];
  for (const row of cursor.raw()) rows.push(Array.from(row, encodeSqlValue));
  return {
    columns,
    rowsJson: JSON.stringify(rows),
    lastRowId: 0,
    changes: cursor.rowsWritten ?? 0,
    rowsRead: cursor.rowsRead ?? rows.length,
    rowsWritten: cursor.rowsWritten ?? 0,
  };
}

function wrapD1Statement(stmt) {
  return {
    bind(values) { return wrapD1Statement(stmt.bind(...(values ?? []))); },
    bindJson(json) { return wrapD1Statement(stmt.bind(...parseBindings(json))); },
    all: async () => JSON.stringify(await stmt.all()),
    first: async () => {
      const row = await stmt.first();
      return row == null ? null : JSON.stringify(row);
    },
    firstColumn: async (colName) => {
      const value = await stmt.first(colName);
      return value == null ? null : JSON.stringify(value);
    },
    run: async () => JSON.stringify(await stmt.run()),
    raw: async () => JSON.stringify(await stmt.raw()),
    grid: async () => d1RawToGrid(await stmt.raw({ columnNames: true })),
    runGrid: async () => {
      const result = await stmt.run();
      const meta = result?.meta ?? {};
      return {
        columns: [],
        rowsJson: "[]",
        lastRowId: meta.last_row_id ?? 0,
        changes: meta.changes ?? 0,
        rowsRead: meta.rows_read ?? 0,
        rowsWritten: meta.rows_written ?? 0,
      };
    },
  };
}

function wrapR2Info(obj) {
  if (!obj) return null;
  return {
    key: obj.key, version: obj.version ?? "", size: obj.size, etag: obj.etag,
    httpEtag: obj.httpEtag, storageClass: obj.storageClass,
  };
}

function wrapR2Object(obj) {
  if (!obj) return null;
  return {
    ...wrapR2Info(obj),
    text: () => obj.text(),
    json: async () => JSON.stringify(await obj.json()),
  };
}

function wrapDoId(id) {
  return { value: String(id), name: id?.name ?? null, jurisdiction: id?.jurisdiction ?? null };
}

async function rpcInt(stub, name) {
  const raw = await stub[name]();
  const n = Number(await Promise.resolve(raw));
  if (!Number.isFinite(n)) throw new Error(`${name} did not return a number: ${String(raw)}`);
  return { value: n };
}

function wrapQueueMetrics(metrics) {
  if (!metrics) return { backlogCount: 0, backlogBytes: 0, oldestMessageTimestamp: null };
  const ts = metrics.oldestMessageTimestamp;
  return {
    backlogCount: metrics.backlogCount ?? 0,
    backlogBytes: metrics.backlogBytes ?? 0,
    oldestMessageTimestamp: ts == null ? null : +new Date(ts),
  };
}

function wrapIKvNamespace(ns) {
  if (!ns) missing("KV");
  return {
    get: (key) => ns.get(key),
    put: (key, value, options) => {
      const clean = stripNulls(options) ?? {};
      if (clean.metadata && typeof clean.metadata === "string") {
        try { clean.metadata = JSON.parse(clean.metadata); } catch {}
      }
      return ns.put(key, value, Object.keys(clean).length ? clean : undefined);
    },
    $delete: (key) => ns.delete(key),
    list: async (options) => {
      const result = await ns.list(stripNulls(options) ?? {});
      return {
        listComplete: !!result.list_complete,
        keys: (result.keys ?? []).map((key) => ({
          name: key.name,
          expiration: key.expiration ?? null,
          metadata: key.metadata == null ? null : JSON.stringify(key.metadata),
        })),
        cursor: result.list_complete ? null : (result.cursor ?? null),
        cacheStatus: result.cacheStatus ?? null,
      };
    },
    getWithMetadata: async (key) => {
      const result = await ns.getWithMetadata(key);
      return {
        value: result.value ?? null,
        metadata: result.metadata == null ? null : JSON.stringify(result.metadata),
        cacheStatus: result.cacheStatus ?? null,
      };
    },
  };
}

function wrapID1Database(db) {
  if (!db) missing("DB");
  return {
    prepare: (query) => wrapD1Statement(db.prepare(query)),
    exec: (query) => db.exec(query),
    withSession: (constraintOrBookmark) => {
      const session = db.withSession(constraintOrBookmark || undefined);
      return {
        prepare: (query) => wrapD1Statement(session.prepare(query)),
        getBookmark: () => session.getBookmark(),
      };
    },
    batchJson: async (json) => {
      const items = JSON.parse(json || "[]");
      const stmts = items.map((item) => db.prepare(item.sql).bind(...(item.params ?? [])));
      const results = await db.batch(stmts);
      return JSON.stringify(results.map(d1ResultToGrid));
    },
  };
}

function wrapIR2Bucket(bucket) {
  if (!bucket) missing("BUCKET");
  return {
    head: async (key) => wrapR2Info(await bucket.head(key)),
    get: async (key) => wrapR2Object(await bucket.get(key)),
    put: async (key, value, options) => {
      const clean = stripNulls(options) ?? {};
      const httpMetadata: any = {};
      if (clean.contentType) httpMetadata.contentType = clean.contentType;
      if (clean.cacheControl) httpMetadata.cacheControl = clean.cacheControl;
      return wrapR2Info(await bucket.put(key, value, {
        httpMetadata: Object.keys(httpMetadata).length ? httpMetadata : { contentType: "text/plain; charset=utf-8" },
        storageClass: clean.storageClass,
      }));
    },
    $delete: (key) => bucket.delete(key),
    deleteMany: (keys) => bucket.delete(keys ?? []),
    list: async (options) => {
      const listed = await bucket.list(Object.keys(stripNulls(options) ?? {}).length ? stripNulls(options) : { limit: 20 });
      return {
        objects: listed.objects.map(wrapR2Info),
        delimitedPrefixes: listed.delimitedPrefixes ?? [],
        truncated: !!listed.truncated,
        cursor: listed.truncated ? listed.cursor : null,
      };
    },
  };
}

function wrapIQueue(queue) {
  if (!queue) missing("QUEUE");
  return {
    send: async (message) => wrapQueueMetrics((await queue.send(message.body, stripNulls({
      contentType: message.contentType, delaySeconds: message.delaySeconds,
    })))?.metadata?.metrics),
    sendBatch: async (messages) => wrapQueueMetrics((await queue.sendBatch((messages ?? []).map((message) => ({
      body: message.body, contentType: message.contentType || undefined, delaySeconds: message.delaySeconds ?? undefined,
    }))))?.metadata?.metrics),
    metrics: async () => wrapQueueMetrics(await queue.metrics()),
  };
}

function wrapIWorkflow(workflow) {
  if (!workflow) missing("WORKFLOW");
  return {
    create: async (options) => {
      const params = options?.params ? JSON.parse(options.params) : {};
      const retention = options?.successRetention || options?.errorRetention
        ? { successRetention: options.successRetention || undefined, errorRetention: options.errorRetention || undefined }
        : undefined;
      return wrapWorkflowInstance(await workflow.create({ id: options?.id || undefined, params, retention }));
    },
    get: async (id) => wrapWorkflowInstance(await workflow.get(id)),
    deleteBatch: async (instanceIds) => {
      const result = await workflow.deleteBatch(instanceIds ?? []);
      return { deleted: result.deleted ?? [], errors: result.errors ?? [] };
    },
  };
}

function wrapWorkflowInstance(inst) {
  return {
    get id() { return inst.id; },
    pause: () => inst.pause(),
    resume: () => inst.resume(),
    terminate: (options) => inst.terminate(stripNulls(options) ?? undefined),
    restart: (options) => {
      if (options?.fromName) {
        return inst.restart({ from: { name: options.fromName, count: options.fromCount ?? 1, type: options.fromType || undefined } });
      }
      return inst.restart(stripNulls(options) ?? undefined);
    },
    $delete: () => inst.delete(),
    status: async () => JSON.stringify(await inst.status()),
    sendEvent: (type, payloadJson) => inst.sendEvent({ type, payload: JSON.parse(payloadJson || "null") }),
  };
}

function wrapSyncKv(kv) {
  if (!kv) return { get: () => null, put: () => {}, $delete: () => false, list: () => "{}" };
  return {
    get: (key) => { const value = kv.get(key); return value == null ? null : String(value); },
    put: (key, value) => kv.put(key, value),
    $delete: (key) => kv.delete(key),
    list: (options) => JSON.stringify(Object.fromEntries(kv.list(stripNulls(options) ?? {}))),
  };
}

export function wrapState(ctx) {
  const sql = ctx.storage.sql;
  return {
    id: String(ctx.id),
    abort: (reason) => ctx.abort(reason ?? undefined),
    storage: {
      get: async (key) => { const value = await ctx.storage.get(key); return value == null ? null : String(value); },
      put: (key, value) => ctx.storage.put(key, value),
      $delete: (key) => ctx.storage.delete(key),
      deleteAll: (options) => ctx.storage.deleteAll(stripNulls(options) ?? {}),
      list: async (options) => JSON.stringify(Object.fromEntries(await ctx.storage.list(stripNulls(options) ?? {}))),
      getAlarm: async () => {
        const scheduledTimeMs = await ctx.storage.getAlarm();
        return scheduledTimeMs == null ? null : { scheduledTimeMs };
      },
      setAlarm: (scheduledTime) => ctx.storage.setAlarm(scheduledTime),
      deleteAlarm: () => ctx.storage.deleteAlarm(),
      sync: () => ctx.storage.sync(),
      getCurrentBookmark: () => ctx.storage.getCurrentBookmark(),
      getBookmarkForTime: (timestampMs) => ctx.storage.getBookmarkForTime(timestampMs),
      onNextSessionRestoreBookmark: (bookmark) => ctx.storage.onNextSessionRestoreBookmark(bookmark),
      sql: {
        get databaseSize() { return sql?.databaseSize ?? 0; },
        exec(query, bindings) {
          if (!sql) return "[]";
          const rows = sql.exec(query, ...(bindings ?? [])).toArray();
          return JSON.stringify(rows.map((row) => Object.fromEntries(
            Object.entries(row).map(([column, value]) => [column, encodeSqlValue(value)]))));
        },
        execJson(query, bindingsJson) { return sql ? cursorToGrid(sql.exec(query, ...parseBindings(bindingsJson))) : emptyGrid(); },
      },
      kv: wrapSyncKv(ctx.storage.kv),
    },
  };
}

export function wrapStep(step) {
  return {
    $do: (name, action) => step.do(name, async () => action()),
    doWithConfig: (name, config, action) => {
      const cfg: any = {};
      if (config?.retryLimit != null || config?.retryDelay) {
        cfg.retries = { limit: config.retryLimit ?? 5, delay: config.retryDelay ?? "5 seconds" };
      }
      if (config?.timeout) cfg.timeout = config.timeout;
      return step.do(name, cfg, async () => action());
    },
    sleep: (name, duration) => step.sleep(name, duration),
    sleepUntil: (name, timestampMs) => step.sleepUntil(name, timestampMs),
    waitForEvent: async (name, type) => JSON.stringify(await step.waitForEvent(name, { type })),
  };
}

export function wrapRequest(request) {
  const headers = {};
  request.headers.forEach((value, key) => { headers[key] = value; });
  const cf = request.cf
    ? {
        colo: request.cf.colo ?? null, country: request.cf.country ?? null, city: request.cf.city ?? null,
        timezone: request.cf.timezone ?? null, httpProtocol: request.cf.httpProtocol ?? null,
        tlsVersion: request.cf.tlsVersion ?? null, asn: request.cf.asn ?? null,
      }
    : null;
  return {
    method: request.method,
    url: request.url,
    headersJson: JSON.stringify(headers),
    cfJson: cf ? JSON.stringify(cf) : null,
    text: () => request.text(),
  };
}

function toResponse(result) {
  if (result.status === 0) return null;
  const headers = JSON.parse(result.headersJson || "{}");
  return new Response(result.body, { status: result.status, headers });
}

function isAssetPath(path) {
  return path === "/favicon.ico" || path.startsWith("/app") || path.startsWith("/_framework") || path.startsWith("/css");
}

""";
}
