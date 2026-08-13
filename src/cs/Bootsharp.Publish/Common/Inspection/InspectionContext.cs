using System.Reflection;
using System.Runtime.Loader;

namespace Bootsharp.Publish;

/// <summary>
/// Collectible context in which the inspected solution's assemblies are loaded.
/// </summary>
/// <param name="directory">
/// Directory with the inspected assemblies; roots resolution of their dependencies.
/// </param>
internal sealed class InspectionContext (string directory) : AssemblyLoadContext("Bootsharp", isCollectible: true)
{
    private readonly AssemblyDependencyResolver? resolver = CreateResolver(directory);

    public InspectedAssembly Load (string path)
    {
        var name = Path.GetFileName(path);
        return new(LoadFile(path), path, name);
    }

    protected override Assembly? Load (AssemblyName name)
    {
        // Prefer whatever the host context already has (the framework and everything MSBuild loaded),
        // so the assemblies this task itself reflects over keep a single type identity and reference-only
        // assemblies of the inspected directory are never loaded for execution.
        if (LoadFromHost(name) is { } host) return host;
        // Dependencies the host lacks — eg, Microsoft.Extensions.* packages referenced by user assemblies —
        // come from the inspected closure. Without this, merely enumerating the exported types of an
        // assembly whose signatures touch such a package throws and fails the entire publish.
        return resolver?.ResolveAssemblyToPath(name) is { } path ? LoadFile(path) : null;
    }

    private Assembly LoadFile (string path)
    {
        // Loading via stream to not lock the file for the remainder of the build.
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return LoadFromStream(stream);
    }

    private static Assembly? LoadFromHost (AssemblyName name)
    {
        try { return Default.LoadFromAssemblyName(name); }
        catch (Exception) { return null; }
    }

    private static AssemblyDependencyResolver? CreateResolver (string directory)
    {
        // The resolver has to be rooted at an existing assembly, rather than a directory; when that
        // assembly has no deps.json beside it (the common case for a non-entry assembly), the host
        // probes app-local, which is exactly the inspected closure we want to resolve from.
        var root = Directory.EnumerateFiles(directory, "*.dll").Order().FirstOrDefault();
        return root is null ? null : new AssemblyDependencyResolver(root);
    }
}
