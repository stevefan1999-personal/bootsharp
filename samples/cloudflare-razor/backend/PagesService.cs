using Cloudflare.Razor.Hosting;
using Cloudflare.Razor.Notes;
using Cloudflare.Razor.Pages;
using Microsoft.Extensions.Logging;
using HomePage = Cloudflare.Razor.Pages.Home;

namespace Cloudflare.Razor;

/// <summary>
/// Page and JSON handlers. Scoped so the <see cref="INoteRepository"/> it captures is the one
/// built for this event, not a leftover from the previous request.
/// </summary>
public sealed class PagesService (INoteRepository notes, ILogger<PagesService> logger)
{
    public async Task<IResult> Home (string? name, string? probe, string? flash)
    {
        var list = await notes.List(20, 0);
        logger.LogInformation("home rendered for {Name} with {Count} notes", name ?? "(anonymous)", list.Count);
        return TypedResults.Html(html => HomePage.Render(html, new HomeModel {
            Runtime = ".NET " + System.Environment.Version,
            Environment = WorkerContext.Env.ENVIRONMENT,
            Name = name,
            Probe = probe,
            Flash = flash,
            Notes = list
        }));
    }

    public async Task<IResult> Create (HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        var body = form.Get("body").Trim();
        if (body.Length == 0)
            return TypedResults.RedirectSeeOther("/?flash=empty");
        await notes.Create(body);
        return TypedResults.RedirectSeeOther("/?flash=saved");
    }

    public async Task<IResult> Delete (int id)
    {
        await notes.Delete(id);
        return TypedResults.RedirectSeeOther("/?flash=deleted");
    }

    public async Task<IResult> Health ()
    {
        var list = await notes.List(1, 0);
        return TypedResults.Ok(new Health(
            Ok: true,
            Runtime: ".NET " + System.Environment.Version,
            Environment: WorkerContext.Env.ENVIRONMENT,
            Page: "Cloudflare.Razor.Pages.Home",
            Notes: list.Count));
    }

    public Task<IReadOnlyList<Note>> ListNotes () => notes.List(50, 0);

    public async Task<IResult> PostNote (NoteInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Body))
            return TypedResults.BadRequest();
        var created = await notes.Create(input.Body.Trim());
        return TypedResults.Created($"/api/notes/{created.Id}", created);
    }
}
