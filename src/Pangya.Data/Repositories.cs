using System.Net;
using Dapper;
using Npgsql;
using Pangya.Domain.Accounts;
using Pangya.Domain.Auth;
using Pangya.Domain.Servers;

namespace Pangya.Data;

public sealed class AccountRepository(Db db) : IAccountStore
{
    const string Columns = "id, login, password_hash, nickname, identity_flags, blocked_until, block_reason";

    public async Task<Account?> FindByLoginAsync(string login)
    {
        await using var c = await db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<Account>($"select {Columns} from accounts where lower(login) = lower(@login)", new { login });
    }

    public async Task<Account?> FindByIdAsync(long id)
    {
        await using var c = await db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<Account>($"select {Columns} from accounts where id = @id", new { id });
    }

    public async Task<Account?> CreateAsync(string login, string passwordHash)
    {
        await using var c = await db.OpenAsync();
        try
        {
            return await c.QuerySingleAsync<Account>(
                $"insert into accounts(login, password_hash) values (@login, @passwordHash) returning {Columns}", new { login, passwordHash });
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { return null; }
    }

    public async Task<bool> SetNicknameAsync(long id, string nickname)
    {
        await using var c = await db.OpenAsync();
        try { return await c.ExecuteAsync("update accounts set nickname = @nickname where id = @id and nickname is null", new { id, nickname }) == 1; }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { return false; }
    }

    public async Task<bool> NicknameExistsAsync(string nickname)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from accounts where lower(nickname) = lower(@nickname))", new { nickname });
    }

    public async Task UpdatePasswordHashAsync(long id, string passwordHash)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update accounts set password_hash = @passwordHash where id = @id", new { id, passwordHash });
    }

    public async Task SetIdentityFlagsAsync(long id, int flags)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update accounts set identity_flags = @flags where id = @id", new { id, flags });
    }

    public async Task RecordLoginAsync(long id, string ip)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update accounts set last_login_at = now(), last_login_ip = @addr::inet where id = @id",
            new { id, addr = IPAddress.TryParse(ip, out var a) ? a.ToString() : null });
    }
}

public sealed class SessionRepository(Db db) : ISessionStore
{
    public async Task CreateAsync(string key, long accountId, SessionKind kind, DateTime expiresUtc, bool replaceOthers)
    {
        await using var c = await db.OpenAsync();
        // aproveita para limpar chaves vencidas
        await c.ExecuteAsync("""
            delete from sessions where expires_at < now() or (@replaceOthers and account_id = @accountId and kind = @kind);
            insert into sessions(key, account_id, kind, expires_at) values (@key, @accountId, @kind, @expiresUtc);
            """, new { key, accountId, kind = (short)kind, expiresUtc, replaceOthers });
    }

    public async Task<long?> ValidateAsync(string key, SessionKind kind, bool consume)
    {
        await using var c = await db.OpenAsync();
        var sql = consume
            ? "delete from sessions where key = @key and kind = @kind and expires_at > now() returning account_id"
            : "select account_id from sessions where key = @key and kind = @kind and expires_at > now()";
        return await c.QuerySingleOrDefaultAsync<long?>(sql, new { key, kind = (short)kind });
    }
}

public sealed class ServerRegistry(Db db) : IServerRegistry
{
    public async Task HeartbeatAsync(ServerInfo s, TimeSpan ttl)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("""
            insert into servers(id, kind, name, address, port, max_users, cur_users, flags, expires_at)
            values (@Id, @Kind, @Name, @Address, @Port, @MaxUsers, @CurUsers, @Flags, now() + @ttl)
            on conflict (id) do update set kind = excluded.kind, name = excluded.name, address = excluded.address,
                port = excluded.port, max_users = excluded.max_users, cur_users = excluded.cur_users,
                flags = excluded.flags, expires_at = excluded.expires_at
            """, new { s.Id, s.Kind, s.Name, s.Address, s.Port, s.MaxUsers, s.CurUsers, s.Flags, ttl });
    }

    public async Task RemoveAsync(int id)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("delete from servers where id = @id", new { id });
    }

    public async Task<IReadOnlyList<ServerInfo>> ListAsync(string kind)
    {
        await using var c = await db.OpenAsync();
        return (await c.QueryAsync<ServerInfo>(
            "select id, kind, name, address, port, max_users, cur_users, flags from servers where kind = @kind and expires_at > now() order by id",
            new { kind })).AsList();
    }
}
