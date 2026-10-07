using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Pangya.Domain.Players;

namespace Pangya.Data;

public sealed class PlayerRepository(Db db) : IPlayerStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    sealed record PlayerRow(long AccountId, string Login, string Nickname, int IdentityFlags, short Level, int Exp, long Pang, long Cookie, int Flags, string Equip);
    sealed record ItemRow(int Id, int TypeId, int Quantity, string Attrs, DateTime? ExpiresAt);

    public async Task<Player?> LoadAsync(long accountId)
    {
        await using var c = await db.OpenAsync();
        var p = await c.QuerySingleOrDefaultAsync<PlayerRow>("""
            select p.account_id, a.login, coalesce(a.nickname, '') nickname, a.identity_flags, p.level, p.exp, p.pang, p.cookie, p.flags, p.equip::text equip
            from players p join accounts a on a.id = p.account_id where p.account_id = @accountId
            """, new { accountId });
        if (p == null) return null;
        var items = await c.QueryAsync<ItemRow>(
            "select id, type_id, quantity, attrs::text attrs, expires_at from items where account_id = @accountId order by id", new { accountId });
        var player = new Player
        {
            AccountId = p.AccountId, Login = p.Login, Nickname = p.Nickname, IdentityFlags = p.IdentityFlags,
            Level = p.Level, Exp = p.Exp, Pang = p.Pang, Cookie = p.Cookie, Flags = p.Flags,
            Equip = JsonSerializer.Deserialize<Equipment>(p.Equip, Json) ?? new(),
        };
        foreach (var i in items)
            player.Add(new Item
            {
                Id = i.Id, TypeId = i.TypeId, Quantity = i.Quantity, ExpiresAt = i.ExpiresAt,
                Attrs = JsonNode.Parse(i.Attrs)?.AsObject() ?? [],
            });
        return player;
    }

    public async Task<int[]> NewIdsAsync(int count)
    {
        await using var c = await db.OpenAsync();
        return [.. await c.QueryAsync<int>("select nextval('object_id_seq')::int from generate_series(1, @count)", new { count })];
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
