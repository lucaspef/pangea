using Dapper;
using Pangya.Domain.Admin;

namespace Pangya.Data;

/// <summary>Auditoria (migração 010).</summary>
public sealed class AuditRepository(Db db) : IAuditStore
{
    sealed record Row(long Id, DateTime At, long? ActorId, string Actor, string Action, string Target, string Details);

    public async Task LogAsync(long? actorId, string actor, string action, string target, string details)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("insert into audit_log(actor_id, actor, action, target, details) values (@actorId, @actor, @action, @target, @details)",
            new { actorId, actor, action, target, details });
    }

    public async Task<List<AuditEntry>> RecentAsync(int max, string? filter = null)
    {
        await using var c = await db.OpenAsync();
        string where = string.IsNullOrWhiteSpace(filter) ? "" : "where actor ilike @like or action ilike @like or target ilike @like";
        var rows = await c.QueryAsync<Row>($"select id, at, actor_id, actor, action, target, details from audit_log {where} order by id desc limit @max",
            new { max, like = "%" + (filter ?? "").Replace("%", "").Replace("_", "") + "%" });
        var list = new List<AuditEntry>();
        foreach (var r in rows) list.Add(new AuditEntry(r.Id, r.At, r.ActorId, r.Actor, r.Action, r.Target, r.Details));
        return list;
    }
}
