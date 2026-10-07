using Npgsql;

namespace Pangya.Data;

/// <summary>Ponto de acesso ao PostgreSQL: um NpgsqlDataSource (pool de conexões) por processo.</summary>
public sealed class Db(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; } = NpgsqlDataSource.Create(connectionString);

    public ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct = default) => Source.OpenConnectionAsync(ct);

    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
