# The NativeAOT-LLVM end-to-end lane

`scripts/compile-test.sh` publishes `-c Debug`, which is Mono, and everything under
`test/spec` is verified there. But `BsLlvm = !BsDebug`: every release publish — and every
Cloudflare Worker — runs the NativeAOT-LLVM path, with a different JS-interop marshaler, a
different finalizer story and a single native `.wasm` instead of side-loaded assemblies. That
path had no automated coverage at all (ADR-0006 §4, research/06 implication 10).

This lane closes that gap for the ADR-0002 Tier-1 mechanisms, which exist *only* to serve that
path. It publishes a minimal exercise worker with `BsLlvm=true` and drives it under real workerd.

```
cd src/js && npm run test:llvm
```

Prerequisites, in order: `src/cs/.scripts/llvm.sh` (the pinned ILCompiler-LLVM packs) and
`src/cs/.scripts/pack.sh` (the Bootsharp packages the exercise project restores). Bootsharp is
consumed as a package so the lane exercises what a user gets; `Bootsharp.Cloudflare` is consumed
by project, so the lane always tests the working tree.

## What it asserts

| Capability | How |
| --- | --- |
| Awaited primitive `Task<int>` import | `IProbeStub.Increment()` across a workerd `JsRpcPromise`, with neither the deleted `RpcInt` box nor a JS-side number normaliser in between. Two invocations, so the value must also advance |
| Handle category, identity preserved | The isolate-scoped `IKvNamespace` binding: the same JavaScript object must resolve to the same C# proxy on a later invocation, and a proxy held since the first invocation must still reach the live binding |
| Disposal scope releasing handles | A Durable Object stub carries no isolate scope, so the invocation that imported it releases it. Held past that point, the call must fail *because the JS registry evicted the id* |

`run.mjs` prints one PASS/FAIL line per check and exits non-zero on any failure. It is deliberately
not a `*.spec.ts`: `npm test` must stay a fast Mono-only run with no publish and no workerd.

## Verified to discriminate

Each assertion was falsified against the published artifacts before being trusted:

- disabling `installScoping` in `dist/worker/runtime.mjs` turns the stale-handle failure into
  workerd's own *"Cannot perform I/O on behalf of a different request"* — the exact failure
  ADR-0002 §5 predicts, and a different message than the registry eviction the lane requires;
- swapping `$i.importExempt` back to `$i.import` for the KV binding breaks both identity checks.

One honest caveat, measured rather than assumed: the awaited-import assertion passes with **or
without** the `await`, because the CoreCLR-flavour marshaler resolves a `JsRpcPromise` as a
thenable. It is a round-trip regression guard for the `RpcInt` deletion, not proof that the await
is required; the shapes that do fail without it — a custom thenable, a plain non-promise value —
are covered on the Mono path by `test/spec/interop.spec.ts`.
