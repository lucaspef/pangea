using Dapper;
using Pangya.Domain.Guilds;

namespace Pangya.Data;

/// <summary>Guildas no PostgreSQL (migração 005). Escritas em transação; leituras simples.</summary>
public sealed class GuildRepository(Db db) : IGuildStore
{
    const string GuildCols = """
        g.id, g.name, g.master_id, coalesce(a.nickname, '') master_nick, g.notice, g.introduce, g.mark, g.pang, g.point, g.created_at,
        (select count(*) from guild_members m where m.guild_id = g.id and m.class in (1, 2, 3))::int member_count
        """;

    sealed record GuildRow(int Id, string Name, long MasterId, string MasterNick, string Notice, string Introduce, string Mark,
        int Pang, int Point, DateTime CreatedAt, int MemberCount);
    sealed record MemberRow(long AccountId, int GuildId, short Class, string Message, string Nickname);

    static Guild ToGuild(GuildRow r) => new()
    {
        Id = r.Id, Name = r.Name, MasterId = r.MasterId, MasterNick = r.MasterNick, Notice = r.Notice, Introduce = r.Introduce,
        Mark = r.Mark, Pang = r.Pang, Point = r.Point, CreatedAt = r.CreatedAt, MemberCount = r.MemberCount,
    };

    static GuildMember ToMember(MemberRow r) => new()
    {
        AccountId = r.AccountId, GuildId = r.GuildId, Class = r.Class, Message = r.Message, Nickname = r.Nickname,
    };

    public async Task<Guild?> GetAsync(int id)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleOrDefaultAsync<GuildRow>(
            $"select {GuildCols} from guilds g left join accounts a on a.id = g.master_id where g.id = @id and g.closed_at is null", new { id });
        return r == null ? null : ToGuild(r);
    }

    public async Task<(List<Guild> Items, int Total)> ListAsync(int page, int perPage, string? nameLike)
    {
        await using var c = await db.OpenAsync();
        string where = "g.closed_at is null" + (nameLike != null ? " and g.name ilike @like" : "");
        var args = new { like = "%" + (nameLike ?? "").Replace("%", "").Replace("_", "") + "%", skip = (Math.Max(page, 1) - 1) * perPage, perPage };
        int total = await c.ExecuteScalarAsync<int>($"select count(*) from guilds g where {where}", args);
        var rows = await c.QueryAsync<GuildRow>(
            $"select {GuildCols} from guilds g left join accounts a on a.id = g.master_id where {where} order by g.point desc, g.id offset @skip limit @perPage", args);
        var list = new List<Guild>();
        foreach (var r in rows) list.Add(ToGuild(r));
        return (list, total);
    }

    public async Task<GuildMember?> MembershipAsync(long accountId)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleOrDefaultAsync<MemberRow>("""
            select m.account_id, m.guild_id, m.class, m.message, coalesce(a.nickname, '') nickname
            from guild_members m join accounts a on a.id = m.account_id join guilds g on g.id = m.guild_id
            where m.account_id = @accountId and g.closed_at is null
            """, new { accountId });
        return r == null ? null : ToMember(r);
    }

    public async Task<(List<GuildMember> Items, int Total)> MembersAsync(int guildId, int page, int perPage)
    {
        await using var c = await db.OpenAsync();
        int total = await c.ExecuteScalarAsync<int>("select count(*) from guild_members where guild_id = @guildId", new { guildId });
        var rows = await c.QueryAsync<MemberRow>("""
            select m.account_id, m.guild_id, m.class, m.message, coalesce(a.nickname, '') nickname
            from guild_members m join accounts a on a.id = m.account_id
            where m.guild_id = @guildId order by m.class, m.joined_at offset @skip limit @perPage
            """, new { guildId, skip = (Math.Max(page, 1) - 1) * perPage, perPage });
        var list = new List<GuildMember>();
        foreach (var r in rows) list.Add(ToMember(r));
        return (list, total);
    }

    public async Task<int> CountClassAsync(int guildId, int cls)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<int>("select count(*) from guild_members where guild_id = @guildId and class = @cls",
            new { guildId, cls = (short)cls });
    }

    public async Task<bool> NameTakenAsync(string nameKey, int exceptGuild = 0)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<bool>(
            "select exists(select 1 from guilds where name_key = @nameKey and closed_at is null and id <> @exceptGuild)", new { nameKey, exceptGuild });
    }

    public async Task<DateTime?> CooldownAsync(long accountId)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<DateTime?>("select guild_cooldown_until from players where account_id = @accountId", new { accountId });
    }

    public async Task<List<GuildHistory>> HistoryAsync(long accountId, int max)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<(long Id, int GuildId, string GuildName, short State, DateTime At)>(
            "select id, guild_id, guild_name, state, at from guild_history where account_id = @accountId order by id desc limit @max",
            new { accountId, max });
        var list = new List<GuildHistory>();
        foreach (var r in rows) list.Add(new GuildHistory(r.Id, r.GuildId, r.GuildName, r.State, r.At));
        return list;
    }

    public async Task<int> CreateAsync(string name, string nameKey, string introduce, long masterId, GuildChange change)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        int id = await c.ExecuteScalarAsync<int>(
            "insert into guilds(name, name_key, master_id, introduce) values (@name, @nameKey, @masterId, @introduce) returning id",
            new { name, nameKey, masterId, introduce }, tx);
        await c.ExecuteAsync("insert into guild_members(account_id, guild_id, class) values (@masterId, @id, 1)", new { masterId, id }, tx);
        for (int i = 0; i < change.History.Count; i++)                     // o histórico de criação ainda não sabia o id
        {
            var h = change.History[i];
            if (h.Guild == 0) change.History[i] = (h.Account, id, h.GuildName, h.State);
        }
        await ApplyInAsync(c, tx, change);
        await tx.CommitAsync();
        return id;
    }

    public async Task ApplyAsync(GuildChange change)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await ApplyInAsync(c, tx, change);
        await tx.CommitAsync();
    }

    sealed record TicketRow(int Id, int GuildId, long AccountId, string Mark, DateTime? UploadedAt);
    static EmblemTicket ToTicket(TicketRow r) => new(r.Id, r.GuildId, r.AccountId, r.Mark, r.UploadedAt != null);
    const string TicketCols = "id, guild_id, account_id, mark, uploaded_at";

    public async Task<EmblemTicket> NewEmblemTicketAsync(int guildId, long accountId)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleAsync<TicketRow>($"""
            insert into guild_emblem_uploads(guild_id, account_id) values (@guildId, @accountId) returning {TicketCols}
            """, new { guildId, accountId });
        string mark = "g" + r.Id.ToString("x");
        await c.ExecuteAsync("update guild_emblem_uploads set mark = @mark where id = @Id", new { mark, r.Id });
        return new EmblemTicket(r.Id, guildId, accountId, mark, false);
    }

    public async Task<EmblemTicket?> EmblemTicketAsync(int id)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleOrDefaultAsync<TicketRow>($"select {TicketCols} from guild_emblem_uploads where id = @id and applied_at is null", new { id });
        return r == null ? null : ToTicket(r);
    }

    public async Task MarkEmblemUploadedAsync(int id)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update guild_emblem_uploads set uploaded_at = now() where id = @id", new { id });
    }

    public async Task<EmblemTicket?> UploadedEmblemAsync(long accountId)
    {
        await using var c = await db.OpenAsync();
        var r = await c.QuerySingleOrDefaultAsync<TicketRow>($"""
            select {TicketCols} from guild_emblem_uploads
            where account_id = @accountId and uploaded_at is not null and applied_at is null order by id desc limit 1
            """, new { accountId });
        return r == null ? null : ToTicket(r);
    }

    static async Task ApplyInAsync(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction tx, GuildChange ch)
    {
        foreach (var id in ch.Removes)
            await c.ExecuteAsync("delete from guild_members where account_id = @id", new { id }, tx);
        foreach (var (account, guild, cls, message) in ch.Upserts)
            await c.ExecuteAsync("""
                insert into guild_members(account_id, guild_id, class, message) values (@account, @guild, @cls, @message)
                on conflict (account_id) do update set guild_id = excluded.guild_id, class = excluded.class, message = excluded.message
                """, new { account, guild, cls = (short)cls, message }, tx);
        if (ch.Update is { } g)
        {
            await c.ExecuteAsync("""
                update guilds set name = @Name, name_key = lower(@Name), master_id = @MasterId, notice = @Notice, introduce = @Introduce,
                    mark = @Mark, closed_at = case when @close then now() else closed_at end
                where id = @Id
                """, new { g.Id, g.Name, g.MasterId, g.Notice, g.Introduce, g.Mark, close = ch.Close }, tx);
            if (ch.Close) await c.ExecuteAsync("delete from guild_members where guild_id = @Id", new { g.Id }, tx);
        }
        foreach (var (account, guild, name, state) in ch.History)
            await c.ExecuteAsync("insert into guild_history(account_id, guild_id, guild_name, state) values (@account, @guild, @name, @state)",
                new { account, guild, name, state = (short)state }, tx);
        if (ch.EmblemApplied is { } ticket)
            await c.ExecuteAsync("update guild_emblem_uploads set applied_at = now() where id = @ticket", new { ticket }, tx);
        foreach (var account in ch.Cooldowns)
            await c.ExecuteAsync("update players set guild_cooldown_until = now() + interval '24 hours' where account_id = @account",
                new { account }, tx);
        if (ch.Kit is { } kit)
        {
            int n = kit.Left > 0
                ? await c.ExecuteAsync("update items set quantity = @Left where id = @ItemId and account_id = @Account", new { kit.ItemId, kit.Account, kit.Left }, tx)
                : await c.ExecuteAsync("delete from items where id = @ItemId and account_id = @Account", new { kit.ItemId, kit.Account }, tx);
            if (n != 1) throw new InvalidOperationException($"kit {kit.ItemId} não é da conta {kit.Account}");
        }
    }
}
