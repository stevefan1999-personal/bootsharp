using System.Data.Common;
using Cloudflare.Data.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Cloudflare.Data.Notes;

/// <summary>
/// Notes over hand-written SQL and <see cref="DbConnection"/> — no ORM, no micro-ORM.
/// </summary>
/// <remarks>
/// <para>
/// The constructor takes <see cref="DbConnection"/>, the BCL abstraction, and not
/// <c>D1DbConnection</c>: the same class runs against the Durable Object SQLite facade, against
/// <c>Microsoft.Data.Sqlite</c> in a CoreCLR unit test, and against D1 in the worker, because
/// nothing below is D1-specific except the dialect — which is SQLite either way.
/// </para>
/// <para>
/// The connection arrives already open. Opening it is the registration's job (see
/// <see cref="DataServiceCollectionExtensions"/>) because <c>D1DbConnection.Open</c> takes a
/// <c>withSession</c> handle whose lifetime is the invocation, so *when* it is opened decides
/// whether this repository can be a singleton — and it cannot.
/// </para>
/// <para>
/// <see cref="ILogger{T}"/> is a constructor dependency, which is also the answer to the one DI
/// shape the compile-time endpoint binder cannot see: <c>ILogger&lt;T&gt;</c> as a *handler lambda*
/// parameter is invisible to it, because the canonical registration is the unbound generic
/// <c>typeof(ILogger&lt;&gt;)</c> and the scan compares closed type names. Loggers belong down
/// here anyway — the endpoint is not where the interesting events happen.
/// </para>
/// </remarks>
internal sealed class AdoNoteRepository (
    DbConnection connection,
    IScopeProbe scope,
    ILogger<AdoNoteRepository> logger) : IAdoNoteRepository
{
    private const string Columns = "id, body, created_at, updated_at";

    public Guid ScopeId => scope.ScopeId;

    public async Task<IReadOnlyList<Note>> List (int take, int skip)
    {
        // created_at has one-second resolution, so id breaks ties: without it two notes written in
        // the same second have no defined order and the window can repeat or skip a row.
        var notes = await Query(
            $"SELECT {Columns} FROM notes ORDER BY created_at DESC, id DESC LIMIT ? OFFSET ?",
            take, skip);
        logger.LogInformation("listed {Count} notes (take {Take}, skip {Skip})", notes.Count, take, skip);
        return notes;
    }

    public async Task<IReadOnlyList<Note>> Search (string term, int take)
    {
        // instr() rather than LIKE: LIKE would make % and _ in the caller's term into wildcards,
        // which is a search that silently means something else than what was typed.
        var notes = await Query(
            $"SELECT {Columns} FROM notes WHERE instr(lower(body), lower(?)) > 0 " +
            "ORDER BY created_at DESC, id DESC LIMIT ?", term, take);
        logger.LogInformation("search for {Term} matched {Count} notes", term, notes.Count);
        return notes;
    }

    public async Task<Note?> Find (int id) =>
        (await Query($"SELECT {Columns} FROM notes WHERE id = ?", id)).FirstOrDefault();

    public async Task<Note> Create (string body)
    {
        // RETURNING makes the insert and the read-back one statement, so the row cannot be the
        // wrong one under a concurrent insert and there is no second round trip to workerd.
        var created = (await Query(
            $"INSERT INTO notes (body) VALUES (?) RETURNING {Columns}", body)).Single();
        logger.LogInformation("created note {NoteId}", created.Id);
        return created;
    }

    public async Task<Note?> Update (int id, string body)
    {
        var updated = (await Query(
            $"UPDATE notes SET body = ?, updated_at = datetime('now') WHERE id = ? RETURNING {Columns}",
            body, id)).FirstOrDefault();
        logger.LogInformation("update of note {NoteId} matched {Matched}", id, updated is not null);
        return updated;
    }

    public async Task<bool> Delete (int id)
    {
        await using var command = Command("DELETE FROM notes WHERE id = ?", id);
        var deleted = await command.ExecuteNonQueryAsync() > 0;
        logger.LogInformation("delete of note {NoteId} affected a row: {Deleted}", id, deleted);
        return deleted;
    }

    private async Task<IReadOnlyList<Note>> Query (string sql, params object?[] values)
    {
        await using var command = Command(sql, values);
        await using var reader = await command.ExecuteReaderAsync();
        var notes = new List<Note>();
        while (await reader.ReadAsync())
            notes.Add(new Note(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return notes;
    }

    private DbCommand Command (string sql, params object?[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var value in values)
        {
            var parameter = command.CreateParameter();
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
