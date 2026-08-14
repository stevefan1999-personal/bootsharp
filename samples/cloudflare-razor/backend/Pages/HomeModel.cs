using Cloudflare.Razor.Notes;

namespace Cloudflare.Razor.Pages;

/// <summary>Everything <c>Home.cshtml</c> renders from. An ordinary class — <c>@model</c> is
/// syntax sugar for a <c>Render</c> parameter named <c>Model</c>, not MVC ViewData.</summary>
public sealed class HomeModel
{
    public string Runtime { get; init; } = "";
    public string Environment { get; init; } = "";
    public string? Name { get; init; }
    public string? Probe { get; init; }
    public string? Flash { get; init; }
    public IReadOnlyList<Note> Notes { get; init; } = [];
}
