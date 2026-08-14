// Declarations for the emitted entrypoint module, which is TypeScript and is the only consumer
// of this surface. Handles crossing these calls are Bootsharp interop objects whose shapes are
// generated per app, so they are deliberately untyped here; what the checker is being asked to
// prove is that the emitted module calls names that exist, with the right arity.

/** Binds the module to the app's compiled wasm, its Bootsharp ESM entry and the name of its
 * log-sink import (null when it declares none), and installs the WebAssembly compile hooks the
 * .NET loader needs inside workerd. */
export declare function configureRuntime (
  module: WebAssembly.Module, loader: () => Promise<any>, logSink?: string | null): void;

/** Releases timers held back from module scope; idempotent, called on every event entry. */
export declare function enableWorkerTimers (): void;

declare global {
  /**
   * Opt-out from the timer shim's zero-delay microtask route, for a host that patches setTimeout
   * itself and depends on the handles its own patch returns — vitest-pool-workers is the case this
   * exists for. It has to be set from the test host's setup file, before the worker
   * module is evaluated, because a foreign patch cannot be told apart from workerd's own
   * implementation: under nodejs_compat workerd's setTimeout is an ordinary JS function that does
   * not report "[native code]", so inferring it would mistake every production isolate for a
   * patched host and freeze it on the first awaited binding call. Leave it unset in production.
   */
  var __bootsharpDelegateZeroDelayTimers: boolean | undefined;
}

/** Boots.NET on first use and resolves the generated interop module. */
export declare function ensureBoot (): Promise<any>;

/** Serializes top-level events (fetch/queue/scheduled) into.NET. */
export declare function exclusive<T> (work: () => T | PromiseLike<T>): Promise<T>;

/** Enters.NET without taking the gate, for Durable Object and Workflow dispatch. */
export declare function reentrant<T> (work: () => T | PromiseLike<T>): Promise<T>;

/** Marks a host object exempt from per-invocation handle release, and returns it: for objects the
 * host memoizes for the isolate but which carry no C# handle type. */
export declare function exemptHandle<T> (instance: T): T;

export declare function missing (name: string): never;
export declare function wrapIdentity (value: any): any;
export declare function wrapDoId (id: any): any;
export declare function wrapIKvNamespace (ns: any, binding: string): any;
export declare function wrapID1Database (db: any, binding: string): any;
export declare function wrapIR2Bucket (bucket: any, binding: string): any;
export declare function wrapIQueue (queue: any, binding: string): any;
export declare function wrapIWorkflow (workflow: any, binding: string): any;
export declare function wrapState (ctx: any): any;
export declare function wrapStep (step: any): any;
export declare function wrapScheduledController (controller: any): any;
export declare function wrapRequest (request: any): any;

/** Rebuilds a workerd Response from the guest's snapshot, or null when the guest declined. */
export declare function toResponse (result: any): Response | null;

/** Whether the path starts with one of the app's declared asset prefixes, and so can be served
 * from the assets binding without booting.NET. */
export declare function isAssetPath (path: string, prefixes: readonly string[]): boolean;
