using System.Text;

namespace Cloudflare.Razor.Hosting;

/// <summary>Reads an <c>application/x-www-form-urlencoded</c> body via the package query parser.</summary>
public static class FormBody
{
    public static async Task<IQueryCollection> ReadAsync (HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        return QueryStringParser.Parse(await reader.ReadToEndAsync());
    }

    public static string Get (this IQueryCollection form, string key, string fallback = "") =>
        form.TryGetValue(key, out var value) && value.Count > 0 ? value[0] ?? fallback : fallback;
}
