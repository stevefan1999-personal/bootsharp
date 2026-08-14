namespace Cloudflare.Data.Notes;

/// <summary>One row of the D1 <c>notes</c> table.</summary>
/// <remarks>
/// The domain record and the wire shape coincide in this sample, so there is one record rather
/// than a repository model plus a near-identical view: a handler may return a POCO directly and
/// the Minimal API layer serializes it through the app's <see cref="ApiJsonContext"/>. Timestamps
/// stay strings because SQLite's <c>datetime('now')</c> is text and both providers read the column
/// back as it was stored — parsing it would be this sample inventing a conversion neither the
/// database nor the ORM performs.
/// </remarks>
public sealed record Note (int Id, string Body, string CreatedAt, string UpdatedAt);

/// <summary><c>POST</c> / <c>PUT /api/&lt;provider&gt;/notes</c> request body.</summary>
public sealed record NoteInput (string Body);

/// <summary>
/// What every note-storage implementation in this sample can do.
/// </summary>
/// <remarks>
/// <para>
/// Declared over the domain, not over a provider: nothing on it names D1, ADO.NET, FreeSql or SQL
/// at all, which is what lets the two implementations below be swapped by one registration line
/// and lets a CoreCLR unit test substitute a third against plain SQLite.
/// </para>
/// <para>
/// Everything is async because the D1 transport is: workerd has no thread that could block on a
/// promise, so <c>D1DbCommand</c>'s synchronous overloads throw by design.
/// </para>
/// </remarks>
public interface INoteRepository
{
    /// <summary>
    /// Identifies the DI scope this instance was resolved from.
    /// </summary>
    /// <remarks>
    /// A concession to <c>GET /api/diag/scope</c>, and the only member here that is not domain
    /// vocabulary. It earns its place: prose claiming "the endpoint and the repository share one
    /// scope" is not evidence, and comparing this against the scope probe the endpoint itself was
    /// handed is.
    /// </remarks>
    Guid ScopeId { get; }

    /// <summary>Newest first, id-tiebroken, windowed.</summary>
    Task<IReadOnlyList<Note>> List (int take, int skip);

    /// <summary>Notes whose body contains <paramref name="term"/>, newest first.</summary>
    Task<IReadOnlyList<Note>> Search (string term, int take);

    /// <summary>One note, or null when no row carries that id.</summary>
    Task<Note?> Find (int id);

    /// <summary>Inserts a note and returns it as stored, with the id the database assigned.</summary>
    Task<Note> Create (string body);

    /// <summary>Rewrites a note's body and bumps <c>updated_at</c>; null when the id is unknown.</summary>
    Task<Note?> Update (int id, string body);

    /// <summary>True when a row was deleted, false when the id was already absent.</summary>
    Task<bool> Delete (int id);
}

/// <summary>
/// <see cref="INoteRepository"/> over hand-written SQL and the ADO.NET provider.
/// </summary>
/// <remarks>
/// A distinct interface rather than a keyed registration so that a handler's parameter type still
/// says which implementation it wants while staying an abstraction. The two sub-interfaces exist
/// only because this sample deliberately hosts both implementations at once; an app that picks one
/// registers <see cref="INoteRepository"/> and never declares these.
/// </remarks>
public interface IAdoNoteRepository : INoteRepository;

/// <summary><see cref="INoteRepository"/> over the FreeSql ORM. See <see cref="IAdoNoteRepository"/>.</summary>
public interface IOrmNoteRepository : INoteRepository;
