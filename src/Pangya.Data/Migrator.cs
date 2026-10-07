using System.Reflection;
using Dapper;

namespace Pangya.Data;

/// <summary>
/// Aplica as migrações SQL numeradas (Migrations/NNN_nome.sql, embutidas na dll) em ordem,
/// cada uma numa transação, registrando a versão em schema_migrations.
/// </summary>
public static class Migrator
{
    public static IReadOnlyList<(int Version, string Name)> All { get; } = Load();

    static List<(int, string)> Load()
    {
        var list = new List<(int, string)>();
        foreach (var res in typeof(Migrator).Assembly.GetManifestResourceNames())
        {
            if (!res.EndsWith(".sql")) continue;
            var name = res.Split('.')[^2];                      // Migrations.001_base.sql -> 001_base
            list.Add((int.Parse(name[..name.IndexOf('_')]), name));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    static string Sql(string name)
    {
        using var s = typeof(Migrator).Assembly.GetManifestResourceStream($"Migrations.{name}.sql")!;
        return new StreamReader(s).ReadToEnd();
    }

    /// <summary>Aplica o que falta; devolve as versões aplicadas agora.</summary>
    public static async Task<List<int>> RunAsync(Db db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync("""
            create table if not exists schema_migrations(
                version int primary key,
                name text not null,
                applied_at timestamptz not null default now())
            """);
        // trava global: dois processos subindo juntos não aplicam a mesma migração
        await conn.ExecuteAsync("select pg_advisory_lock(7212001)");
        try
        {
            var done = new HashSet<int>(await conn.QueryAsync<int>("select version from schema_migrations"));
            var applied = new List<int>();
            foreach (var (version, name) in All)
            {
                if (done.Contains(version)) continue;
                await using var tx = await conn.BeginTransactionAsync(ct);
                await conn.ExecuteAsync(Sql(name), transaction: tx);
                await conn.ExecuteAsync("insert into schema_migrations(version, name) values (@version, @name)", new { version, name }, tx);
                await tx.CommitAsync(ct);
                applied.Add(version);
            }
            return applied;
        }
        finally
        {
            await conn.ExecuteAsync("select pg_advisory_unlock(7212001)");
        }
    }
}
