using System.Xml.Linq;
using Microsoft.Build.Utilities;

namespace Bootsharp.Publish;

internal sealed class SolutionInspector (TaskLoggingHelper logger)
{
    private readonly List<InspectedAssembly> asses = [];
    private readonly List<DocMeta> docs = [];

    /// <summary>
    /// Inspects specified solution assembly paths in the output directory.
    /// </summary>
    /// <param name="directory">Directory with the inspected assemblies; roots dependency resolution.</param>
    /// <param name="paths">Absolute paths of the assemblies to inspect.</param>
    public SolutionInspection Inspect (string directory, IEnumerable<string> paths)
    {
        var ctx = new InspectionContext(directory);
        var types = new TypeInspector(Warn);
        foreach (var pth in paths) LoadAssembly(pth, ctx);
        foreach (var ass in asses) ResolvePreferences(ass);
        foreach (var ass in asses) InspectTypes(ass, types);
        foreach (var ass in asses) InspectDocs(ass);
        return new(ctx) { Types = types.Collect(), Docs = docs.ToArray() };
    }

    private void LoadAssembly (string path, InspectionContext ctx)
    {
        if (!IsUserAssembly(Path.GetFileNameWithoutExtension(path))) return;
        try { asses.Add(ctx.Load(path)); }
        catch (Exception e) { Warn(Describe(path), e); }
    }

    private void ResolvePreferences (InspectedAssembly ass)
    {
        try { Preferences.Resolve(ass.Assembly); }
        catch (Exception e) { Warn(Describe(ass.Path), e); }
    }

    private void InspectTypes (InspectedAssembly ass, TypeInspector types)
    {
        // The inspector already skips individual types it fails to load; this guards the remainder
        // (assembly-level attributes) so that an unresolvable dependency never fails the publish.
        try { types.Inspect(ass.Assembly); }
        catch (Exception e) { Warn(Describe(ass.Path), e); }
    }

    private void InspectDocs (InspectedAssembly ass)
    {
        var xmlPath = Path.ChangeExtension(ass.Path, ".xml");
        var name = Path.GetFileNameWithoutExtension(ass.Name);
        if (File.Exists(xmlPath)) docs.Add(new(name, XDocument.Load(xmlPath)));
    }

    private static string Describe (string path) => $"'{Path.GetFileName(path)}' assembly";

    private void Warn (string subject, Exception error) => logger.LogWarning(
        $"Failed to inspect {subject}. Error: {error}");
}
