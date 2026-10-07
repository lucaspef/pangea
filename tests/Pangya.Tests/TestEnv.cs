using Dapper;
using Pangya.Core.Config;
using Pangya.Data;

namespace Pangya.Tests;

/// <summary>Localiza a raiz do repositório e carrega config/test.json.</summary>
public static class TestEnv
{
    public static string Root { get; } = FindRoot();

    public static PangyaConfig Config
    {
        get
        {
            var cfg = PangyaConfig.Load(Path.Combine(Root, "config", "test.json"));
            cfg.Data.IffPath = Path.Combine(Root, cfg.Data.IffPath);
            return cfg;
        }
    }

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Pangya.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("Pangya.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Banco de teste limpo: apaga o schema public do pangya_test e aplica todas as migrações,
/// uma vez por execução. Testes que usam o banco ficam na coleção "db" (rodam em série).
/// </summary>
public sealed class DbFixture : IAsyncLifetime
{
    public Db Db { get; } = new(TestEnv.Config.Database.ConnectionString);

    public async Task InitializeAsync()
    {
        await using (var c = await Db.OpenAsync())
            await c.ExecuteAsync("drop schema public cascade; create schema public;");
        await Migrator.RunAsync(Db);
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
}

[CollectionDefinition("db")]
public sealed class DbCollection : ICollectionFixture<DbFixture>;
