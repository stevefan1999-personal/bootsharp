using System.Runtime.CompilerServices;
using static Bootsharp.Instances;

namespace Bootsharp.Common.Test;

public class InstancesTest
{
    private interface IFoo;
    private interface IBar;
    private class Foo : IFoo;
    private class Bar : IBar;
    private class Proxy (int id) : JSProxy(id);
    private class SpecializedExport (object it) : Bootsharp.SpecializedExport(it);

    private class SpecializedImport (int id, object it) : Bootsharp.SpecializedImport(id)
    {
        protected internal override object Unwrap () => it;
    }

    private class DelegateProxy (int id) : JSProxy(id)
    {
        public void Invoke () { }
    }

    [Fact]
    public void CanExportAndDisposeInstance ()
    {
        var exported = new object();
        var id = Export(exported);
        Assert.Same(exported, Exported<object>(id));
        DisposeExported(id);
    }

    [Fact]
    public void IgnoresUnknownIdsOnDispose ()
    {
        var exported = new object();
        var id = Export(exported);
        DisposeExported(-1);
        Assert.Same(exported, Exported<object>(id));
    }

    [Fact]
    public void GeneratesUniqueIdsForUniqueExports ()
    {
        Assert.NotEqual(Export(new object()), Export(new object()));
    }

    [Fact]
    public void ShortCircuitsRegisteredExports ()
    {
        var exported = new object();
        Assert.Equal(Export(exported), Export(exported));
    }

    [Fact]
    public void ShortCircuitsImportedProxies ()
    {
        Assert.Equal(Export(new Proxy(1)), Export(new Proxy(1)));
    }

    [Fact]
    public void ShortCircuitsSpecializedExportsWrappingImportedProxy ()
    {
        RegisterExport(typeof(Bar), _ => new SpecializedExport(new Proxy(1)));
        Assert.Equal(Export(new Bar()), Export(new Bar()));
    }

    [Fact]
    public void GeneratesUniqueIdsForSpecializedExportsNotWrappingImportedProxy ()
    {
        RegisterExport(typeof(Foo), it => new SpecializedExport(it));
        Assert.NotEqual(Export(new Foo()), Export(new Foo()));
    }

    [Fact]
    public void ShortCircuitsRegisteredSpecializedExports ()
    {
        var exported = new Foo();
        RegisterExport(typeof(Foo), it => new SpecializedExport(it));
        Assert.Equal(Export(exported), Export(exported));
    }

    [Fact]
    public void ShortCircuitsImportedDelegates ()
    {
        Assert.Equal(Export(new DelegateProxy(1).Invoke), Export(new DelegateProxy(1).Invoke));
    }

    [Fact]
    public void ExportsZeroWhenInstanceIsNull ()
    {
        Assert.Equal(0, Export(default(object)));
    }

    [Fact]
    public void InvokesExportFactoryCallbacks ()
    {
        var exported = false;
        var disposed = false;
        RegisterExport(typeof(Bar), null, (_, _) => {
            exported = true;
            return () => disposed = true;
        });
        var id = Export(new Bar());
        Assert.True(exported);
        Assert.False(disposed);
        DisposeExported(id);
        Assert.True(disposed);
    }

    /// <summary>
    /// The registry leak behind a workflow step: a fresh callback per call is exported under a fresh
    /// ID and nothing on the JavaScript side ever notifies its disposal, so the C# side has to be
    /// able to let go of it itself.
    /// </summary>
    [Fact]
    public void ReleasingExportedUntracksTheInstance ()
    {
        var exported = new object();
        var id = Export(exported);
        Assert.True(ReleaseExported(exported));
        Assert.Throws<KeyNotFoundException>(() => Exported<object>(id));
        Assert.NotEqual(id, Export(exported));
    }

    /// <summary>A released ID is retired: recycling it would let a JavaScript proxy that outlived
    /// the release resolve whatever instance took the ID next.</summary>
    [Fact]
    public void ReleasedIdsAreNotRecycled ()
    {
        var released = new object();
        var releasedId = Export(released);
        ReleaseExported(released);
        // Disposing after a release must not put the retired ID back into circulation either.
        DisposeExported(releasedId);
        Assert.NotEqual(releasedId, Export(new object()));
    }

    [Fact]
    public void ReleasingUnregisteredInstancesIsIgnored ()
    {
        var exported = new object();
        Assert.False(ReleaseExported(exported));
        var id = Export(exported);
        Assert.True(ReleaseExported(exported));
        Assert.False(ReleaseExported(exported));
        Assert.NotEqual(id, Export(exported));
    }

    [Fact]
    public void NotifiesJavaScriptOfReleasedExports ()
    {
        var released = new List<int>();
        RegisterReleaseNotifier(released.Add);
        var exported = new object();
        var id = Export(exported);
        ReleaseExported(exported);
        Assert.Equal([id], released);
    }

    [Fact]
    public void InvokesDisposeCallbackOnRelease ()
    {
        var disposed = false;
        RegisterExport(typeof(Bar), null, (_, _) => () => disposed = true);
        var exported = new Bar();
        Export(exported);
        ReleaseExported(exported);
        Assert.True(disposed);
    }

    [Fact]
    public void CanImportAndDisposeInstance ()
    {
        var imported = new Foo();
        RegisterImport(typeof(IFoo), _ => imported);
        Assert.Same(imported, Resolve<IFoo>(1));
        DisposeImported(1);
    }

    [Fact]
    public void ShortCircuitsRegisteredImportsUntilDisposed ()
    {
        var imported = new Bar();
        RegisterImport(typeof(IBar), _ => imported);
        Resolve<IBar>(42);
        RegisterImport(typeof(IBar), _ => new Bar());
        Assert.Same(imported,
            // We already have previous import associated with the '42' ID — the registered
            // factory should not be invoked again; it's the responsibility of the JS side
            // to let us know when the instance is disposed.
            Resolve<IBar>(42));
        // Here, we simulate JS side telling us to dispose the '42' instance.
        DisposeImported(42);
        // Now we exercise the factory and register the new instance as '42'.
        Assert.NotSame(imported, Resolve<IBar>(42));
        DisposeImported(42);
    }

    [Fact]
    public void ShortCircuitsImportedExports ()
    {
        var exported = new object();
        Assert.Same(exported, Resolve<object>(Export(exported)));
    }

    [Fact]
    public void UnwrapsResolvedSpecializedImportsOfRefType ()
    {
        var imported = new Bar();
        RegisterImport(typeof(IBar), id => new SpecializedImport(id, imported));
        Assert.Same(imported, Resolve<IBar>(1));
        DisposeImported(1);
    }

    [Fact]
    public void UnwrapsResolvedSpecializedImportsOfValueType ()
    {
        var imported = DateTime.Now;
        RegisterImport(typeof(DateTime), id => new SpecializedImport(id, imported));
        Assert.Equal(imported, Resolve<DateTime>(1));
        DisposeImported(1);
    }

    [Fact]
    public void UnwrapsResolvedSpecializedExports ()
    {
        var exported = new Foo();
        RegisterExport(typeof(Foo), it => new SpecializedExport(it));
        Assert.Same(exported, Resolve<IFoo>(Export(exported)));
    }

    [Fact]
    public void ResolvesNullWhenIdIsZero ()
    {
        Assert.Null(Resolve<object>(0));
    }

    [Fact]
    public void DisposeImportedIgnoresForeignProxy ()
    {
        // An ID released by an invocation scope may be recycled to another instance,
        // whose registration the finalizer of the previously associated proxy must not evict.
        var imported = new Foo();
        RegisterImport(typeof(IFoo), _ => imported);
        Resolve<IFoo>(101);
        Assert.False(DisposeImported(101, new Foo()));
        Assert.Same(imported, Resolve<IFoo>(101));
        Assert.True(DisposeImported(101, imported));
    }

    [Fact]
    public void DisposeImportedNotifiesForDelegateProxy ()
    {
        // Imported delegates register the delegate, while the finalizer is on its target proxy.
        var proxy = new DelegateProxy(102);
        Action del = proxy.Invoke;
        RegisterImport(typeof(Action), _ => del);
        Resolve<Action>(102);
        Assert.True(DisposeImported(102, proxy));
    }

    [Fact]
    public void DisposeImportedIgnoresUnknownIds ()
    {
        Assert.False(DisposeImported(103, new Foo()));
    }

    [Fact]
    public void DisposeImportedNotifiesWhenRegisteredWasCollected ()
    {
        // Short weak references are cleared when the object is found unreachable, ie before its
        // finalizer runs, in which case the finalizing proxy is the only possible owner of the ID.
        RegisterImport(typeof(IFoo), _ => new Foo());
        ResolveTransient(104);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.True(DisposeImported(104, new Foo()));
    }

    [Fact]
    public void ReleasedImportIsNotDisposedAgain ()
    {
        var imported = new Foo();
        RegisterImport(typeof(IFoo), _ => imported);
        Resolve<IFoo>(105);
        ReleaseImported(105);
        Assert.False(DisposeImported(105, imported));
    }

    [Fact]
    public void ReleasedImportIsResolvedAnew ()
    {
        var imported = new Foo();
        RegisterImport(typeof(IFoo), _ => imported);
        Resolve<IFoo>(106);
        ReleaseImported(106);
        RegisterImport(typeof(IFoo), _ => new Foo());
        Assert.NotSame(imported, Resolve<IFoo>(106));
        ReleaseImported(106);
    }

    [Fact]
    public void UncheckedDisposeImportedOverloadIsPreserved ()
    {
        var imported = new Foo();
        RegisterImport(typeof(IFoo), _ => imported);
        Resolve<IFoo>(107);
        DisposeImported(107);
        RegisterImport(typeof(IFoo), _ => new Foo());
        Assert.NotSame(imported, Resolve<IFoo>(107));
        DisposeImported(107);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResolveTransient (int id) => Resolve<IFoo>(id);
}
