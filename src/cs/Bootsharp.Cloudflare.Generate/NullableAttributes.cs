namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Nullability attribute netstandard2.0 does not carry.
/// </summary>
/// <remarks>
/// A source generator must target netstandard2.0 to load into every host, and that reference
/// assembly predates the nullable-analysis attributes. Roslyn ships its own internal copies, which
/// is why the vendored RDG utilities compile inside aspnetcore but not here — they annotate their
/// <c>out</c> parameters with it. Declaring it in source is the same polyfill upstream applies
/// (<c>src/Shared/Nullable/NullableAttributes.cs</c>), reduced to the one attribute that is used.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NotNullWhenAttribute (bool returnValue) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
}
