using Cloudflare.Data.Diagnostics;
using Cloudflare.Data.Notes;
using Microsoft.AspNetCore.Mvc;

namespace Cloudflare.Data;

/// <summary>
/// This worker's endpoint table.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Map*</c> call below is replaced by a generated interceptor: the pattern is parsed, the
/// route/query/body bindings are emitted and the precedence is computed while the app compiles
///. Two consequences show up in the shape of this file. A handler parameter is a
/// service because a registration in this compilation says so — see
/// <see cref="DataServiceCollectionExtensions"/> — and every pattern is a string literal, because a
/// pattern the compiler cannot read is a pattern the generator cannot parse.
/// </para>
/// <para>
/// The two note families are the point of the sample. <c>/api/ado/notes*</c> and
/// <c>/api/orm/notes*</c> are twelve endpoints over two implementations, and every one of them
/// delegates to a handler body written against <see cref="INoteRepository"/> — so the only
/// difference between an endpoint served by hand-written SQL and one served by an ORM is which
/// interface its lambda asks for.
/// </para>
/// </remarks>
public static class Routes
{
    /// <summary>Largest window a list or search endpoint will serve.</summary>
    private const int MaxTake = 100;

    /// <summary>Longest note body accepted.</summary>
    private const int MaxBodyLength = 4000;

    public static void MapNotesApi (this WebApplication app)
    {
        app.MapGet("/api/health", (IIsolateProbe isolate, ICloudflareEnv env) =>
            new HealthView(true, $".NET {Environment.Version}", env.ENVIRONMENT, isolate.IsolateId));

        // The DI proof. Both probes are resolved for this one event; the repository is in the
        // response because a shared scope between the endpoint and the layer beneath it is the
        // property that actually matters, and only the repository can report its own.
        app.MapGet("/api/diag/scope", (IScopeProbe scope, IAdoNoteRepository notes, IIsolateProbe isolate) =>
            new ScopeReport(scope.ScopeId, notes.ScopeId, scope.ScopeId == notes.ScopeId,
                scope.Sequence, isolate.IsolateId, isolate.ScopesDisposed));

        // Hand-written SQL over System.Data.Common.
        app.MapGet("/api/ado/notes", (IAdoNoteRepository notes,
            [FromQuery] int take = 20, [FromQuery] int skip = 0) => List(notes, take, skip));
        app.MapGet("/api/ado/notes/search", (IAdoNoteRepository notes,
            [FromQuery] string q, [FromQuery] int take = 20) => Search(notes, q, take));
        app.MapGet("/api/ado/notes/{id:int}", (IAdoNoteRepository notes, int id) => Find(notes, id));
        app.MapPost("/api/ado/notes", (IAdoNoteRepository notes, NoteInput input) => Create(notes, input, "ado"));
        app.MapPut("/api/ado/notes/{id:int}", (IAdoNoteRepository notes, int id, NoteInput input) =>
            Update(notes, id, input));
        app.MapDelete("/api/ado/notes/{id:int}", (IAdoNoteRepository notes, int id) => Delete(notes, id));

        // The same six endpoints through FreeSql. Note the literal "/search" beats "{id:int}" by
        // precedence, so it is reachable without being registered first.
        app.MapGet("/api/orm/notes", (IOrmNoteRepository notes,
            [FromQuery] int take = 20, [FromQuery] int skip = 0) => List(notes, take, skip));
        app.MapGet("/api/orm/notes/search", (IOrmNoteRepository notes,
            [FromQuery] string q, [FromQuery] int take = 20) => Search(notes, q, take));
        app.MapGet("/api/orm/notes/{id:int}", (IOrmNoteRepository notes, int id) => Find(notes, id));
        app.MapPost("/api/orm/notes", (IOrmNoteRepository notes, NoteInput input) => Create(notes, input, "orm"));
        app.MapPut("/api/orm/notes/{id:int}", (IOrmNoteRepository notes, int id, NoteInput input) =>
            Update(notes, id, input));
        app.MapDelete("/api/orm/notes/{id:int}", (IOrmNoteRepository notes, int id) => Delete(notes, id));
    }

    // Below this line nothing knows which implementation it is talking to, which is the layering
    // the sample exists to show: one contract, two providers, one set of handler bodies.

    /// <remarks>Returns the list itself rather than an <c>IResult</c>: a handler may return a POCO
    /// and the layer serializes it through <see cref="ApiJsonContext"/>. <c>take</c> is clamped
    /// rather than rejected — an out-of-range window is not a client error, it is a window.</remarks>
    private static Task<IReadOnlyList<Note>> List (INoteRepository notes, int take, int skip) =>
        notes.List(Math.Clamp(take, 1, MaxTake), Math.Max(skip, 0));

    /// <remarks><c>q</c> carries no default, so a request without it is a 400 the generated binder
    /// produces before this method is entered — there is no null check here because there is no way
    /// to reach it with a missing <c>q</c>.</remarks>
    private static Task<IReadOnlyList<Note>> Search (INoteRepository notes, string q, int take) =>
        notes.Search(q, Math.Clamp(take, 1, MaxTake));

    private static async Task<IResult> Find (INoteRepository notes, int id) =>
        await notes.Find(id) is { } note ? TypedResults.Ok(note) : NotFound(id);

    private static async Task<IResult> Create (INoteRepository notes, NoteInput input, string provider)
    {
        if (Invalid(input) is { } problem) return problem;
        var created = await notes.Create(input.Body);
        return TypedResults.Created($"/api/{provider}/notes/{created.Id}", created);
    }

    private static async Task<IResult> Update (INoteRepository notes, int id, NoteInput input)
    {
        if (Invalid(input) is { } problem) return problem;
        // A successful PUT moves updated_at, so the effect of this call is visible in the response
        // body rather than only in a status code — which is what makes it assertable at all.
        return await notes.Update(id, input.Body) is { } note ? TypedResults.Ok(note) : NotFound(id);
    }

    private static async Task<IResult> Delete (INoteRepository notes, int id) =>
        await notes.Delete(id) ? TypedResults.NoContent() : NotFound(id);

    private static IResult? Invalid (NoteInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Body))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> {
                ["body"] = ["A note body is required."]
            });
        if (input.Body.Length > MaxBodyLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> {
                ["body"] = [$"A note body is at most {MaxBodyLength} characters."]
            });
        return null;
    }

    private static IResult NotFound (int id) =>
        TypedResults.Problem(detail: $"No note carries id {id}.", statusCode: StatusCodes.Status404NotFound,
            title: "Note not found");
}
