using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace Cloudflare.Razor.Notes;

/// <summary>The <c>notes</c> table as FreeSql maps it.</summary>
[Table(Name = "notes")]
public sealed class FreeSqlNote
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "body", IsNullable = false)]
    public string Body { get; set; } = "";

    [Column(Name = "created_at", CanInsert = false, CanUpdate = false)]
    public string CreatedAt { get; set; } = "";

    [Column(Name = "updated_at", CanInsert = false, CanUpdate = false)]
    public string UpdatedAt { get; set; } = "";
}

/// <summary><see cref="INoteRepository"/> over FreeSql, which talks to D1 through the scoped
/// <see cref="IFreeSql"/> the container built for this event.</summary>
internal sealed class FreeSqlNoteRepository (IFreeSql fsql, ILogger<FreeSqlNoteRepository> logger)
    : INoteRepository
{
    public async Task<IReadOnlyList<Note>> List (int take, int skip)
    {
        var rows = await fsql.Select<FreeSqlNote>()
            .OrderByDescending(note => note.CreatedAt)
            .OrderByDescending(note => note.Id)
            .Skip(skip).Take(take).ToListAsync();
        logger.LogInformation("listed {Count} notes", rows.Count);
        return [.. rows.Select(Project)];
    }

    public async Task<Note> Create (string body)
    {
        var id = (int)await fsql.Insert(new FreeSqlNote { Body = body }).ExecuteIdentityAsync();
        logger.LogInformation("created note {NoteId}", id);
        var row = await fsql.Select<FreeSqlNote>().Where(note => note.Id == id).FirstAsync()
            ?? throw new InvalidOperationException($"Note {id} was inserted but could not be read back.");
        return Project(row);
    }

    public async Task<bool> Delete (int id)
    {
        var affected = await fsql.Delete<FreeSqlNote>(id).ExecuteAffrowsAsync();
        logger.LogInformation("delete of note {NoteId} affected {Affected} rows", id, affected);
        return affected > 0;
    }

    private static Note Project (FreeSqlNote row) => new(row.Id, row.Body, row.CreatedAt, row.UpdatedAt);
}
