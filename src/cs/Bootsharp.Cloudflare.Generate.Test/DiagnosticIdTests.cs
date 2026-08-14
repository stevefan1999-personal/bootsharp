using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// Guards the one property of a diagnostic identifier that no single generator can check for itself:
/// that it means exactly one thing across the whole package.
/// </summary>
/// <remarks>
/// <para>
/// The four front ends in this assembly (worker/RPC, minimal API, HTML templates, SignalR hubs) were
/// written independently and each allocates its own <c>CFW</c> numbers. They report under different
/// categories, which hides the hazard: a user suppresses, escalates or documents a diagnostic by its
/// <em>id</em>, so two generators sharing one number means <c>&lt;NoWarn&gt;CFW031&lt;/NoWarn&gt;</c>
/// aimed at an undeclared hub payload also silences an unsafe HTML interpolation — the one class of
/// mistake the HTML tier exists to make loud. That collision reached the tree once (the hub and
/// template generators both claimed 031 and 032); this test is why it cannot again.
/// </para>
/// <para>
/// Scanned from source rather than reflected, because a Roslyn generator's descriptors are built
/// inside the reporting call and are not enumerable from outside a compilation. The block table
/// below is therefore also the allocation record: a new front end takes the next free ten.
/// </para>
/// </remarks>
public class DiagnosticIdTests
{
    /// <summary>
    /// Directory of a front end, and one id range it owns. Ranges may not overlap; a front end that
    /// fills a range takes the next free ten and appears here twice, because renumbering a shipped
    /// id is the one repair that is never available — a user's <c>NoWarn</c> names it.
    /// </summary>
    private static readonly (string Dir, int First, int Last)[] blocks = [
        ("", 10, 19),               // worker entrypoints, env bindings, RPC projection
        ("MinimalApi", 20, 29),     // route patterns, parameter binding, results
        ("Html", 30, 39),           // compiled HTML templates
        ("SignalR", 40, 49),        // hub dispatch
        ("", 50, 59),               // the same front end, continued: CFW019 filled its first ten
        // Reserved rather than scanned: the .cshtml generator ships in its own analyzer assembly
        // (Bootsharp.Cloudflare.Razor), because it redistributes a 1 MB pinned Razor compiler that
        // no other front end should make a consumer download. The scan below cannot reach it, so the
        // row exists to keep the block spoken for — the id table is the allocation record, and an
        // allocation that is only written down in the other project is one nobody here would see.
        ("../Bootsharp.Cloudflare.Razor", 60, 69)
    ];

    /// <summary>Every <c>new(...)("CFWnnn", "Title", ...)</c> the generator sources contain.</summary>
    private static IEnumerable<(string Id, string Title, string File)> Reported ()
    {
        foreach (var file in Directory.EnumerateFiles(GeneratorRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(file), """"("CFW\d{3}"), *("(?:[^"\\]|\\.)*")""""))
                yield return (match.Groups[1].Value.Trim('"'), match.Groups[2].Value.Trim('"'), file);
        }
    }

    [Fact]
    public void EveryIdCarriesOneTitle ()
    {
        var titles = new Dictionary<string, (string Title, string File)>();
        foreach (var (id, title, file) in Reported())
        {
            if (!titles.TryGetValue(id, out var seen)) { titles[id] = (title, file); continue; }
            Assert.True(seen.Title == title,
                $"{id} is reported as '{seen.Title}' in {Path.GetFileName(seen.File)} and as " +
                $"'{title}' in {Path.GetFileName(file)}. A user suppresses by id, so one number " +
                "cannot carry two meanings — take the next free id in this front end's block.");
        }
    }

    [Fact]
    public void EveryIdFallsInItsFrontEndBlock ()
    {
        foreach (var (id, title, file) in Reported())
        {
            var relative = Path.GetRelativePath(GeneratorRoot(), file);
            var dir = Path.GetDirectoryName(relative) is { Length: > 0 } d ? d.Split(Path.DirectorySeparatorChar)[0] : "";
            var owned = Array.FindAll(blocks, b => b.Dir == dir);
            Assert.True(owned.Length > 0,
                $"{relative} reports {id} but claims no id block. Add its directory to the table in {nameof(DiagnosticIdTests)}.");
            var number = int.Parse(id[3..]);
            Assert.True(Array.Exists(owned, b => number >= b.First && number <= b.Last),
                $"{id} ('{title}') is reported from {relative}, whose blocks are " +
                $"{string.Join(", ", owned.Select(Range))}. Blocks keep front ends from colliding.");
        }
    }

    /// <summary>
    /// Compared pairwise by position rather than by owner, so that the two ranges one front end
    /// holds are checked against each other as well: the hazard is an id meaning two things, and a
    /// range accidentally restated under the same directory produces exactly that.
    /// </summary>
    [Fact]
    public void BlocksDoNotOverlap ()
    {
        for (var left = 0; left < blocks.Length; left++)
        for (var right = left + 1; right < blocks.Length; right++)
            Assert.True(blocks[left].Last < blocks[right].First || blocks[right].Last < blocks[left].First,
                $"blocks '{blocks[left].Dir}' {Range(blocks[left])} and " +
                $"'{blocks[right].Dir}' {Range(blocks[right])} overlap");
    }

    private static string Range ((string Dir, int First, int Last) block) =>
        $"CFW{block.First:000}-CFW{block.Last:000}";

    /// <summary>The generator project beside this one, located from this file rather than from the
    /// working directory so the scan is independent of how the suite is launched.</summary>
    private static string GeneratorRoot ([CallerFilePath] string self = "") =>
        Path.Combine(Path.GetDirectoryName(self)!, "..", "Bootsharp.Cloudflare.Generate");
}
