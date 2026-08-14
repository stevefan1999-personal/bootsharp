// Declarations for the emitted entrypoint module, which is TypeScript and is the only consumer
// of this surface. The generated Durable Object class and the env namespace stub are app-specific,
// so they are deliberately untyped here; what the checker is being asked to prove is that the
// emitted module calls names that exist, with the right arity.

/** Subclasses a generated Durable Object with workerd's reserved hibernation handlers. */
export declare function hubDurableObject (Base: any, options?: {
  sweepIntervalMs?: number;
  connectionId?: (request: Request) => string;
}): any;

/** Worker-side negotiate and WebSocket upgrade for a `[HubRoute]` prefix. */
export declare function routeHub (
  request: Request, url: URL, prefix: string, ns: any): Promise<Response>;
