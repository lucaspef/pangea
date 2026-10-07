using Pangya.Core.Config;
using Pangya.Core.Net;
using Pangya.Domain.Accounts;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Tests;

public class LevelAndRewardTests
{
    [Fact]
    public void ExpCarriesOverLevels()
    {
        var p = new Player { Level = 0, Exp = 25 };
        Assert.Equal(1, Levels.AddExp(p, 10));          // 30 para subir do 0
        Assert.Equal((1, 5), (p.Level, p.Exp));
        Assert.Equal(2, Levels.AddExp(p, 35 + 50));      // 40 (nível 1) + 50 (nível 2)
        Assert.Equal((3, 0), (p.Level, p.Exp));
        var max = new Player { Level = Levels.Max };
        Levels.AddExp(max, 10_000_000);
        Assert.Equal(Levels.Max, max.Level);
        Assert.True(max.Exp < Levels.Need(Levels.Max));
    }

    [Fact]
    public void RewardIsCappedAndOnlyForFinishers()
    {
        var cfg = new RewardConfig { ExpPerHole = 2, MaxPangPerHole = 100 };
        Assert.Equal((250L, 6), Rewards.Compute(200, 50, 3, true, cfg));
        Assert.Equal((300L, 6), Rewards.Compute(999_999, 999_999, 3, true, cfg));   // pang informado é limitado
        Assert.Equal((0L, 0), Rewards.Compute(200, 0, 3, false, cfg));                // saiu antes: nada
    }
}

public class ShopPriceTests
{
    static ShopItem Item(int tid, uint price, uint sale = 0, int pack = 0, int[]? periods = null) =>
        new() { TypeId = tid, Price = price, SalePrice = sale, InStock = 1, PackSize = pack, PeriodPrices = periods ?? [] };

    [Fact]
    public void PricesFollowTheClientRules()
    {
        Assert.Equal(80, ShopService.Price(Item(0x08000800, 100, 80), 1, 0));          // promoção
        Assert.Equal(100, ShopService.Price(Item(0x08000800, 100, 120), 1, 0));        // "promoção" mais cara: preço normal
        Assert.Equal(60, ShopService.Price(Item(0x18000008, 30, pack: 5), 10, 0));      // 2 pacotes de 5
        var mascot = Item(0x40000000, 0, periods: [10, 20, 30, 40, 50]);
        Assert.Equal(40, ShopService.Price(mascot, 1, 30));
        Assert.Null(ShopService.Price(mascot, 1, 15));                                 // mascote não vende 15 dias
        Assert.Null(ShopService.Price(mascot, 1, 2));
        Assert.Null(ShopService.Price(Item(0x20200001, 0, periods: [1, 2, 3, 4, 5]), 1, 7));   // item de caddie: sem 7 dias
    }
}

/// <summary>Cliente já no lobby, com os pacotes de login consumidos.</summary>
static class LobbyClient
{
    public static async Task<(TestClient C, Account Acc)> EnterAsync(GameEnv env, long cookie = 1000)
    {
        var (acc, key) = await env.NewPlayerAsync();
        await using (var db = await env.S.Db.OpenAsync())
            await Dapper.SqlMapper.ExecuteAsync(db, """
                update players set cookie = @cookie where account_id = @id;
                update items set attrs = attrs || '{"hair": 0}' where account_id = @id and type_id = 67108864;
                """, new { cookie, id = acc.Id });
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc);
    }

    public static async Task<(uint Code, List<(uint Tid, uint Id, ushort Time, ushort Count)> Recs)> BuyAsync(TestClient c, params (int Tid, int Days, int Qty)[] items)
    {
        var w = new PacketWriter(0x1D).U8(0).U16((ushort)items.Length);
        foreach (var (tid, days, qty) in items) w.I32(-1).U32((uint)tid).U16((ushort)days).I16(-1).U32((uint)qty);
        await c.SendAsync(w);
        var recs = new List<(uint, uint, ushort, ushort)>();
        while (true)
        {
            var (id, r) = await c.ReceiveAsync();
            if (id == 0xA8)
                for (int n = r.U16(), i = 0; i < n; i++)
                {
                    var b = r.Struct<sBuyItemResult>();
                    recs.Add((b.Typeid, b.guid, b.Time, b.Count));
                }
            else if (id == 0x66) return (r.U32(), recs);
        }
    }
}

[Collection("db")]
public class MyRoomTests(DbFixture fx)
{
    [Fact]
    public async Task CaddieHairMascotCardsAndUpgradesPersist()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, acc) = await LobbyClient.EnterAsync(env);
        await using var _c = c;
        var p0 = (await env.Players.LoadAsync(acc.Id))!;
        int charId = p0.Character!.Id;

        // caddie + item do caddie (registro com o id do CADDIE, 30 dias = 720 h) + 7 dias não vende
        var (code, recs) = await LobbyClient.BuyAsync(c, (0x1C000001, 0, 1));
        Assert.Equal(0u, code);
        uint caddie = recs[0].Id;
        (code, recs) = await LobbyClient.BuyAsync(c, (0x20200001, 30, 1));
        Assert.Equal(0u, code);
        Assert.Equal((caddie, (ushort)720), (recs[0].Id, recs[0].Time));
        Assert.Equal(0x13u, (await LobbyClient.BuyAsync(c, (0x20200001, 7, 1))).Code);
        // cor de cabelo -> id do personagem; repetir -> já tem
        (code, recs) = await LobbyClient.BuyAsync(c, (0x3C000002, 0, 1));
        Assert.Equal((0u, (uint)charId), (code, recs[0].Id));
        Assert.Equal(4u, (await LobbyClient.BuyAsync(c, (0x3C000002, 0, 1))).Code);
        // mascote 30 dias e equipar
        (code, recs) = await LobbyClient.BuyAsync(c, (0x40000000, 30, 1));
        Assert.Equal((0u, (ushort)720), (code, recs[0].Time));
        uint mascot = recs[0].Id;
        await c.SendAsync(new PacketWriter(0x20).U8(8).U32(mascot));
        var eq = await c.ExpectAsync(0x69);
        Assert.Equal((4, 8), (eq.U8(), eq.U8()));
        Assert.Equal(mascot, eq.Struct<sMascotInfo>().guid);
        // pacotes de card: pilha única; abrir dá 3 cards
        (code, recs) = await LobbyClient.BuyAsync(c, (0x7CC00000, 0, 1));
        uint pack = recs[0].Id;
        (code, recs) = await LobbyClient.BuyAsync(c, (0x7CC00000, 0, 1));
        Assert.Equal((pack, (ushort)2), (recs[0].Id, recs[0].Count));
        var drawn = new List<uint>();
        for (int i = 0; i < 2; i++)
        {
            await c.SendAsync(new PacketWriter(0xC2).U32(0x7CC00000).U32(pack));
            var r = await c.ExpectAsync(0x14C);
            Assert.Equal(0u, r.U32());
            r.Struct<sCards>();
            int n = r.U8();
            Assert.Equal(3, n);
            for (int k = 0; k < n; k++) { drawn.Add(r.Struct<sCards>().typeId); Assert.Equal(1u, r.U32()); }
        }
        await c.SendAsync(new PacketWriter(0xC2).U32(0x7CC00000).U32(pack));
        Assert.Equal(1u, (await c.ExpectAsync(0x14C)).U32());              // acabaram os pacotes
        await c.SendAsync(new PacketWriter(0xB5).U32(0x7C800001));
        Assert.Equal(1u, (await c.ExpectAsync(0x158)).U32());              // card que não tem
        // upgrade de força do personagem: 2100, depois 4200; descer custa 0; descer do zero = 5
        await c.SendAsync(new PacketWriter(0x4B).U8(0).U8(0).U32((uint)charId).Zeros(0x1BC));
        var up = await c.ExpectAsync(0xA3);
        Assert.Equal((1, 0, 0, (uint)charId, 2100L), (up.U8(), up.U8(), up.U8(), up.U32(), up.I64()));
        await c.SendAsync(new PacketWriter(0x4B).U8(0).U8(0).U32((uint)charId).Zeros(0x1BC));
        up = await c.ExpectAsync(0xA3);
        up.U8(); up.U8(); up.U8(); up.U32();
        Assert.Equal(4200L, up.I64());
        await c.SendAsync(new PacketWriter(0x4B).U8(2).U8(0).U32((uint)charId).Zeros(0x1BC));
        Assert.Equal(2, (await c.ExpectAsync(0xA3)).U8());
        await c.SendAsync(new PacketWriter(0x4B).U8(2).U8(3).U32((uint)charId).Zeros(0x1BC));
        Assert.Equal(5, (await c.ExpectAsync(0xA3)).U8());
        // mensagem do mascote e aviso do caddie
        await c.SendAsync(new PacketWriter(0x73).U32(mascot).Str("hello"));
        var m = await c.ExpectAsync(0xE0);
        Assert.Equal((4, mascot, "hello"), (m.U8(), m.U32(), m.Str()));
        await c.SendAsync(new PacketWriter(0x6B).U32(caddie).U8(1));
        await c.SendAsync(new PacketWriter(0x20).U8(1).U32(caddie));
        await c.ExpectAsync(0x69);

        // tudo gravado no banco
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var cad = p.Find((int)caddie)!;
        Assert.Equal(0x20200001, cad.Int("part"));
        Assert.Equal(1, cad.Int("warning"));
        Assert.Equal(caddie, (uint)p.Equip.CaddieId);
        Assert.Equal(mascot, (uint)p.Equip.MascotId);
        var ch = p.Find(charId)!;
        Assert.Equal(1, ch.Int("hair"));
        Assert.Equal(1, ch.IntArray("pcl", 5)[0]);
        Assert.Equal("hello", p.Find((int)mascot)!.Attrs["msg"]!.GetValue<string>());
        Assert.Null(p.FindType(0x7CC00000));
        int cards = 0;
        foreach (var it in p.OfGroup(ItemGroup.Card)) cards += it.Quantity;
        Assert.Equal(6, cards);
        Assert.Equal(100_000 - 8200 - 2100 - 4200 - SumPang(env, 0x20200001, 30) - SumPang(env, 0x3C000002, 0) - SumPang(env, 0x40000000, 30), p.Pang);
        var s = PlayerStructs.UserInfo(p);
        Assert.Equal(0x40000000u, s.mascotInfo.tid);
        Assert.Equal(0x20200001u, s.caddieInfo.tidPart);
        Assert.Equal(720, s.caddieInfo.Remain_Partdate);
    }

    static long SumPang(GameEnv env, int tid, int days)
    {
        var item = ((Kr645GameData)env.Data).GetShopItem(tid)!;
        return item.IsCash ? 0 : ShopService.Price(item, 1, days)!.Value;
    }

    [Fact]
    public async Task LockerKeepsItemsAndPang()
    {
        await using var env = await GameEnv.StartAsync();
        var (c, acc) = await LobbyClient.EnterAsync(env);
        await using var _c = c;
        var (code, recs) = await LobbyClient.BuyAsync(c, (0x08000800, 0, 1));    // chapéu
        Assert.Equal(0u, code);
        uint hat = recs[0].Id;
        await c.SendAsync(new PacketWriter(0xD6).U8(1).U64(1000));
        Assert.Equal(0u, (await c.ExpectAsync(0x176)).U32());
        var pang = await c.ExpectAsync(0xC6);
        long expected = 100_000 - 1100 - 1000;
        Assert.Equal((ulong)expected, pang.U64());
        Assert.Equal(1000ul, (await c.ExpectAsync(0x177)).U64());
        await c.SendAsync(new PacketWriter(0xD6).U8(2).U64(5000));              // sacar mais que tem
        Assert.Equal(1u, (await c.ExpectAsync(0x176)).U32());

        var put = new sStoredItemInfo();
        put.itemInfo.dwGuid = hat;
        await c.SendAsync(new PacketWriter(0xD0).U8(1).Struct(put));
        var gone = await c.ExpectAsync(0xA5);
        Assert.Equal((1, 0x08000800u, hat, (ushort)0), (gone.U8(), gone.U32(), gone.U32(), gone.U16()));
        Assert.Equal(0u, (await c.ExpectAsync(0x173)).U32());
        await c.SendAsync(new PacketWriter(0xCF).U32(99).U16(1));
        var page = await c.ExpectAsync(0x172);
        Assert.Equal((1, 1, 1), (page.U16(), page.U16(), page.U8()));
        Assert.Equal(hat, page.Struct<sStoredItemInfo>().itemInfo.dwGuid);
        var clubPut = new sStoredItemInfo();
        clubPut.itemInfo.dwGuid = (uint)(await env.Players.LoadAsync(acc.Id))!.Equip.ClubSetId;
        await c.SendAsync(new PacketWriter(0xD0).U8(1).Struct(clubPut));         // club em uso: recusado
        Assert.Equal(1u, (await c.ExpectAsync(0x173)).U32());
        await c.SendAsync(new PacketWriter(0xD1).U8(1).Struct(put));
        var back = await c.ExpectAsync(0x71);
        back.U16(); back.U16();
        Assert.Equal(hat, back.Struct<sItemInfo>().guid);
        Assert.Equal(0u, (await c.ExpectAsync(0x174)).U32());
        var p = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Equal(1000, p.LockerPang);
        Assert.Equal(ItemLocation.Inventory, p.Find((int)hat)!.Location);
    }

    [Fact]
    public async Task UserInfoAndQuickEquip()
    {
        await using var env = await GameEnv.StartAsync();
        var (c, acc) = await LobbyClient.EnterAsync(env);
        await using var _c = c;
        await c.SendAsync(new PacketWriter(0x2F).U32((uint)acc.Id).U8(5));
        var seq = new List<ushort>();
        while (true)
        {
            var (id, r) = await c.ReceiveAsync();
            seq.Add(id);
            if (id == 0x87) { Assert.Equal(1u, r.U32()); break; }
        }
        Assert.Equal([0x14F, 0x14E, 0x156, 0x150, 0x151, 0x154, 0x152, 0x153, 0x87], seq.Select(x => (int)x));
        await c.SendAsync(new PacketWriter(0x2F).U32(0x7FFFFFF0).U8(5));
        Assert.Equal(3u, (await c.ExpectAsync(0x87)).U32());

        var p = (await env.Players.LoadAsync(acc.Id))!;
        await c.SendAsync(new PacketWriter(0x0B).U8(4).U32((uint)p.Character!.Id));
        var r4 = await c.ExpectAsync(0x49);
        Assert.Equal((4, (uint)acc.Id), (r4.U8(), r4.U32()));
        Assert.Equal((uint)p.Character.Id, r4.Struct<sCharacterInfo>().guid);
        await c.SendAsync(new PacketWriter(0x0B).U8(3).U32(101));                  // club de outra pessoa: sem resposta
        await c.SendAsync(new PacketWriter(0x95));
        var (next, _) = await c.ReceiveAsync();
        Assert.Equal(0x109, next);
    }
}

[Collection("db")]
public class SetItemTests(DbFixture fx)
{
    [Fact]
    public async Task PackageWithOwnedCharacterDeliversTheRest()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, acc) = await LobbyClient.EnterAsync(env);
        await using var _c = c;
        // 블랙리본정장세트(누리포함): Nuri (já tem) + 3 roupas, em cookies (preço de promoção do catálogo)
        long price = env.Data.GetShopItem(0x24200008)!.UnitPrice;
        var (code, recs) = await LobbyClient.BuyAsync(c, (0x24200008, 0, 1));
        Assert.Equal(0u, code);
        Assert.Equal([0x8006020u, 0x800a017u, 0x8010016u], recs.Select(r => r.Tid).Order());
        var p = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Single(p.OfGroup(ItemGroup.Character));                      // não ganhou outra Nuri
        Assert.Equal(1000 - price, p.Cookie);
        Assert.Equal(4u, (await LobbyClient.BuyAsync(c, (0x24200008, 0, 1))).Code);   // já tem tudo: recusa, sem cobrar
        Assert.Equal(1000 - price, (await env.Players.LoadAsync(acc.Id))!.Cookie);
    }
}
