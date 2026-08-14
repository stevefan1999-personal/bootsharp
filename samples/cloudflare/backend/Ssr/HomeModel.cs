namespace Cloudflare.Backend.Ssr;

/// <summary>
/// Everything the home page renders from, loaded once per request by
/// <see cref="SiteService"/> and handed to whichever tier renders it.
/// </summary>
/// <remarks>Plain C# with no attribute and no base type: what a <c>.cshtml</c> page binds through
/// <c>@model</c> is an ordinary class, so the model outlived the page's conversion from
/// <c>[HtmlTemplate]</c> to <c>HomePage.cshtml</c> without changing a line.</remarks>
public sealed class HomeModel
{
    public string Runtime { get; init; } = "";
    public string Host { get; init; } = "";
    public string WorkersTypes { get; init; } = "";
    public string? KvValue { get; init; }
    public string D1Rows { get; init; } = "[]";
    public string R2Objects { get; init; } = "[]";
    public int Counter { get; init; }
    public string? Flash { get; init; }
    public string? Error { get; init; }
}
