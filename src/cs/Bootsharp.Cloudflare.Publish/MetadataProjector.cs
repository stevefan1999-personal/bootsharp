using System.Reflection;
using Bootsharp.Cloudflare.Projection;

namespace Bootsharp.Cloudflare.Publish;

/// <summary>
/// App-level configuration the emitted module is parameterised by. It is read from the app
/// assembly's own attributes rather than from build properties, so that it travels with the code it
/// describes and both front ends read one declaration instead of restating a default (
/// milestone 4.
/// </summary>
/// <param name="AssetsBinding">Binding static assets are served from, per wrangler's
/// <c>assets.binding</c>, or null when the app declares no assets at all. Null means the emitted
/// module never names a binding: wrangler projects <c>Env</c> from the bindings it configures, so
/// naming one it does not is a type error against the app's own worker-configuration.d.ts — and a
/// worker serving no assets has nothing to fall back to anyway.</param>
/// <param name="AssetPrefixes">Request paths answered from that binding without booting the guest;
/// empty when the app declares none, in which case nothing is short-circuited.</param>
/// <param name="LogSink">Interop member the runtime binds its log handler to, or null when the app
/// imports no sink — assuming the member exists is what used to fail non-logging apps at boot.</param>
internal sealed record WorkerOptions(string? AssetsBinding, string[] AssetPrefixes, string? LogSink);

/// <summary>
/// Resolves the worker projection from the compiled app assembly, applying the same
/// <see cref="Rules"/> the Roslyn generator applies to symbols. Reading metadata rather than
/// receiving a model from the generator is the repo's established shape — Bootsharp.Publish
/// inspects the built assemblies for exactly this reason — and it is the only channel available:
/// an analyzer's sole output is source, and baking the module into a C# literal is what
/// milestone 2 removed.
/// </summary>
/// <remarks>
/// Nothing is executed. A <see cref="MetadataLoadContext"/> reads the browser-wasm assemblies as
/// data, resolved from the compile-time reference closure MSBuild already computed, so no assembly
/// targeting a foreign runtime is ever loaded for execution and no dependency can go missing.
/// </remarks>
internal sealed class MetadataProjector : IDisposable
{
    private readonly MetadataLoadContext context;
    private readonly Assembly assembly;
    private readonly Action<string> warn;

    /// <param name="warn">Sink for a shape the projection refuses; the MSBuild task routes it to
    /// its own log, and the suite collects it. Nothing here needs the build engine.</param>
    public MetadataProjector (string assemblyPath, IEnumerable<string> references, Action<string> warn)
    {
        this.warn = warn;
        var paths = new HashSet<string>(references, StringComparer.OrdinalIgnoreCase) { assemblyPath };
        // No core assembly name is passed: the closure is a reference pack during a publish and the
        // runtime's own assemblies under test, which name their core assembly differently, and the
        // context guesses correctly from the well-known names in both cases.
        context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        assembly = context.LoadFromAssemblyPath(assemblyPath);
    }

    public void Dispose () => context.Dispose();

    /// <summary>Entrypoint classes the app declares, ordered so the emitted module is stable.</summary>
    public Entrypoint[] ProjectEntrypoints () => Types()
        .Where(static type => type.IsClass && !type.IsAbstract)
        .Select(Project)
        .Where(static entrypoint => entrypoint is not null)
        .Select(static entrypoint => entrypoint!)
        .OrderBy(static entrypoint => entrypoint.Kind, StringComparer.Ordinal)
        .ThenBy(static entrypoint => entrypoint.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Bindings the app's env interface declares, found through the same <c>[WorkerEnv]</c> marker
    /// the generator resolves: where an app declares its bindings, and what it calls the interface,
    /// is the app's business. Ordered so a rebuild emits the same module.
    /// </summary>
    public EnvProperty[] ProjectEnv ()
    {
        var env = Types()
            .Where(static type => type.IsInterface && Marked(type, Rules.EnvAttribute))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (env is null) return [];
        return env.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static property => new EnvProperty(
                property.Name, property.PropertyType.Namespace ?? "", property.PropertyType.Name))
            .ToArray();
    }

    /// <summary>Worker configuration the app declares as assembly attributes.</summary>
    public WorkerOptions ProjectOptions ()
    {
        var assets = Attribute(assembly.GetCustomAttributesData(), Rules.AssetsAttribute);
        return new WorkerOptions(
            // No attribute means no assets binding at all. With one, an unset property reads as null
            // here rather than as the attribute's default, which is applied by a constructor that
            // never runs; an empty one would emit `this.env.`.
            assets is null ? null
                : Named(assets, "Binding") is string { Length: > 0 } binding ? binding : Rules.DefaultAssetsBinding,
            assets is null ? [] : Strings(assets.ConstructorArguments.FirstOrDefault()),
            ProjectLogSink());
    }

    /// <summary>
    /// Name the guest's log sink is reachable under on the booted interop module, or null when the
    /// app imports none. It is the interface's own name, which is how Bootsharp names an import.
    /// </summary>
    private string? ProjectLogSink () => assembly.GetCustomAttributesData()
        .Where(static data => data.AttributeType.FullName == Rules.ImportAttribute)
        // The attribute takes `params Type[]`, so the types arrive as one array-valued argument.
        .SelectMany(static data => Types(data.ConstructorArguments.FirstOrDefault()))
        .FirstOrDefault(static type => type.FullName == Rules.LogSink)?.Name;

    private static bool Marked (MemberInfo member, string attribute) =>
        Attribute(member.GetCustomAttributesData(), attribute) is not null;

    private static CustomAttributeData? Attribute (IEnumerable<CustomAttributeData> data, string attribute) =>
        data.FirstOrDefault(item => item.AttributeType.FullName == attribute);

    private static object? Named (CustomAttributeData? data, string name) => data?.NamedArguments
        .FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value;

    private static string[] Strings (CustomAttributeTypedArgument argument) =>
        Elements(argument).Select(static element => element.Value).OfType<string>().ToArray();

    private static IEnumerable<Type> Types (CustomAttributeTypedArgument argument) =>
        Elements(argument).Select(static element => element.Value).OfType<Type>();

    /// <summary>
    /// Items of an array-valued attribute argument. An absent argument reads as a default struct
    /// whose value is null, which is exactly the empty case.
    /// </summary>
    private static IEnumerable<CustomAttributeTypedArgument> Elements (CustomAttributeTypedArgument argument) =>
        argument.Value as IEnumerable<CustomAttributeTypedArgument> ?? [];

    /// <summary>
    /// Types the app assembly declares. A type whose dependencies did not resolve is dropped rather
    /// than failing the publish: it cannot be an entrypoint, because an entrypoint's base type comes
    /// from the runtime package that is always in the reference closure.
    /// </summary>
    private Type[] Types ()
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error)
        {
            warn($"Failed to read some types of '{assembly.GetName().Name}'. Error: {error.Message}");
            return error.Types.Where(static type => type is not null).Select(static type => type!).ToArray();
        }
    }

    private Entrypoint? Project (Type type)
    {
        if (Kind(type) is not { } kind) return null;
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsProjectable)
            .Select(method => Project(kind, method))
            .Where(static method => method is not null)
            .Select(static method => method!)
            // A hub-hosting Durable Object inherits its transport surface rather than declaring it,
            // and DeclaredOnly is what both front ends read — so without this the emitted module
            // would expose no RPC at all and the SignalR JavaScript half would call four methods
            // that were never emitted. The generator injects the same four. See Rules.HubTransports.
            .Concat(HostsHub(type) ? HubTransport() : [])
            .ToArray();
        return new Entrypoint(kind, type.Name, type.Namespace ?? "", new(methods));
    }

    /// <summary>Whether the class derives from <c>HubDurableObject&lt;THub, TEnv&gt;</c>.</summary>
    private static bool HostsHub (Type type) => Rules.HostsHub(Bases(type));

    /// <summary>Full names of every base, unbound — metadata spells a generic name with its arity.</summary>
    private static IEnumerable<string> Bases (Type type)
    {
        for (var current = BaseType(type); current is not null; current = BaseType(current))
            yield return current.Namespace is { Length: > 0 } space
                ? $"{space}.{Rules.Unbound(current.Name)}" : Rules.Unbound(current.Name);
    }

    private static IEnumerable<Method> HubTransport () => Rules.HubTransports
        .Select(static m => new Method(m.CsName, m.JsName, "rpc", m.Return, m.Await));

    /// <summary>
    /// Walks to the runtime base the way the generator does, on the unbound name: the bases are
    /// generic over the app's env, and metadata spells a generic name with its arity.
    /// </summary>
    private static string? Kind (Type type)
    {
        for (var current = BaseType(type); current is not null; current = BaseType(current))
            if (Rules.Kind(current.Namespace ?? "", Rules.Unbound(current.Name)) is { } kind)
                return kind;
        return null;
    }

    /// <summary>A base type outside the reference closure ends the walk instead of the publish.</summary>
    private static Type? BaseType (Type type)
    {
        try { return type.BaseType; }
        catch (FileNotFoundException) { return null; }
    }

    private static bool IsProjectable (MethodInfo method) =>
        !method.IsSpecialName && !method.IsStatic && !OverridesObject(method);

    /// <summary>
    /// Whether the method is an override of one of <c>object</c>'s public virtual members, which
    /// the generator drops as well. <c>GetBaseDefinition</c> would say so directly but is not
    /// supported under a <see cref="MetadataLoadContext"/>; matching the signatures is exact rather
    /// than approximate, because <c>object</c> declares no other public virtual instance method.
    /// </summary>
    private static bool OverridesObject (MethodInfo method) => method.IsVirtual && method.Name switch {
        "ToString" or "GetHashCode" => method.GetParameters().Length == 0,
        "Equals" => method.GetParameters() is [{ ParameterType.FullName: "System.Object" }],
        _ => false
    };

    /// <summary>
    /// Classifies one public method exactly as the generator does. Anything the generator refuses
    /// is refused here too — as a warning, because the refusal is already a compile error at the
    /// source location that caused it, so a publish can only reach this code with a clean model.
    /// </summary>
    private Method? Project (string kind, MethodInfo method)
    {
        var jsName = Rules.ToJs(method.Name);
        if (Rules.HandlerSlot(kind, method.Name) is { } slot)
            return new Method(method.Name, jsName, slot, "void", true);
        if (kind is not "DurableObject" || Rules.Reserved.Contains(jsName)) return Refuse(kind, method);
        var (returned, awaited) = ReturnShape(method.ReturnType);
        if (returned is null) return Refuse(kind, method);
        var optionals = method.GetParameters().Count(static parameter => parameter.IsOptional);
        if (optionals > 1 || !method.GetParameters().All(IsProjectable)) return Refuse(kind, method);
        return new Method(method.Name, jsName, "rpc", returned, awaited);
    }

    private Method? Refuse (string kind, MethodInfo method)
    {
        warn(
            $"'{method.DeclaringType?.FullName}.{method.Name}' is not a projectable {kind} member " +
            "and was left out of the worker module. The generator reports the cause with a CFW diagnostic.");
        return null;
    }

    /// <summary>
    /// Whether the argument can arrive in the JSON array. Nullable annotations are not read: only
    /// the generated C# dispatch tells the two string readers apart, and that is emitted from
    /// symbols where the annotation is available.
    /// </summary>
    private static bool IsProjectable (ParameterInfo parameter) =>
        !parameter.ParameterType.IsByRef
        && (!parameter.IsOptional || parameter.HasDefaultValue)
        && Rules.ParameterKind(parameter.ParameterType.Namespace ?? "", parameter.ParameterType.Name, false) is not null;

    private static (string? Kind, bool Await) ReturnShape (Type type)
    {
        if (type.Namespace == "System.Threading.Tasks" && Rules.Unbound(type.Name) == "Task")
            return type.IsGenericType ? (ValueKind(type.GetGenericArguments()[0]), true) : ("void", true);
        if (type.FullName == "System.Void") return ("void", false);
        return (ValueKind(type), false);
    }

    private static string? ValueKind (Type type) => Rules.ValueKind(type.Namespace ?? "", type.Name);
}
