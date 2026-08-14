using Bootsharp.Cloudflare.AspNetCore.Html;

namespace Microsoft.AspNetCore.Http.HttpResults;

/// <summary>A <c>text/html</c> response rendered by a template.</summary>
/// <remarks>
/// Takes the template rather than its output, which is what keeps the API streaming-shaped: the
/// writer handed to <see cref="HtmlBody"/> buffers today and will push chunks across the interop
/// boundary once milestone 0b lands, with no change to any template or to this type's
/// signature.
/// </remarks>
public sealed class HtmlHttpResult (HtmlBody body, int? statusCode) : IResult, IStatusCodeHttpResult
{
    /// <summary>The template this response renders.</summary>
    public HtmlBody Body { get; } = body;

    public int? StatusCode { get; } = statusCode;
    int IStatusCodeHttpResult.StatusCode => StatusCode ?? StatusCodes.Status200OK;

    public Task ExecuteAsync (HttpContext httpContext)
    {
        if (StatusCode.HasValue) httpContext.Response.StatusCode = StatusCode.Value;
        httpContext.Response.ContentType = "text/html; charset=utf-8";
        var writer = new StringHtmlWriter();
        Body(writer);
        return httpContext.Response.WriteAsync(writer.ToString());
    }
}
