using System.Runtime.CompilerServices;
using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// The emitted module imports <c>./signalr.mjs</c> by name, and the sample type-checks that
/// module against the declarations shipped beside it — the same seam <c>runtime.d.mts</c> holds
/// for the packaged runtime. A missing declaration is a <c>tsc</c> failure on the next publish,
/// not a workerd one.
/// </summary>
public class SignalRAssetTests
{
    [Fact]
    public void TheSignalRAssetDeclaresEveryNameTheEmittedModuleImports ()
    {
        var js = File.ReadAllText(Path.Combine(AssetDirectory(), "signalr.mjs"));
        var dts = File.ReadAllText(Path.Combine(AssetDirectory(), "signalr.d.mts"));
        foreach (var name in (string[])["hubDurableObject", "routeHub"])
        {
            Assert.True(
                js.Contains($"export function {name} (") || js.Contains($"export async function {name} ("),
                $"js/signalr.mjs no longer exports '{name}', which the emitted module imports.");
            Assert.Contains($"export declare function {name} (", dts);
        }
    }

    private static string AssetDirectory ([CallerFilePath] string self = "") =>
        Path.Combine(Path.GetDirectoryName(self)!, "..", "Bootsharp.Cloudflare.SignalR", "js");
}
