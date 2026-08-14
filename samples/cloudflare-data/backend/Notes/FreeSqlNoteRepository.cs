using System.Globalization;
using Cloudflare.Data.Diagnostics;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace Cloudflare.Data.Notes;

/// <summary>The <c>notes</c> table as FreeSql maps it.</summary>
/// <remarks>
/// A mutable class with a parameterless constructor, because that is what an ORM materializes into
/// — which is exactly why it is a separate type from the <see cref="Note"/> record the rest of the
/// app passes around. <c>created_at</c> is never written by C#: the column's SQL default is the
/// only writer, so the database decides what "now" means for both providers alike.
/// </remarks>
[Table(Name = "notes")]
public sealed class FreeSqlNote
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "body", IsNullable = false)]
    public string Body { get; set; } = "";

    [Column(Name = "created_at", CanInsert = false, CanUpdate = false)]
    public string CreatedAt { get; set; } = "";

    [Column(Name = "updated_at", CanInsert = false)]
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// The same <see cref="INoteRepository"/> contract, served by the FreeSql ORM.
/// </summary>
/// <remarks>
/// <para>
/// Reading this beside <see cref="AdoNoteRepository"/> is the point of the sample: the two answer
/// identically-shaped requests, one through SQL it wrote and one through expression trees the ORM
/// translated, and the endpoints below them cannot tell which is which because both arrive as
/// <see cref="INoteRepository"/>.
/// </para>
/// <para>
/// <see cref="IFreeSql"/> is injected, never constructed here. A repository that built its own
/// would own a disposable whose lifetime it cannot see, and would silently make itself
/// unregisterable as anything but a transient.
/// </para>
/// </remarks>
internal sealed class FreeSqlNoteRepository (
    IFreeSql fsql,
    IScopeProbe scope,
    ILogger<FreeSqlNoteRepository> logger) : IOrmNoteRepository
{
    public Guid ScopeId => scope.ScopeId;

    public async Task<IReadOnlyList<Note>> List (int take, int skip)
    {
        var rows = await Ordered().Skip(skip).Take(take).ToListAsync();
        logger.LogInformation("orm listed {Count} notes (take {Take}, skip {Skip})", rows.Count, take, skip);
        return Project(rows);
    }

    public async Task<IReadOnlyList<Note>> Search (string term, int take)
    {
        // Contains() becomes LIKE '%term%' in the emitted SQL, so unlike the ADO repository's
        // instr() this path does treat % and _ in the term as wildcards. The two providers really
        // do differ here, and pretending otherwise would be the sample lying about an ORM.
        var rows = await Ordered().Where(note => note.Body.Contains(term)).Take(take).ToListAsync();
        logger.LogInformation("orm search for {Term} matched {Count} notes", term, rows.Count);
        return Project(rows);
    }

    public async Task<Note?> Find (int id) =>
        Project(await fsql.Select<FreeSqlNote>().Where(note => note.Id == id).FirstAsync());

    public async Task<Note> Create (string body)
    {
        var id = (int)await fsql.Insert(new FreeSqlNote { Body = body }).ExecuteIdentityAsync();
        logger.LogInformation("orm created note {NoteId}", id);
        return await Find(id) ?? throw new InvalidOperationException(
            $"Note {id} was inserted but could not be read back.");
    }

    public async Task<Note?> Update (int id, string body)
    {
        // updated_at is set from C# rather than from datetime('now') because the ORM's UPDATE is
        // built from column assignments, not from raw SQL fragments; the format is SQLite's own so
        // the two providers store the same text.
        var affected = await fsql.Update<FreeSqlNote>(id)
            .Set(note => note.Body, body)
            .Set(note => note.UpdatedAt, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .ExecuteAffrowsAsync();
        logger.LogInformation("orm update of note {NoteId} affected {Affected} rows", id, affected);
        return affected == 0 ? null : await Find(id);
    }

    public async Task<bool> Delete (int id)
    {
        var affected = await fsql.Delete<FreeSqlNote>(id).ExecuteAffrowsAsync();
        logger.LogInformation("orm delete of note {NoteId} affected {Affected} rows", id, affected);
        return affected > 0;
    }

    private ISelect<FreeSqlNote> Ordered () => fsql.Select<FreeSqlNote>()
        .OrderByDescending(note => note.CreatedAt)
        .OrderByDescending(note => note.Id);

    private static IReadOnlyList<Note> Project (List<FreeSqlNote> rows) =>
        [.. rows.Select(row => new Note(row.Id, row.Body, row.CreatedAt, row.UpdatedAt))];

    private static Note? Project (FreeSqlNote? row) =>
        row is null ? null : new Note(row.Id, row.Body, row.CreatedAt, row.UpdatedAt);
}
