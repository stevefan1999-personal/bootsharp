namespace Bootsharp.Publish.Test;

public class InspectionTest : GenerateJSTest
{
    [Fact]
    public void AllAssembliesAreInspected ()
    {
        AddAssembly("foo.dll",
            WithClass("Foo", "[Export] public static void Inv () {}")
        );
        Execute();
        Assert.True(File.Exists($"{Task.BuildDirectory}/generated/modules/foo.g.mjs"));
    }

    [Fact]
    public void WarnsWhenAssemblyFailedToLoad ()
    {
        AddAssembly("foo.dll",
            WithClass("[Export] public static void InvFoo () {}")
        );
        AddAssembly("bar.dll",
            WithClass("[Export] public static void InvBar () {}")
        );
        File.WriteAllText(Path.Combine(Project.Root, "foo.dll"), "corrupted");
        Execute();
        Assert.Contains(Engine.Warnings, w => w.Contains("Failed to inspect 'foo.dll' assembly"));
    }

    [Fact]
    public void ResolvesDependenciesFromInspectedDirectory ()
    {
        // Dependencies excluded from inspection (Microsoft.*) are still required to inspect
        // the user assemblies referencing them, and the MSBuild host doesn't have them loaded.
        AddAssembly("Microsoft.Mock.Dependency.dll",
            With("Microsoft.Mock.Dependency", "public class Payload { public string Value { get; set; } = \"\"; }")
        );
        AddAssembly("foo.dll",
            WithClass("Foo", "[Export] public static Microsoft.Mock.Dependency.Payload Get () => null!;")
        );
        Execute();
        Assert.DoesNotContain(Engine.Warnings, w => w.Contains("Failed to inspect"));
        Contains("foo.g.mjs", "get");
    }

    [Fact]
    public void WarnsAndSkipsTypeWithUnresolvableDependency ()
    {
        AddAssembly("Microsoft.Mock.Dependency.dll",
            With("Microsoft.Mock.Dependency", "public class Payload;")
        );
        AddAssembly("foo.dll",
            WithClass("Good", "[Export] public static void Inv () {}"),
            WithClass("Bad", "[Export] public static Microsoft.Mock.Dependency.Payload Get () => null!;")
        );
        File.Delete(Path.Combine(Project.Root, "Microsoft.Mock.Dependency.dll"));
        Execute();
        Assert.Contains(Engine.Warnings, w => w.Contains("Failed to inspect 'Bad.Class' type"));
        Assert.True(File.Exists($"{Task.BuildDirectory}/generated/modules/good.g.mjs"));
        Assert.False(File.Exists($"{Task.BuildDirectory}/generated/modules/bad.g.mjs"));
    }

    [Fact]
    public void WarnsWhenMissingSpecializationPair ()
    {
        AddAssembly(With(
            """
            public class Custom;
            [SpecializeExport(typeof(Custom))]
            public sealed class CustomExport (Custom it) : SpecializedExport(it);
            """));
        Execute();
        Assert.Contains(Engine.Warnings, w => w.Contains("missing the paired import"));
    }

    [Fact]
    public void IgnoresAssembliesNotPresentInBuildDirectory ()
    {
        var buildDir = $"{Project.Root}/build";
        Task.BuildDirectory = buildDir;
        Directory.CreateDirectory(buildDir);
        File.WriteAllText($"{buildDir}/foo.wasm", "");

        foreach (var file in Directory.EnumerateFiles(Project.Root))
            File.WriteAllText($"{buildDir}/{Path.GetFileName(file)}", File.ReadAllText(file));

        AddAssembly("foo.dll",
            WithClass("Foo", "[Export] public static void InvFoo () {}")
        );
        AddAssembly("bar.dll",
            WithClass("Bar", "[Export] public static void InvBar () {}")
        );
        Execute();

        Assert.True(File.Exists($"{Task.BuildDirectory}/generated/modules/foo.g.mjs"));
        Assert.False(File.Exists($"{Task.BuildDirectory}/generated/modules/bar.g.mjs"));
    }

    [Fact]
    public void DoesntIgnoreAssembliesWhenLLVM ()
    {
        Task.LLVM = true;

        var buildDir = $"{Project.Root}/build";
        Task.BuildDirectory = buildDir;
        Directory.CreateDirectory(buildDir);
        File.WriteAllText($"{buildDir}/foo.wasm", "");

        foreach (var file in Directory.EnumerateFiles(Project.Root))
            File.WriteAllText($"{buildDir}/{Path.GetFileName(file)}", File.ReadAllText(file));

        AddAssembly("foo.dll",
            WithClass("Foo", "[Export] public static void InvFoo () {}")
        );
        AddAssembly("bar.dll",
            WithClass("Bar", "[Export] public static void InvBar () {}")
        );
        Execute();

        Assert.True(File.Exists($"{Task.BuildDirectory}/generated/modules/foo.g.mjs"));
        Assert.True(File.Exists($"{Task.BuildDirectory}/generated/modules/bar.g.mjs"));
    }
}
