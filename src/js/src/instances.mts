import { exports } from "./exports.mjs";

/** Notified with the ID of an imported instance registered while the tracker is installed. */
export type ImportTracker = (id: number) => void;

const exportedFinalizer = new FinalizationRegistry(finalizeExported);
const exportedById = new Map<number, WeakRef<object>>();
const importedById = new Map<number, object>();
const idByImported = new Map<object, number>();
const refsById = new Map<number, number>();
const onDisposeById = new Map<number, () => void>();
const idPool = new Array<number>();
const exempted = new WeakSet<object>();
let track: ImportTracker | null = null;
let nextId = 1; // JS IDs are positive; C#'s — negative; 0 reserved for null.

export const instances = {
    /** Resolves a registered instance associated with the specified ID,
     *  or uses the specified factory to register a new exported instance. */
    resolve<T extends object>(id: number, factory: new (id: number) => T): T | null {
        if (id === 0) return null;
        if (id > 0) return importedById.get(id) as T;
        const exported = exportedById.get(id)?.deref() as T;
        if (exported != null) return exported;
        const proxy = new factory(id);
        exportedById.set(id, new WeakRef(proxy));
        exportedFinalizer.register(proxy, id, proxy);
        return proxy;
    },
    /** Invoked from C# to notify that the exported (C# -> JS) instance was released there and its
     *  proxy can be dropped here. Unlike the finalization path, this is deterministic: it runs when
     *  C# knows the instance is done with — a callback passed to a host API for one call, which is
     *  unreachable the moment that call returns.
     *  @param id Unique identifier of the released instance. */
    releaseExported(id: number): void {
        const proxy = exportedById.get(id)?.deref();
        if (proxy != null) exportedFinalizer.unregister(proxy);
        exportedById.delete(id);
    },
    /** Registers specified imported (JS) instance and returns the associated unique ID.
     *  Short-circuits already registered imported and exported instances.
     *  Each call hands one reference on the ID to the C# side, which returns them all when the
     *  proxy over the instance is disposed; the ID stays valid until every reference is back. */
    import(instance?: object, cb?: (id: number) => () => void): number {
        if (instance == null) return 0;
        const exportedId = (instance as { _id: number })?._id;
        if (exportedId !== undefined) return exportedId;
        const importedId = idByImported.get(instance);
        if (importedId !== undefined) {
            refsById.set(importedId, refsById.get(importedId)! + 1);
            return importedId;
        }
        const id = idPool.length > 0 ? idPool.pop()! : nextId++;
        importedById.set(id, instance);
        idByImported.set(instance, id);
        refsById.set(id, 1);
        if (cb != null) onDisposeById.set(id, cb(id));
        // Only freshly allocated IDs are tracked, so an instance imported before the current scope
        // opened (eg, an isolate-lived one memoized on the host) is never swept by a later scope.
        if (track != null && !exempted.has(instance)) track(id);
        return id;
    },
    /** Same as import, but the instance is never tracked by an invocation scope;
     *  emitted for the handle types declared with the isolate lifetime. */
    importExempt(instance?: object, cb?: (id: number) => () => void): number {
        if (instance != null) exempted.add(instance);
        return instances.import(instance, cb);
    },
    /** Returns a registered imported instance associated with the specified ID. */
    imported(id: number): object {
        return importedById.get(id)!;
    },
    /** Installs the host's per-invocation tracker; specify null to uninstall.
     *  Intended for hosts (not apps): a single tracker is allowed at a time. */
    trackImported(tracker: ImportTracker | null): void {
        if (tracker != null && track != null)
            throw Error("Imported instance tracker is already installed.");
        track = tracker;
    },
    /** Marks specified instance never tracked by an invocation scope; returns the instance.
     *  Escape hatch for host objects that have no associated C# handle type. */
    exempt<T extends object>(instance: T): T {
        exempted.add(instance);
        return instance;
    },
    /** Invoked from C# to notify that the imported (JS -> C#) instance is no longer used
     *  (eg, was garbage collected) and can be released on the JavaScript side as well.
     *  The proxy gives back every reference it took and the instance is evicted only when the last
     *  one is back: a single JavaScript object is imported under one ID however many times it is
     *  handed over, while the proxy over it is transient, so a proxy dying while another hand-off
     *  of the same ID is in flight must not invalidate the ID for the survivor.
     *  @param id Unique identifier of the disposed instance.
     *  @param refs Number of references to release; one, unless specified otherwise. */
    disposeImported(id: number, refs = 1): void {
        const remaining = (refsById.get(id) ?? 0) - refs;
        if (remaining > 0) refsById.set(id, remaining);
        else if (evict(id)) recycle(id);
    },
    /** Releases an imported instance at the end of the invocation scope that registered it.
     *  Evicts the C# proxy as well, but without routing back through the C#-initiated
     *  disposal path, which notifies JavaScript and would hence recurse.
     *  @param id Unique identifier of the released instance. */
    releaseImported(id: number): void {
        if (!evict(id)) return;
        (exports as { releaseImported: (id: number) => void }).releaseImported(id);
        recycle(id);
    }
};

// Both disposal paths may race: a C# finalizer can run while the scope that imported the instance
// is still open. Evicting an unregistered ID would recycle it twice, yielding two live instances
// associated with the same ID, hence the guard.
function evict(id: number): boolean {
    const instance = importedById.get(id);
    if (instance === undefined) return false;
    idByImported.delete(instance);
    importedById.delete(id);
    refsById.delete(id);
    onDisposeById.get(id)?.();
    onDisposeById.delete(id);
    return true;
}

// Recycling IDs is safe only while disposal is driven by C# finalizers, which imply the proxy is
// dead. A scope may release an ID whose C# proxy is still alive, so a tracking host gets monotonic
// IDs instead — making a recycled ID resolving to a stale proxy unreachable by construction.
function recycle(id: number) {
    if (track == null) idPool.push(id);
}

/* v8 ignore start -- uncoverable, as finalization in Node is not controllable */
function finalizeExported(id: number) {
    exportedById.delete(id);
    (exports as { disposeExported: (id: number) => void }).disposeExported(id);
}
/* v8 ignore stop */
