namespace Cloudflare.Backend;

/// <summary>
/// User Durable Object. wrangler <c>class_name</c> is this type name.
/// No JavaScript class to write — publish emits <c>export class Counter extends DurableObject</c>.
/// </summary>
public sealed class Counter : DurableObject
{
    public Counter(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

    public async Task<int> Get()
    {
        var raw = await Ctx.Storage.Get("n");
        return int.TryParse(raw, out var n) ? n : 0;
    }

    public async Task<int> Increment()
    {
        var next = await Get() + 1;
        await Ctx.Storage.Put("n", next.ToString());
        return next;
    }

    public async Task<string> SqlDemo()
    {
        await Task.Yield();
        var sql = Ctx.Storage.Sql;
        sql.ExecJson("CREATE TABLE IF NOT EXISTS ticks (id INTEGER PRIMARY KEY AUTOINCREMENT, n INTEGER NOT NULL)", "[]");
        sql.ExecJson("INSERT INTO ticks (n) VALUES (?)", "[1]");
        return sql.ExecJson("SELECT COUNT(*) AS c FROM ticks", "[]").RowsJson;
    }
}
