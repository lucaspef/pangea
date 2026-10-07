using Dapper;
using Pangya.Core.Config;
using Pangya.Data;

namespace Pangya.Tests;

public class ConfigTests
{
    [Fact]
    public void ExampleConfigsAreValid()
    {
        foreach (var f in new[] { "pangya.example.json", "test.example.json" })
            Assert.NotNull(PangyaConfig.Load(Path.Combine(TestEnv.Root, "config", f)));
    }

    [Fact]
    public void UnknownFieldIsRejected() =>
        Assert.ThrowsAny<Exception>(() => PangyaConfig.Parse("""{"Database":{"ConnectionString":"x"},"Netwrk":{}}"""));

    [Fact]
    public void MissingConnectionStringIsRejected() =>
        Assert.Throws<InvalidDataException>(() => PangyaConfig.Parse("{}"));

    [Fact]
    public void InvalidLimitsAreRejected() =>
        Assert.Throws<InvalidDataException>(() => PangyaConfig.Parse("""{"Database":{"ConnectionString":"x"},"Limits":{"MaxPacketsPerSecond":0}}"""));
}

[Collection("db")]
public class MigratorTests(DbFixture fx)
{
    [Fact]
    public async Task AllMigrationsAppliedAndIdempotent()
    {
        Assert.Empty(await Migrator.RunAsync(fx.Db));
        await using var c = await fx.Db.OpenAsync();
        var versions = (await c.QueryAsync<int>("select version from schema_migrations order by version")).ToList();
        Assert.Equal(Migrator.All.Select(m => m.Version), versions);
    }

    [Fact]
    public async Task ObjectIdsAreUniqueFromDatabase()
    {
        await using var c = await fx.Db.OpenAsync();
        var a = await c.ExecuteScalarAsync<int>("select nextval('object_id_seq')");
        var b = await c.ExecuteScalarAsync<int>("select nextval('object_id_seq')");
        Assert.True(a >= 1000000 && b > a);
    }
}
