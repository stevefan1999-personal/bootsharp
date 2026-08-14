using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.AspNetCore.Http.Json;

/// <summary>
/// Options for JSON body binding and JSON results.
/// </summary>
/// <remarks>
/// <para>
/// Reimplemented rather than vendored for one line. Upstream's default resolver is
/// <c>JsonSerializer.IsReflectionEnabledByDefault ? new DefaultJsonTypeInfoResolver(): JsonTypeInfoResolver.Combine()</c>,
/// and that feature switch defaults to <b>true</b> unless an MSBuild property sets it — neither
/// <c>PublishTrimmed</c> nor <c>PublishAot</c> sets it for you. A vendored copy
/// would therefore drag the reflective resolver into the graph of any app that forgot the property.
/// </para>
/// <para>
/// Here the resolver chain starts empty and is only ever added to by a generated
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>, so an unregistered type
/// fails with <see cref="MissingTypeInfo"/>'s message naming the type and the attribute to add,
/// rather than with a first-request 500 in production.
/// </para>
/// </remarks>
public sealed class JsonOptions
{
    /// <summary>Serializer options used for request bodies and JSON results.</summary>
    /// <remarks><see cref="JsonSerializerDefaults.Web"/> matches ASP.NET Core: camelCase names,
    /// case-insensitive reads, numbers readable from strings.</remarks>
    public JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(),
    };

    /// <summary>Adds a source-generated context to the resolver chain.</summary>
    /// <remarks>The only supported way to make a type serializable here: there is no reflective
    /// fallback to fall back to.</remarks>
    public JsonOptions AddContext (IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        SerializerOptions.TypeInfoResolverChain.Add(resolver);
        return this;
    }

    /// <summary>Resolves the metadata for <typeparamref name="T"/>, or explains what is missing.</summary>
    public JsonTypeInfo<T> GetTypeInfo<T> ()
    {
        if (SerializerOptions.TryGetTypeInfo(typeof(T), out var info) && info is JsonTypeInfo<T> typed)
            return typed;
        throw MissingTypeInfo(typeof(T));
    }

    [DoesNotReturn]
    internal static InvalidOperationException MissingTypeInfo (Type type) => throw new InvalidOperationException(
        $"No JsonTypeInfo is registered for '{type}'. Reflection-based serialization is off in " +
        "Bootsharp apps, so every serialized type needs source-generated metadata: add " +
        $"[JsonSerializable(typeof({type.Name}))] to a JsonSerializerContext and register it with " +
        "JsonOptions.AddContext.");
}
