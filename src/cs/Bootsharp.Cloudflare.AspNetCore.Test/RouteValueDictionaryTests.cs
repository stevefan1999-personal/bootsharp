using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// vendors <c>RouteValueDictionary</c> "with its reflection ctor and
/// <c>PropertyHelper</c> storage excised". The excision is taken by compiling upstream's own lean
/// selection rather than by hand, so what these assert is that the selection is the one that was
/// taken — a member back in the graph would otherwise be invisible until an ILC link failed.
/// </summary>
public class RouteValueDictionaryExcisionTests
{
    private static readonly Type type = typeof(RouteValueDictionary);

    private const BindingFlags everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    /// The reflection constructor: it reads an anonymous object's properties through
    /// <c>PropertyHelper</c>, which is the whole reason the type needed a trim annotation upstream.
    /// </summary>
    [Fact]
    public void ConstructorTakingAnArbitraryObjectIsGone () =>
        Assert.Null(type.GetConstructor([typeof(object)]));

    /// <summary>
    /// <c>FromArray</c> and the string-valued enumerable constructor are part of the same lean
    /// selection; taking one and not the others would leave the vendored file diverging from
    /// upstream's.
    /// </summary>
    [Fact]
    public void TheRestOfTheNonLeanSurfaceIsGoneToo ()
    {
        Assert.Null(type.GetMethod("FromArray", everything));
        Assert.Null(type.GetConstructor([typeof(IEnumerable<KeyValuePair<string, string?>>)]));
    }

    /// <summary>
    /// The property-backed storage and its cache. A field surviving would mean the reflective read
    /// path is still reachable even if nothing public calls it.
    /// </summary>
    [Fact]
    public void PropertyHelperStorageIsGone ()
    {
        Assert.Null(type.GetNestedType("PropertyStorage", everything));
        Assert.Null(type.GetNestedType("MetadataUpdateHandler", everything));
        foreach (var field in type.GetFields(everything))
            Assert.DoesNotContain("property", field.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nothing in the assembly reaches the activator or the regex constraint the forbidden set names
    /// — asserted here as well as in the build target, so the suite fails on the same
    /// regression the packaging does.
    /// </summary>
    [Fact]
    public void AssemblyDeclaresNoneOfTheForbiddenTypes ()
    {
        var declared = type.Assembly.GetTypes().Select(static declaring => declaring.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var forbidden in new[] {
                     "RequestDelegateFactory", "DfaMatcher", "ILEmitTrie",
                     "DefaultJsonTypeInfoResolver", "ParameterPolicyActivator", "RegexRouteConstraint"
                 })
            Assert.DoesNotContain(forbidden, declared);
    }

    /// <summary>
    /// The identity rule of, read off the built assembly: nothing shared-framework-only is
    /// referenced, because there is no shared framework here to reference it from.
    /// </summary>
    [Fact]
    public void AssemblyReferencesNoAspNetCoreAssembly ()
    {
        foreach (var reference in type.Assembly.GetReferencedAssemblies())
            Assert.DoesNotContain("Microsoft.AspNetCore", reference.Name ?? "", StringComparison.Ordinal);
    }
}

/// <summary>What survives the excision still has to be a route value dictionary.</summary>
public class RouteValueDictionaryBehaviourTests
{
    [Fact]
    public void EmptyDictionaryReadsAsEmpty ()
    {
        var values = new RouteValueDictionary();
        Assert.Empty(values);
        Assert.Null(values["missing"]);
        Assert.False(values.ContainsKey("missing"));
        Assert.False(values.TryGetValue("missing", out _));
    }

    /// <summary>Route values are matched the way route patterns are: without regard to case.</summary>
    [Fact]
    public void KeysAreCaseInsensitive ()
    {
        var values = new RouteValueDictionary { ["Id"] = "7" };
        Assert.Equal("7", values["id"]);
        Assert.True(values.ContainsKey("ID"));
        values["ID"] = "8";
        Assert.Single(values);
        Assert.Equal("8", values["Id"]);
    }

    [Fact]
    public void PairsCanBeSeededCopiedAndEnumerated ()
    {
        var seeded = new RouteValueDictionary(new Dictionary<string, object?> { ["a"] = 1, ["b"] = "two" });
        var copy = new RouteValueDictionary(seeded);
        Assert.Equal(2, copy.Count);
        Assert.Equal(1, copy["a"]);
        Assert.Equal("two", copy["b"]);
        Assert.Equal(["a", "b"], copy.Select(static pair => pair.Key).Order());
        // A copy, not an alias: the matcher hands each request its own values.
        copy["a"] = 9;
        Assert.Equal(1, seeded["a"]);
    }

    [Fact]
    public void RemovalAndClearingBehave ()
    {
        var values = new RouteValueDictionary { ["a"] = 1, ["b"] = 2 };
        Assert.True(values.Remove("A"));
        Assert.False(values.Remove("A"));
        Assert.Single(values);
        values.Clear();
        Assert.Empty(values);
    }

    /// <summary>A null seed is an empty dictionary, which is how the matcher builds defaults.</summary>
    [Fact]
    public void NullSeedIsEmpty ()
    {
        Assert.Empty(new RouteValueDictionary((IEnumerable<KeyValuePair<string, object?>>?)null));
        Assert.Empty(new RouteValueDictionary((RouteValueDictionary?)null));
    }
}
