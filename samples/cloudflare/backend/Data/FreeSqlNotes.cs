using System.Diagnostics.CodeAnalysis;
using FreeSql;
using FreeSql.Custom;
using FreeSql.DataAnnotations;

namespace Cloudflare.Backend.Data;

[Table(Name = "notes")]
public sealed class FreeSqlNote
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "body", IsNullable = false)]
    public string Body { get; set; } = "";

    [Column(Name = "created_at", CanInsert = false, CanUpdate = false)]
    public string CreatedAt { get; set; } = "";
}

internal static class FreeSqlNotes
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FreeSqlNote))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CustomProvider<>))]
    public static IFreeSql Open(ID1Database db)
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionFactory(
                DataType.Custom,
                () =>
                {
                    var connection = new D1DbConnection(db);
                    connection.Open();
                    return connection;
                },
                typeof(CustomProvider<>))
            .UseAutoSyncStructure(false)
            .Build();
        fsql.SetCustomAdapter(new SqliteCustomAdapter());
        return fsql;
    }
}
