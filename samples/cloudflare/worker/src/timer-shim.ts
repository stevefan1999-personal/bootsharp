const hostSetTimeout = globalThis.setTimeout;
const hostClearTimeout = globalThis.clearTimeout;
// vitest-pool-workers patches setTimeout before this module is evaluated and depends on the
// sentinel handles its own patch returns, so it opts out of the zero-delay route below from its
// setup file. The opt-out has to be explicit: a foreign patch cannot be told apart from the
// platform's own implementation, because under nodejs_compat workerd's setTimeout is itself an
// ordinary JS function that does not report "[native code]" — inferring from that mistakes
// every production isolate for a patched host and freezes it on the first awaited binding call.
const delegateZeroDelay = (globalThis as any).__bootsharpDelegateZeroDelayTimers === true;
const nativeSetTimeout = hostSetTimeout.bind(globalThis);
const nativeClearTimeout = hostClearTimeout.bind(globalThis);

// Shim-owned handles are negative so they can never collide with the host's positive
// ones: the .NET timer pump calls clearTimeout on every reschedule, and a collision
// would cancel an unrelated callback.
let nextShimHandle = -1;
const microtasks = new Map();
const deferred = new Map();
let live = false;

globalThis.setTimeout = (fn, ms, ...args) => {
  const delay = Number(ms) || 0;
  // Zero delay is the .NET scheduler's hot path (the ThreadPool pump, plus any timer
  // that is already due). workerd cancels timers scheduled inside an I/O context when
  // that context ends, and .NET's pump latches (_callbackQueued, s_shortestDueTimeMs)
  // turn a single dropped tick into a permanent isolate freeze — so these go to the
  // microtask queue, which belongs to no I/O context and cannot be cancelled that way.
  if (delay <= 0) {
    if (delegateZeroDelay) return nativeSetTimeout(fn, ms, ...args);
    const handle = nextShimHandle--;
    microtasks.set(handle, { fn, args });
    queueMicrotask(() => {
      const timer = microtasks.get(handle);
      if (!timer) return;
      microtasks.delete(handle);
      timer.fn(...timer.args);
    });
    return handle;
  }
  if (live) return nativeSetTimeout(fn, ms, ...args);
  // workerd forbids timers at module scope, so hold them until the first request.
  const handle = nextShimHandle--;
  deferred.set(handle, { fn, args, ms: delay, nativeHandle: null });
  return handle;
};

globalThis.clearTimeout = (handle) => {
  if (microtasks.delete(handle)) return;
  const timer = deferred.get(handle);
  if (!timer) return nativeClearTimeout(handle);
  deferred.delete(handle);
  if (timer.nativeHandle !== null) nativeClearTimeout(timer.nativeHandle);
};

// nodejs_compat installs a partial `process`, which workers-types does not declare on globalThis.
const nodeProcess = (globalThis as any).process;
if (typeof nodeProcess?.exit === "function") {
  // The loader classifies workerd as node (nodejs_compat) and would exit the process on
  // a runtime fault; raising instead keeps the failure inside the request.
  nodeProcess.exit = (code: number) => {
    throw new Error(`process.exit(${code}) from .NET runtime`);
  };
}

export function enableWorkerTimers() {
  if (live) return;
  live = true;
  for (const [handle, timer] of deferred) {
    timer.nativeHandle = nativeSetTimeout(() => {
      deferred.delete(handle);
      timer.fn(...timer.args);
    }, timer.ms);
  }
}
