namespace Bootsharp.Publish;

/// <summary>
/// Generates C# binding proxies for <see cref="InstanceMeta"/>.
/// Symmetrical to <see cref="JSInstanceGenerator"/>, which generates the same but for the JS side.
/// </summary>
internal sealed class CSInstanceGenerator
{
    private InstanceMeta it = null!;

    public string Generate (IReadOnlyCollection<InstanceMeta> its) =>
        $$"""
          #nullable enable
          #pragma warning disable

          using System.Runtime.CompilerServices;
          using System.Runtime.InteropServices.JavaScript;

          namespace Bootsharp.Generated;

          public static partial class Instances
          {
              internal static int Export<T> (T? it) => Bootsharp.Instances.Export(it);
              internal static T Exported<T> (int id) where T : class => Bootsharp.Instances.Exported<T>(id);
              internal static T? Resolve<T> (int id) => Bootsharp.Instances.Resolve<T>(id);
              internal static T Imported<T> (int id) => Bootsharp.Instances.Imported<T>(id);

              internal static void DisposeImported (int id, object proxy)
              {
                  var refs = Bootsharp.Instances.DisposeImported(id, proxy);
                  if (refs > 0) NotifyImportedDisposed(id, refs);
              }

              [ModuleInitializer]
              internal static void RegisterImports ()
              {
                  {{Fmt(its.Where(i => i.IK == InteropKind.Import).Select(EmitImporter), 2)}}
              }

              [ModuleInitializer]
              internal static void RegisterExports ()
              {
                  Bootsharp.Instances.RegisterReleaseNotifier(NotifyExportedReleased);
                  {{Fmt(its.Where(i => i.Exporter != null).Select(EmitExporter), 2)}}
              }

              [JSExport] private static void DisposeExported (int id) => Bootsharp.Instances.DisposeExported(id);
              [JSExport] private static void ReleaseImported (int id) => Bootsharp.Instances.ReleaseImported(id);
              [JSImport("instances.disposeImported", "Bootsharp")] private static partial void NotifyImportedDisposed (int id, int refs);
              [JSImport("instances.releaseExported", "Bootsharp")] private static partial void NotifyExportedReleased (int id);
          }

          {{Fmt(its.Where(i => i.IK == InteropKind.Import).Select(EmitProxy), 0, "\n\n")}}
          """;

    private static string EmitImporter (InstanceMeta it)
    {
        var proxy = it is DelegateMeta
            ? $"static id => new {it.Syntax}(new {it.Proxy.Syntax}(id).Invoke)"
            : $"static id => new {it.Proxy.Syntax}(id)";
        return $"Bootsharp.Instances.RegisterImport(typeof({it.Syntax}), {proxy});";
    }

    private static string EmitExporter (InstanceMeta it)
    {
        var evt = it.Members.OfType<EventMeta>().ToArray();
        var sp = it.Proxy as SpecializedProxy;
        var spec = sp != null ? $"static it => new {sp.Export.Syntax}(({it.Syntax})it)" : "null";
        if (evt.Length == 0) return $"Bootsharp.Instances.RegisterExport(typeof({it.Syntax}), {spec});";
        return
            $$"""
              Bootsharp.Instances.RegisterExport(typeof({{it.Syntax}}), {{spec}}, static (_id, obj) => {
                  var it = ({{sp?.Export.Syntax ?? it.Syntax}})obj;
                  {{Fmt(evt.Select(e => $"it.{e.Name} += Handle{e.Name};"))}}
                  return () => {
                      {{Fmt(evt.Select(e => $"it.{e.Name} -= Handle{e.Name};"), 2)}}
                  };

                  {{Fmt(evt.Select(e => {
                      var args = string.Join(", ", e.Args.Select(a => $"{a.Value.TypeSyntax} {a.Name}"));
                      var invArgs = PrependIdArg(string.Join(", ", e.Args.Select(ExportCS)));
                      var name = $"{it.Proxy.Id}_Broadcast{e.Name}_Serialized";
                      return $"void Handle{e.Name} ({args}) => Interop.{name}({invArgs});";
                  }))}}
              });
              """;
    }

    private string EmitProxy (InstanceMeta it) => (this.it = it) switch {
        DelegateMeta del => EmitDelegateProxy(del),
        { Proxy: SpecializedProxy sp } => EmitSpecializedProxy(it, sp),
        _ => EmitOpaqueProxy(it)
    };

    private string EmitOpaqueProxy (InstanceMeta it) =>
        $$"""
          public sealed class {{it.Proxy.Id}} (int id) : global::Bootsharp.JSProxy(id), {{it.Syntax}}{{(it.Handle != null ? ", global::System.IDisposable" : "")}}
          {
              ~{{it.Proxy.Id}}() => Instances.DisposeImported(_id, this);

              {{Fmt([EmitHandleDispose(it), ..it.Members.Select(EmitMemberImport)])}}
          }
          """;

    // Handles are released deterministically: neither NativeAOT finalizers nor the JS finalization
    // registry fire reliably in the hosts (workerd) the handle category exists for.
    private static string? EmitHandleDispose (InstanceMeta it) => it.Handle == null ? null :
        $$"""
          public void Dispose ()
          {
              global::System.GC.SuppressFinalize(this);
              Instances.DisposeImported(_id, this);
          }
          """;

    private string EmitSpecializedProxy (InstanceMeta it, SpecializedProxy sp) =>
        $$"""
          public sealed class {{it.Proxy.Id}} (int id) : {{sp.Import.Syntax}}(id)
          {
              ~{{it.Proxy.Id}}() => Instances.DisposeImported(_id, this);

              {{Fmt([..it.Members.Select(EmitMemberImport), sp.CS?.Replace("$full", it.Syntax)])}}
          }
          """;

    private static string EmitDelegateProxy (DelegateMeta del)
    {
        var inv = del.Invoker;
        var args = string.Join(", ", inv.Args.Select(a => $"{a.Value.TypeSyntax} {a.Name}"));
        var callArgs = PrependIdArg(string.Join(", ", inv.Args.Select(a => a.Name)));
        var fn = $"global::Bootsharp.Generated.Interop.{del.Proxy.Id}_{inv.Name}";
        return
            $$"""
              public sealed class {{del.Proxy.Id}} (int id) : global::Bootsharp.JSProxy(id)
              {
                  ~{{del.Proxy.Id}}() => Instances.DisposeImported(_id, this);

                  public {{inv.Return.TypeSyntax}} Invoke ({{args}}) {{Alive(inv.Void, $"{fn}({callArgs})")}}
              }
              """;
    }

    private string EmitMemberImport (MemberMeta member) => member switch {
        EventMeta evt => EmitEventImport(evt),
        PropertyMeta prop => EmitPropertyImport(prop),
        _ => EmitMethodImport((MethodMeta)member),
    };

    private string EmitEventImport (EventMeta evt)
    {
        var args = string.Join(", ", evt.Args.Select(a => $"{a.Value.TypeSyntax} {a.Name}"));
        var callArgs = string.Join(", ", evt.Args.Select(a => a.Name));
        var mod = it.Proxy is SpecializedProxy ? "public override" : "public";
        return Fmt(0,
            $"{mod} event {evt.TypeSyntax} {evt.Name};",
            $"internal void Invoke{evt.Name} ({args}) => {evt.Name}?.Invoke({callArgs});"
        );
    }

    private string EmitPropertyImport (PropertyMeta prop)
    {
        var space = $"global::Bootsharp.Generated.Interop.{it.Proxy.Id}";
        var getArgs = PrependIdArg("");
        var setArgs = PrependIdArg("value");
        var head = it.Proxy is SpecializedProxy
            ? $"public override {prop.TypeSyntax} {prop.Name}"
            : $"{prop.TypeSyntax} {it.Syntax}.{prop.Name}";
        return
            $$"""
              {{head}}
              {
                  {{Fmt(
                      prop.CanGet ? $"get {Alive(false, $"{space}_Get{prop.Name}({getArgs})")}" : null,
                      prop.CanSet ? $"set {Alive(true, $"{space}_Set{prop.Name}({setArgs})")}" : null
                  )}}
              }
              """;
    }

    private string EmitMethodImport (MethodMeta method)
    {
        var args = string.Join(", ", method.Args.Select(a => $"{a.Value.TypeSyntax} {a.Name}"));
        var callArgs = PrependIdArg(string.Join(", ", method.Args.Select(a => a.Name)));
        var name = $"{it.Proxy.Id}_{method.Endpoint}";
        var sig = it.Proxy is SpecializedProxy
            ? $"public override {method.Return.TypeSyntax} {method.Name}"
            : $"{method.Return.TypeSyntax} {it.Syntax}.{method.Name}";
        return $"{sig} ({args}) {Alive(method.Void, $"global::Bootsharp.Generated.Interop.{name}({callArgs})")}";
    }

    // The ID crosses as a bare integer, so the read of _id is the proxy's last use and the collector
    // may finalize it — releasing the ID on the JavaScript side — before the call carrying that ID
    // is made. Every member keeps the proxy alive across its interop call.
    private static string Alive (bool isVoid, string call) =>
        isVoid ? $"{{ {call}; Alive(); }}" : $"=> Alive({call});";
}
