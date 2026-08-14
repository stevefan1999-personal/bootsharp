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
    /// <summary>Directory of a front end, and the id range it owns. Ranges may not overlap.</summary>
    private static readonly (string Dir, int First, int Last)[] blocks = [
        ("", 10, 19),               // worker entrypoints, env bindings, RPC projection
        ("MinimalApi", 20, 29),     // route patterns, parameter binding, results
        ("Html", 30, 39),           // compiled HTML templates
        ("SignalR", 40, 49)         // hub dispatch
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
            var block = Array.Find(blocks, b => b.Dir == dir);
            Assert.True(block.Dir is not null || dir == "",
                $"{relative} reports {id} but claims no id block. Add its directory to the table in {nameof(DiagnosticIdTests)}.");
            var number = int.Parse(id[3..]);
            Assert.True(number >= block.First && number <= block.Last,
                $"{id} ('{title}') is reported from {relative}, whose block is " +
                $"CFW{block.First:000}-CFW{block.Last:000}. Blocks keep front ends from colliding.");
        }
    }

    [Fact]
    public void BlocksDoNotOverlap ()
    {
        foreach (var left in blocks)
        foreach (var right in blocks)
            if (left.Dir != right.Dir)
                Assert.True(left.Last < right.First || right.Last < left.First,
                    $"blocks '{left.Dir}' and '{right.Dir}' overlap");
    }

    /// <summary>The generator project beside this one, located from this file rather than from the
    /// working directory so the scan is independent of how the suite is launched.</summary>
    private static string GeneratorRoot ([CallerFilePath] string self = "") =>
        Path.Combine(Path.GetDirectoryName(self)!, "..", "Bootsharp.Cloudflare.Generate");
}
