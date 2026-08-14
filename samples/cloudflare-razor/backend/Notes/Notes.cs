namespace Cloudflare.Razor.Notes;

/// <summary>One row of the D1 <c>notes</c> table.</summary>
public sealed record Note (int Id, string Body, string CreatedAt, string UpdatedAt);

/// <summary><c>POST /api/notes</c> JSON body.</summary>
public sealed record NoteInput (string Body);

/// <summary>
/// Note storage. Declared over the domain: nothing on it names D1, ADO.NET or FreeSql.
/// </summary>
public interface INoteRepository
{
    Task<IReadOnlyList<Note>> List (int take, int skip);
    Task<Note> Create (string body);
    Task<bool> Delete (int id);
}
