using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Pangya.Domain.Players;

namespace Pangya.Data;

public sealed class PlayerRepository(Db db) : IPlayerStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    sealed record PlayerRow(long AccountId, string Login, string Nickname, int IdentityFlags, short Level, int Exp, long Pang, long Cookie, int Flags, string Equip, long LockerPang, string Stats);
    sealed record ItemRow(int Id, int TypeId, int Quantity, string Attrs, DateTime? ExpiresAt, short Location);

    public async Task<Player?> LoadAsync(long accountId)
    {
        await using var c = await db.OpenAsync();
        var p = await c.QuerySingleOrDefaultAsync<PlayerRow>("""
            select p.account_id, a.login, coalesce(a.nickname, '') nickname, a.identity_flags, p.level, p.exp, p.pang, p.cookie, p.flags, p.equip::text equip, p.locker_pang, p.stats::text stats
            from players p join accounts a on a.id = p.account_id where p.account_id = @accountId
            """, new { accountId });
        if (p == null) return null;
        var items = await c.QueryAsync<ItemRow>(
            "select id, type_id, quantity, attrs::text attrs, expires_at, location from items where account_id = @accountId order by id", new { accountId });
        var player = new Player
        {
            AccountId = p.AccountId, Login = p.Login, Nickname = p.Nickname, IdentityFlags = p.IdentityFlags,
            Level = p.Level, Exp = p.Exp, Pang = p.Pang, Cookie = p.Cookie, Flags = p.Flags, LockerPang = p.LockerPang,
            Equip = JsonSerializer.Deserialize<Equipment>(p.Equip, Json) ?? new(),
        };
        ApplyStats(player, p.Stats);
        foreach (var i in items)
            player.Add(new Item
            {
                Id = i.Id, TypeId = i.TypeId, Quantity = i.Quantity, ExpiresAt = i.ExpiresAt, Location = (ItemLocation)i.Location,
                Attrs = JsonNode.Parse(i.Attrs)?.AsObject() ?? [],
            });
        return player;
    }

    /// <summary>players.stats (jsonb): totais, escola, tutorial e recordes por curso.</summary>
    internal static void ApplyStats(Player player, string json)
    {
        var stats = JsonNode.Parse(json);
        if (stats?["totals"] is JsonObject totals && totals.Deserialize<PlayerStats>(Json) is { } t) player.Stats = t;
        if (stats?["school"] is JsonValue school && school.TryGetValue<int>(out var sc)) player.School = sc;
        if (stats?["tutorial"] is JsonArray tut)
            for (int i = 0; i < Math.Min(tut.Count, player.Tutorial.Length); i++) player.Tutorial[i] = tut[i]?.GetValue<int>() ?? 0;
        if (stats?["courses"] is JsonObject courses)
            foreach (var (k, v) in courses)
                if (int.TryParse(k, out var course) && v != null && v.Deserialize<CourseRecord>(Json) is { } rec) player.Courses[course] = rec;
    }

    public async Task<int[]> NewIdsAsync(int count)
    {
        await using var c = await db.OpenAsync();
        return [.. await c.QueryAsync<int>("select nextval('object_id_seq')::int from generate_series(1, @count)", new { count })];
    }

    public async Task ApplyAsync(long accountId, PlayerChanges ch)
    {
        if (ch.IsEmpty) return;
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await ApplyInAsync(c, tx, accountId, ch, strict: false);
        await tx.CommitAsync();
    }

    public async Task ApplyTradeAsync(long sellerId, PlayerChanges seller, long buyerId, PlayerChanges buyer)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await ApplyInAsync(c, tx, sellerId, seller, strict: true);         // o item tem de continuar com o vendedor
        await ApplyInAsync(c, tx, buyerId, buyer, strict: true);
        await tx.CommitAsync();
    }

    /// <summary>strict: cada item alterado/apagado tem de existir na conta (senão exceção = rollback).</summary>
    internal static async Task ApplyInAsync(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction tx, long accountId, PlayerChanges ch, bool strict)
    {
        foreach (var it in ch.Added)
            await c.ExecuteAsync("""
                insert into items(id, account_id, type_id, quantity, attrs, expires_at, location)
                values (@Id, @accountId, @TypeId, @Quantity, @attrs::jsonb, @ExpiresAt, @loc)
                """, new { it.Id, accountId, it.TypeId, it.Quantity, attrs = it.Attrs.ToJsonString(), it.ExpiresAt, loc = (short)it.Location }, tx);
        foreach (var it in ch.Updated)
        {
            int n = await c.ExecuteAsync("""
                update items set quantity = @Quantity, attrs = @attrs::jsonb, expires_at = @ExpiresAt, location = @loc
                where id = @Id and account_id = @accountId
                """, new { it.Id, accountId, it.Quantity, attrs = it.Attrs.ToJsonString(), it.ExpiresAt, loc = (short)it.Location }, tx);
            if (strict && n != 1) throw new InvalidOperationException($"item {it.Id} não é da conta {accountId}");
        }
        foreach (var id in ch.Removed)
        {
            int n = await c.ExecuteAsync("delete from items where id = @id and account_id = @accountId", new { id, accountId }, tx);
            if (strict && n != 1) throw new InvalidOperationException($"item {id} não é da conta {accountId}");
        }
        await c.ExecuteAsync("""
            update players set pang = coalesce(@Pang, pang), cookie = coalesce(@Cookie, cookie),
                locker_pang = coalesce(@LockerPang, locker_pang), level = coalesce(@Level::smallint, level),
                exp = coalesce(@Exp, exp), flags = coalesce(@Flags, flags), equip = coalesce(@equip::jsonb, equip),
                stats = stats || jsonb_strip_nulls(jsonb_build_object('courses', @courses::jsonb, 'totals', @totals::jsonb,
                    'school', @school::int, 'tutorial', @tutorial::jsonb))
            where account_id = @accountId
            """, new
        {
            accountId, ch.Pang, ch.Cookie, ch.LockerPang, ch.Level, ch.Exp, ch.Flags,
            equip = ch.Equip == null ? null : JsonSerializer.Serialize(ch.Equip, Json),
            courses = ch.Courses == null ? null : JsonSerializer.Serialize(ch.Courses, Json),
            totals = ch.Stats == null ? null : JsonSerializer.Serialize(ch.Stats, Json),
            school = ch.School,
            tutorial = ch.Tutorial == null ? null : JsonSerializer.Serialize(ch.Tutorial, Json),
        }, tx);
    }

    public async Task SaveItemAsync(long accountId, Item item)
    {
        await using var c = await db.OpenAsync();
        if (item.Quantity <= 0)
            await c.ExecuteAsync("delete from items where id = @Id and account_id = @accountId", new { item.Id, accountId });
        else
            await c.ExecuteAsync("update items set quantity = @Quantity, attrs = @attrs::jsonb, expires_at = @ExpiresAt where id = @Id and account_id = @accountId",
                new { item.Id, item.Quantity, item.ExpiresAt, attrs = item.Attrs.ToJsonString(), accountId });
    }

    public async Task SaveEquipAsync(long accountId, Equipment equip)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update players set equip = @equip::jsonb where account_id = @accountId",
            new { accountId, equip = JsonSerializer.Serialize(equip, Json) });
    }

    public async Task<Player> CreateAsync(long accountId, NewPlayer spec)
    {
        await using (var c = await db.OpenAsync())
        await using (var tx = await c.BeginTransactionAsync())
        {
            await c.ExecuteAsync("insert into players(account_id, pang, cookie) values (@accountId, @Pang, @Cookie)",
                new { accountId, spec.Pang, spec.Cookie }, tx);
            var equip = new Equipment();
            foreach (var it in spec.Items)
            {
                var id = await c.ExecuteScalarAsync<int>(
                    "insert into items(account_id, type_id, quantity, attrs) values (@accountId, @TypeId, @Quantity, @attrs::jsonb) returning id",
                    new { accountId, it.TypeId, it.Quantity, attrs = (it.Attrs ?? []).ToJsonString() }, tx);
                if (!it.Equip) continue;
                switch (Item.GroupOf(it.TypeId))
                {
                    case ItemGroup.Character: equip.CharacterId = id; break;
                    case ItemGroup.Caddie: equip.CaddieId = id; break;
                    case ItemGroup.ClubSet: equip.ClubSetId = id; break;
                    case ItemGroup.Ball: equip.BallTypeId = it.TypeId; break;
                    case ItemGroup.Mascot: equip.MascotId = id; break;
                }
            }
            await c.ExecuteAsync("update players set equip = @equip::jsonb where account_id = @accountId",
                new { accountId, equip = JsonSerializer.Serialize(equip, Json) }, tx);
            await tx.CommitAsync();
        }
        return (await LoadAsync(accountId))!;
    }
}
