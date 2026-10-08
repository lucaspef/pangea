using Dapper;
using Pangya.Domain.Messenger;

namespace Pangya.Data;

/// <summary>Amigos do mensageiro (migração 008): uma linha por lado.</summary>
public sealed class FriendRepository(Db db) : IFriendStore
{
    sealed record NickRow(long Id, string Nickname);
    sealed record Row(long FriendId, string Nickname, int Level, short State, string Alias, bool Blocked, bool BlockedMe);

    const string Select = """
        select f.friend_id, coalesce(a.nickname, '') nickname, coalesce(p.level, 0)::int level, f.state, f.alias, f.blocked,
               coalesce(o.blocked, false) blocked_me
        from friends f
        join accounts a on a.id = f.friend_id
        left join players p on p.account_id = f.friend_id
        left join friends o on o.owner_id = f.friend_id and o.friend_id = f.owner_id
        """;

    static Friend ToFriend(Row r) => new(r.FriendId, r.Nickname, r.Level, (FriendState)r.State, r.Alias, r.Blocked, r.BlockedMe);

    public async Task<List<Friend>> ListAsync(long owner)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<Row>($"{Select} where f.owner_id = @owner order by f.created_at", new { owner });
        var list = new List<Friend>();
        foreach (var r in rows) list.Add(ToFriend(r));
        return list;
    }

    public async Task<Friend?> GetAsync(long owner, long friend)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleOrDefaultAsync<Row>($"{Select} where f.owner_id = @owner and f.friend_id = @friend", new { owner, friend });
        return r == null ? null : ToFriend(r);
    }

    public async Task<int> CountAsync(long owner)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<int>("select count(*) from friends where owner_id = @owner", new { owner });
    }

    public async Task<(long Id, string Nickname)?> FindByNickAsync(string nick)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleOrDefaultAsync<NickRow>(
            "select id, nickname from accounts where nickname is not null and lower(nickname) = lower(@nick) limit 1", new { nick });
        return r == null ? null : (r.Id, r.Nickname);
    }

    public async Task RequestAsync(long from, long to)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into friends(owner_id, friend_id, state) values (@from, @to, 1) on conflict do nothing", new { from, to }, tx);
        await c.ExecuteAsync("insert into friends(owner_id, friend_id, state) values (@to, @from, 2) on conflict do nothing", new { from, to }, tx);
        await tx.CommitAsync();
    }

    public async Task<bool> AcceptAsync(long owner, long friend)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        int n = await c.ExecuteAsync("update friends set state = 3 where owner_id = @owner and friend_id = @friend and state = 2",
            new { owner, friend }, tx);
        int m = await c.ExecuteAsync("update friends set state = 3 where owner_id = @friend and friend_id = @owner and state = 1",
            new { owner, friend }, tx);
        if (n != 1 || m != 1) { await tx.RollbackAsync(); return false; }
        await tx.CommitAsync();
        return true;
    }

    public async Task RemoveAsync(long a, long b)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("delete from friends where (owner_id = @a and friend_id = @b) or (owner_id = @b and friend_id = @a)", new { a, b });
    }

    public async Task<bool> SetBlockedAsync(long owner, long friend, bool blocked)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteAsync("update friends set blocked = @blocked where owner_id = @owner and friend_id = @friend",
            new { owner, friend, blocked }) == 1;
    }

    public async Task<bool> SetAliasAsync(long owner, long friend, string alias)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteAsync("update friends set alias = @alias where owner_id = @owner and friend_id = @friend",
            new { owner, friend, alias }) == 1;
    }
}
