using System.Diagnostics.CodeAnalysis;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.Mapping;

namespace Cloudflare.Backend.Data;

[Table("notes")]
public sealed class Note
{
    [PrimaryKey, Identity]
    [Column("id")]
    public int Id { get; set; }

    [Column("body"), LinqToDB.Mapping.NotNull]
    public string Body { get; set; } = "";

    [Column("created_at", SkipOnInsert = true, SkipOnUpdate = true)]
    public string CreatedAt { get; set; } = "";
}

internal static class LinqToDbNotes
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Note))]
    public static DataConnection Open(ID1Database db)
    {
        var connection = new D1DbConnection(db);
        connection.Open();
        return new DataConnection(
            new DataOptions()
                .UseSQLite(SQLiteProvider.Microsoft)
                .UseConnection(connection));
    }
}
