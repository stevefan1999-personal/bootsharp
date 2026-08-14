using System.Diagnostics.CodeAnalysis;
using Cloudflare.Data.Notes;
using FreeSql;
using FreeSql.Custom;

namespace Cloudflare.Data.Ado;

/// <summary>
/// Builds an <see cref="IFreeSql"/> whose ADO provider is <see cref="D1DbConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>FreeSql.Provider.Custom</c> is the provider that takes a connection factory instead of a
/// driver, which is the only shape that can work here: there is no SQLite native library to P/Invoke
/// inside workerd, and D1 is reached over an interop handle. <see cref="SqliteCustomAdapter"/> then
/// retargets the dialect, because the Custom provider's own defaults are SQL Server.
/// </para>
/// <para>
/// The <c>UseConnectionFactory</c> callback is what makes the returned instance <em>scoped</em>
/// rather than a singleton: FreeSql feeds this factory into an internal ADO pool, so an instance
/// held across events would hand out a connection whose D1 session handle was released when the
/// event that opened it ended. See the README's lifetime table.
/// </para>
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
            // The schema is migrations/0001_init.sql, applied by wrangler. Letting the ORM emit
            // DDL on first use would make two sources of truth for one table.
            .UseAutoSyncStructure(false)
            .Build();
        fsql.SetCustomAdapter(new SqliteCustomAdapter());
        return fsql;
    }
}
