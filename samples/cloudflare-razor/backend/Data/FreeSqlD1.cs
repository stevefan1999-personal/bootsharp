using System.Diagnostics.CodeAnalysis;
using Cloudflare.Razor.Notes;
using FreeSql;
using FreeSql.Custom;

namespace Cloudflare.Razor.Data;

/// <summary>
/// Builds a scoped <see cref="IFreeSql"/> over <see cref="D1DbConnection"/>.
/// </summary>
/// <remarks>
/// The instance is scoped, not a singleton: <c>UseConnectionFactory</c> feeds an internal ADO
/// pool, so an instance held across events would hand out a connection whose D1 session was
/// released when the event that opened it ended.
/// </remarks>
internal static class FreeSqlD1
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FreeSqlNote))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CustomProvider<>))]
    public static IFreeSql Open (ID1Database db)
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionFactory(DataType.Custom, () =>
            {
                var connection = new D1DbConnection(db);
                connection.Open();
                return connection;
            }, typeof(CustomProvider<>))
            .UseAutoSyncStructure(false)
            .Build();
        fsql.SetCustomAdapter(new SqliteCustomAdapter());
        return fsql;
    }
}
