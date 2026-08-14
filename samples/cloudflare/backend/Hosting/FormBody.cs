using System.Text;

namespace Cloudflare.Backend.Hosting;

/// <summary>
/// Reads an <c>application/x-www-form-urlencoded</c> request body.
/// </summary>
/// <remarks>
/// <c>HttpRequest.Form</c> and <c>[FromForm]</c> both refuse in this package: the urlencoded and
/// multipart readers live in <c>Microsoft.AspNetCore.WebUtilities</c>, which the verdict table put
/// in a separate opt-in layer so an app that accepts no uploads does not carry one. The SSR page in
/// this sample posts real HTML forms, so it does the one thing that layer would do for it — a
/// urlencoded body is a query string, and the package's own query parser is public for exactly this.
/// </remarks>
public static class FormBody
{
    public static async Task<IQueryCollection> ReadAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        return QueryStringParser.Parse(await reader.ReadToEndAsync());
    }

    /// <summary>The first value for <paramref name="key"/>, or <paramref name="fallback"/>.</summary>
    public static string Get(this IQueryCollection form, string key, string fallback = "") =>
        form.TryGetValue(key, out var value) && value.Count > 0 ? value[0] ?? fallback : fallback;
}

/// <summary>Post/Redirect/Get: every form handler answers with one of these.</summary>
/// <remarks><c>TypedResults.RedirectSeeOther</c> is this package's one deliberate addition to
/// upstream's result surface — 303 is the status that tells the browser to follow with GET, so a
/// reload does not repost, and upstream's permanent/preserve-method matrix cannot produce it.</remarks>
public static class Flash
{
    /// <summary>Redirects home with a flash message in the query string.</summary>
    public static IResult Home(string flash) =>
        TypedResults.RedirectSeeOther("/?flash=" + Uri.EscapeDataString(flash));
}
