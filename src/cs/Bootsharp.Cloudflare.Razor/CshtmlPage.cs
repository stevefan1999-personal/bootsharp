using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bootsharp.Cloudflare.Razor;

/// <summary>The two build properties a page's identity is derived from.</summary>
/// <remarks>Both are made compiler-visible by this package's own targets, so a project that
/// references the package needs no configuration at all.</remarks>
internal sealed record CshtmlOptions (string RootNamespace, string ProjectDirectory)
{
    private const string fallbackNamespace = "Views";

    public static CshtmlOptions Read (AnalyzerConfigOptionsProvider provider)
    {
        var root = Value(provider, "build_property.RootNamespace") ?? fallbackNamespace;
        return new CshtmlOptions(root, Value(provider, "build_property.ProjectDir") ?? "");
    }

    private static string? Value (AnalyzerConfigOptionsProvider provider, string key) =>
        provider.GlobalOptions.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}

/// <summary>
/// One <c>.cshtml</c> file, and the C# identity it compiles to.
/// </summary>
/// <remarks>
/// <para>
/// Namespace is the project's root namespace plus the page's own folders, and the class is the file
/// name — <c>Views/Shared/_Card.cshtml</c> in an app rooted at <c>App</c> becomes
/// <c>App.Views.Shared.Card</c>. Deriving the namespace from the folder rather than flattening
/// everything into one is what keeps two same-named pages in different folders from colliding, which
/// is otherwise the first thing an app with more than a handful of pages hits.
/// </para>
/// <para>
/// A leading underscore is dropped from the class name, so MVC's partial-naming habit
/// (<c>_Card.cshtml</c>) survives and reads as <c>Card.Render(html, item)</c> at the call site. The
/// underscore is a file-naming convention, never a difference in what the page compiles to: on this
/// tier every page is a partial and every partial is a page.
/// </para>
/// <para>
/// The record is a value: the incremental pipeline compares one of these against the previous
/// build's to decide whether the page has to be compiled again, so every field it holds must
/// participate in equality — which is why the page's text is carried here rather than re-read.
/// </para>
/// </remarks>
internal sealed record CshtmlPage (string Path, string RelativePath, string Text, string Namespace, string ClassName)
{
    /// <summary>What a handler writes to render this page.</summary>
    public string QualifiedName => $"{Namespace}.{ClassName}";

    /// <summary>Derived from the relative path rather than from the class name, so that two pages
    /// sharing a name in different folders still produce two distinct generated files. Roslyn refuses
    /// a repeated hint name outright.</summary>
    public string HintName => $"{Sanitized(RelativePath)}.g.cs";

    /// <summary>MVC's import conventions, which have no meaning without an MVC runtime and would
    /// otherwise be compiled as if they were ordinary pages.</summary>
    public bool IsImportConvention =>
        System.IO.Path.GetFileName(Path).Equals("_ViewImports.cshtml", StringComparison.OrdinalIgnoreCase) ||
        System.IO.Path.GetFileName(Path).Equals("_ViewStart.cshtml", StringComparison.OrdinalIgnoreCase);

    public static CshtmlPage? Read (AdditionalText file, CshtmlOptions options, CancellationToken token)
    {
        if (file.GetText(token) is not { } text) return null;
        var relative = Relative(file.Path, options.ProjectDirectory);
        var folders = System.IO.Path.GetDirectoryName(relative) ?? "";
        var segments = folders.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != "." && segment != "..")
            .Select(Identifier);
        var qualifiers = new[] { options.RootNamespace }.Concat(segments)
            .Where(part => !string.IsNullOrEmpty(part));
        var name = Identifier(System.IO.Path.GetFileNameWithoutExtension(file.Path).TrimStart('_'));
        return new CshtmlPage(file.Path, relative, text.ToString(), string.Join(".", qualifiers), name);
    }

    /// <summary>The page's path as the author sees it, which is also what its <c>#line</c> directives
    /// carry: an absolute path in generated code would make the build non-reproducible across
    /// machines for no gain, since the compiler resolves a relative one against the project.</summary>
    private static string Relative (string path, string root)
    {
        if (root.Length == 0) return System.IO.Path.GetFileName(path);
        var prefix = root.TrimEnd('/', '\\') + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(prefix.Length)
            : System.IO.Path.GetFileName(path);
    }

    /// <summary>Anything a file system allows in a name, reduced to what C# allows in one.</summary>
    private static string Identifier (string name)
    {
        var identifier = new StringBuilder();
        foreach (var character in name)
            identifier.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        if (identifier.Length == 0) identifier.Append('_');
        if (char.IsDigit(identifier[0])) identifier.Insert(0, '_');
        return identifier.ToString();
    }

    /// <summary>A whole path folded into one file name, dots kept so the result still reads as the
    /// path it came from when it shows up in a build log.</summary>
    private static string Sanitized (string path)
    {
        var sanitized = new StringBuilder();
        foreach (var character in path)
            sanitized.Append(char.IsLetterOrDigit(character) || character == '_' || character == '.'
                ? character : '.');
        return sanitized.Length == 0 ? "Page" : sanitized.ToString();
    }
}
