namespace Pangya.Domain.Admin;

/// <summary>Uma ação registrada (GM no jogo, painel web ou linha de comando).</summary>
public sealed record AuditEntry(long Id, DateTime At, long? ActorId, string Actor, string Action, string Target, string Details);

public interface IAuditStore
{
    Task LogAsync(long? actorId, string actor, string action, string target, string details);
    /// <summary>As mais novas; filter procura em quem fez, ação ou alvo.</summary>
    Task<List<AuditEntry>> RecentAsync(int max, string? filter = null);
}

/// <summary>Auditoria que nunca derruba a ação: falha ao gravar só vira log.</summary>
public sealed class AuditLog(IAuditStore store)
{
    public IAuditStore Store => store;

    public async Task WriteAsync(long? actorId, string actor, string action, string target = "", string details = "")
    {
        try { await store.LogAsync(actorId, Trim(actor, 64), Trim(action, 64), Trim(target, 128), Trim(details, 1000)); }
        catch (Exception e) { Core.Logging.Log.Error($"auditoria: falha ao gravar {action} de {actor}", e); }
    }

    static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
