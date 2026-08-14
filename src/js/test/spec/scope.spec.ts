import { describe, it, beforeAll, afterEach, expect } from "vitest";
import { Event, instances, bootRuntime } from "../cs";
import { IImportedModule, Modules } from "../cs/Test/bin/bootsharp/generated/modules/test/library.g.mjs";
import type { IIsolateHandle, IScopedHandle } from "../cs/Test/bin/bootsharp/generated/modules/test/library.g.mjs";

class Handle implements IIsolateHandle, IScopedHandle {
    constructor(readonly kind: string) { }
    getId() { return this.kind; }
    getKind() { return this.kind; }
}

class Instanced {
    // The instance is imported through the generated event wrapper, which subscribes on import.
    onRecordChanged = new Event<[never, never]>();
    constructor(private arg: string) { }
    getInstanceArg() { return this.arg; }
}

describe("while bootsharp is booted", () => {
    beforeAll(bootRuntime);
    afterEach(() => instances.trackImported(null));

    it("imported instances are not tracked by default", () => {
        const instance = new Instanced("untracked");
        expect(Modules.getInstanceArg(<never>instance)).toStrictEqual("untracked");
        expect(instances.imported(instances.import(instance))).toBe(instance);
    });

    it("imported instances are tracked while a tracker is installed", () => {
        const ids: number[] = [];
        const instance = new Instanced("tracked");
        instances.trackImported(id => ids.push(id));
        expect(Modules.getInstanceArg(<never>instance)).toStrictEqual("tracked");
        expect(ids).toHaveLength(1);
        expect(instances.imported(ids[0])).toBe(instance);
    });

    it("released instances are evicted on both sides and re-imported under a fresh id", () => {
        const ids: number[] = [];
        const instance = new Instanced("released");
        instances.trackImported(id => ids.push(id));
        Modules.getInstanceArg(<never>instance);
        instances.releaseImported(ids[0]);
        expect(instances.imported(ids[0])).toBeUndefined();
        // IDs are not recycled while tracking, so a released ID can't resolve a stale C# proxy.
        expect(Modules.getInstanceArg(<never>instance)).toStrictEqual("released");
        expect(ids).toHaveLength(2);
        expect(ids[1]).not.toStrictEqual(ids[0]);
    });

    it("releasing an unregistered id is ignored", () => {
        const ids: number[] = [];
        instances.trackImported(id => ids.push(id));
        const instance = new Instanced("double-released");
        Modules.getInstanceArg(<never>instance);
        instances.releaseImported(ids[0]);
        instances.releaseImported(ids[0]);
        expect(instances.import(instance)).not.toStrictEqual(ids[0]);
    });

    it("exempted instances are never tracked", () => {
        const ids: number[] = [];
        const instance = instances.exempt(new Instanced("exempted"));
        instances.trackImported(id => ids.push(id));
        expect(Modules.getInstanceArg(<never>instance)).toStrictEqual("exempted");
        expect(ids).toHaveLength(0);
    });

    it("isolate handles are imported exempt from tracking", () => {
        const ids: number[] = [];
        const handle = new Handle("isolate");
        const getHandle = () => handle;
        IImportedModule.getIsolateHandle = getHandle;
        expect(IImportedModule.getIsolateHandle).toBe(getHandle);
        instances.trackImported(id => ids.push(id));
        expect(Modules.getIsolateHandleId()).toStrictEqual("isolate");
        expect(Modules.getIsolateHandleId()).toStrictEqual("isolate");
        expect(ids).toHaveLength(0);
    });

    it("invocation handles are tracked and disposed deterministically", () => {
        const ids: number[] = [];
        const handle = new Handle("scoped");
        const getHandle = () => handle;
        IImportedModule.getScopedHandle = getHandle;
        expect(IImportedModule.getScopedHandle).toBe(getHandle);
        instances.trackImported(id => ids.push(id));
        // The C# side disposes the handle before returning, which evicts it from the registry.
        expect(Modules.getScopedHandleKind()).toStrictEqual("scoped");
        expect(ids).toHaveLength(1);
        expect(instances.imported(ids[0])).toBeUndefined();
    });

    it("importing a missing instance yields the null id", () => {
        expect(instances.importExempt()).toStrictEqual(0);
    });

    it("disposing an unregistered id is ignored", () => {
        const id = instances.import(new Instanced("disposed"));
        instances.disposeImported(id);
        instances.disposeImported(id);
        // A duplicate ID in the recycle pool would associate two instances with the same ID.
        expect(instances.import(new Instanced("first"))).not.toStrictEqual(instances.import(new Instanced("second")));
    });

    // The export direction of the same problem: C# hands JavaScript a callback for the duration of
    // one host call, and only C# knows when it is done with. Releasing drops the proxy here, so the
    // registry does not grow by one entry per call for the life of the isolate.
    it("released exported instances are dropped and resolved again as a fresh proxy", () => {
        class Exported { constructor(readonly _id: number) { } }
        const id = -1000;
        const proxy = instances.resolve(id, Exported);
        expect(instances.resolve(id, Exported)).toBe(proxy);
        instances.releaseExported(id);
        expect(instances.resolve(id, Exported)).not.toBe(proxy);
    });

    it("releasing an unregistered exported id is ignored", () => {
        expect(() => instances.releaseExported(-1001)).not.toThrow();
    });

    it("a second tracker is rejected", () => {
        instances.trackImported(() => { });
        expect(() => instances.trackImported(() => { })).toThrow(/already installed/);
    });
});
