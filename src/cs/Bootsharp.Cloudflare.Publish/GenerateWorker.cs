using Microsoft.Build.Framework;

namespace Bootsharp.Cloudflare.Publish;

/// <summary>
/// Emits the worker's ESM entrypoint module and places the packaged runtime assets beside it,
/// after the native link and after Bootsharp has published its own modules. Running
/// here rather than inside the Roslyn generator is what removes the build-time write into the
/// source tree, and with it the window in which <c>wrangler deploy</c> could ship a module emitted
/// from C# that is no longer the C# being deployed.
/// </summary>
public sealed class GenerateWorker : Microsoft.Build.Utilities.Task
{
    /// <summary>Where the emitted module and the runtime assets are placed.</summary>
    public required string WorkerDirectory { get; set; }
    /// <summary>The package's <c>js/</c> folder, copied verbatim next to the emitted module.</summary>
    public required string AssetDirectory { get; set; }
    /// <summary>The compiled app assembly the projection is read from.</summary>
    public required string EntryAssembly { get; set; }
    /// <summary>Compile-time reference closure, resolving everything the app assembly names.</summary>
    public required ITaskItem[] References { get; set; }
    /// <summary>The wasm binary workerd imports as an already-compiled module.</summary>
    public required string WasmFile { get; set; }
    /// <summary>Bootsharp's published ESM entry, imported lazily on the first event.</summary>
    public required string BootModule { get; set; }

    public override bool Execute ()
    {
        if (!Exists(EntryAssembly, "compiled app assembly")) return false;
        if (!Exists(WasmFile, "wasm binary")) return false;
        if (!Exists(BootModule, "Bootsharp ES module")) return false;
        Directory.CreateDirectory(WorkerDirectory);
        CopyAssets();
        WriteEntrypoints(Emit());
        Log.LogMessage(MessageImportance.High, $"Cloudflare worker module emitted at {WorkerDirectory}");
        return true;
    }

    private string Emit ()
    {
        var references = References.Select(static r => r.GetMetadata("FullPath"));
        using var projector = new MetadataProjector(Path.GetFullPath(EntryAssembly), references,
            message => Log.LogWarning("{0}", message));
        return JsEmitter.Emit(
            projector.ProjectEntrypoints(),
            projector.ProjectEnv(),
            projector.ProjectOptions(),
            Relative(WasmFile),
            Relative(BootModule));
    }

    /// <summary>
    /// The assets are hand-written JavaScript and its declarations, so they are copied rather than
    /// generated; whatever the package ships lands next to the module that imports it.
    /// </summary>
    private void CopyAssets ()
    {
        foreach (var asset in Directory.GetFiles(AssetDirectory))
            File.Copy(asset, Path.Combine(WorkerDirectory, Path.GetFileName(asset)), true);
    }

    /// <summary>
    /// Writes with LF regardless of host, so the same sources emit the same bytes on Linux and
    /// Windows — the file is an input to wrangler's bundler and to the repo's diffs.
    /// </summary>
    private void WriteEntrypoints (string content) =>
        File.WriteAllText(
            Path.Combine(WorkerDirectory, "entrypoints.ts"),
            content.Replace("\r\n", "\n"));

    /// <summary>
    /// Module specifier for a build artifact, relative to the emitted module. Computed rather than
    /// configured: the layout of the publish output is the build's decision, and a specifier that
    /// is derived from it cannot drift from where the file actually landed.
    /// </summary>
    private string Relative (string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(WorkerDirectory), Path.GetFullPath(path))
            .Replace('\\', '/');
        return relative.StartsWith(".", StringComparison.Ordinal) ? relative : "./" + relative;
    }

    private bool Exists (string path, string subject)
    {
        if (File.Exists(path)) return true;
        // Placeholder rather than interpolation, as with the projector's warnings: a path is
        // arbitrary text and MSBuild reads the message as a composite format string.
        Log.LogError("{0}", $"Cloudflare worker emission needs the {subject}, which is not at '{path}'.");
        return false;
    }
}
