using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Rooms;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

public class PersonalShopRuleTests
{
    [Fact]
    public void TitleRules()
    {
        Assert.Equal(TradeCode.Ok, PersonalShopRules.CheckTitle("Loja do Royal"));
        Assert.Equal(TradeCode.NoTitle, PersonalShopRules.CheckTitle(""));
        Assert.Equal(TradeCode.BadTitle, PersonalShopRules.CheckTitle("   "));
        Assert.Equal(TradeCode.BadTitle, PersonalShopRules.CheckTitle(new string('x', 32)));
    }

    [Fact]
    public async Task ItemsOnSaleAreLockedAndEquippedOnesCannotBeSold()
    {
        var p = new Player { AccountId = 1 };
        var balls = new Item { Id = 10, TypeId = 0x14000001, Quantity = 10 };
        var club = new Item { Id = 11, TypeId = 0x10000007, Quantity = 1 };
        p.Items[balls.Id] = balls;
        p.Items[club.Id] = club;
        p.Reserved = new Dictionary<int, int> { [10] = 4, [11] = 1 };
        Assert.Equal((6, 0), (PlayerActions.Free(p, balls), PlayerActions.Free(p, club)));
        Assert.True(PlayerActions.IsBusy(p, club));
        Assert.False(PlayerActions.IsBusy(p, balls));
        var actions = new PlayerActions(null!, null!);                       // recusa antes de gravar
        Assert.Null(await actions.DeleteItemAsync(p, club.TypeId, 1));
        Assert.Null(await actions.DeleteItemAsync(p, balls.TypeId, 7));       // só 6 livres

        var buyer = new Player { AccountId = 2, Pang = 1000 };
        var t = new TradeItem { Index = 0, TypeId = club.TypeId, ItemId = club.Id, Quantity = 1, Price = 10 };
        Assert.NotNull(PersonalShopRules.Transfer(p, buyer, t, 1, 99));
        p.Equip.ClubSetId = club.Id;                                          // equipou depois de anunciar
        Assert.Null(PersonalShopRules.Transfer(p, buyer, t, 1, 99));
    }
}

/// <summary>Loja pessoal no lounge de ponta a ponta (dois jogadores).</summary>
[Collection("db")]
public class PersonalShopTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> EnterAsync(GameEnv env, int itemTid = 0, int qty = 0, long pang = -1)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        if (itemTid != 0) await new ShopService(env.Players.Store, env.Data).GiveAsync(p, itemTid, qty);
        await env.Players.Store.ApplyAsync(acc.Id, new PlayerChanges { Pang = pang >= 0 ? pang : null, Level = 5 });   // loja exige nível ≥ 1
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id);
    }

    [Fact]
    public async Task OpenVisitAndBuyMovesItemAndPangAtomically()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        int tid = 0;
        foreach (var x in ((Kr645GameData)env.Data).Iff.Items)
            if (Item.GroupOf((int)x.c.TypeId) == ItemGroup.Usable && env.Data.CanTrade((int)x.c.TypeId)) { tid = (int)x.c.TypeId; break; }
        Assert.NotEqual(0, tid);
        var (a, aId) = await EnterAsync(env, tid, 10, pang: 1000);
        var (b, bId) = await EnterAsync(env, pang: 500);
        await using var _a = a;
        await using var _b = b;
        int stack = (await env.Players.LoadAsync(aId))!.FindType(tid)!.Id;

        await a.SendAsync(new PacketWriter(0x08).U8(0).U32(0).U32(0).U8(30).U8(2).U8(5).U8(0).U8(0).Str("lounge").Str(""));
        var enter = await a.ExpectAsync(0x47);
        enter.U8(); enter.U8();
        ushort index = enter.Struct<sRoomInfo>().roomGuid;
        await b.SendAsync(new PacketWriter(0x09).U16(index).Str(""));
        await b.ExpectAsync(0x47);

        await b.SendAsync(new PacketWriter(0x77).U32((uint)aId));           // ainda não há loja
        Assert.Equal((uint)TradeCode.Closed, (await b.ExpectAsync(0xE4)).U32());

        await a.SendAsync(new PacketWriter(0x76));                          // criar/editar: todos veem
        Assert.Equal(1u, (await b.ExpectAsync(0xE3)).U32());
        await a.SendAsync(new PacketWriter(0x79).Str("Loja A"));
        var title = await b.ExpectAsync(0xE6);
        Assert.Equal((1u, "Loja A"), (title.U32(), title.Str()));

        // publicar 4 unidades a 100 cada; um item que não é dele é recusado
        await a.SendAsync(new PacketWriter(0x7C).U32(1).U32(1).Struct(new sTradeItem { dwTid = (uint)tid, dwGuid = 12345, iNum = 1, i64Price = 100 }));
        Assert.Equal((uint)TradeCode.NotYours, (await a.ExpectAsync(0xE9)).U32());
        await a.SendAsync(new PacketWriter(0x7C).U32(1).U32(1).Struct(new sTradeItem { dwTid = (uint)tid, dwGuid = (uint)stack, iNum = 4, i64Price = 100 }));
        var pub = await a.ExpectAsync(0xE9);
        Assert.Equal((1u, 1u), (pub.U32(), pub.U32()));
        Assert.Equal(1u, (await b.ExpectAsync(0xE1)).U32());                // ícone da loja para os outros
        var seller = env.Game.World.Find(aId)!.Player;
        Assert.Equal(4, seller.Reserved[stack]);                              // preso no inventário enquanto à venda

        await b.SendAsync(new PacketWriter(0x77).U32((uint)aId));
        var shop = await b.ExpectAsync(0xE4);
        Assert.Equal((1u, 1u), (shop.U32(), shop.U32()));
        shop.Bytes(22);
        Assert.Equal(("Loja A", (uint)aId, 1u), (shop.Str(), shop.U32(), shop.U32()));
        var listed = shop.Struct<sTradeItem>();
        Assert.Equal((4, 100L), (listed.iNum, listed.i64Price));

        await b.SendAsync(new PacketWriter(0x7D).U32((uint)aId).Struct(new sTradeItem { iIndex = 0, dwTid = (uint)tid, dwGuid = (uint)stack, iNum = 2, i64Price = 1 }));
        var bought = await b.ExpectAsync(0xEA);                              // comprador: paga o preço da loja (100), não o 1
        Assert.Equal((1u, 0, 300UL), (bought.U32(), bought.U8(), bought.U64()));
        Assert.Equal(2, bought.Struct<sTradeItem>().iNum);
        Assert.Equal(2, bought.U8());                                        // objeto novo
        var info = bought.Struct<sItemInfo>();
        Assert.Equal(((uint)tid, (short)2), (info.tid, info.Common[0]));
        var sold = await a.ExpectAsync(0xEA);                                // vendedor
        Assert.Equal((1u, 1, 200UL), (sold.U32(), sold.U8(), sold.U64()));
        var left = await b.ExpectAsync(0xEB);
        left.Str(); left.U32();
        Assert.Equal(2, left.Struct<sTradeItem>().iNum);
        Assert.Equal(1u, left.U32());

        var sa = (await env.Players.LoadAsync(aId))!;
        var sb = (await env.Players.LoadAsync(bId))!;
        Assert.Equal((8, 1200L), (sa.FindType(tid)!.Quantity, sa.Pang));
        Assert.Equal(2, seller.Reserved[stack]);
        Assert.Equal((2, 300L), (sb.FindType(tid)!.Quantity, sb.Pang));

        await b.SendAsync(new PacketWriter(0x7D).U32((uint)aId).Struct(new sTradeItem { iIndex = 0, iNum = 3 }));
        Assert.Equal((uint)TradeCode.BadQuantity, (await b.ExpectAsync(0xEA)).U32());   // só sobraram 2
        await b.SendAsync(new PacketWriter(0x7D).U32((uint)aId).Struct(new sTradeItem { iIndex = 0, iNum = 2 }));
        Assert.Equal(1u, (await b.ExpectAsync(0xEA)).U32());                 // compra o resto (200 de 300)
        var soldOut = await b.ExpectAsync(0xEB);
        soldOut.Str(); soldOut.U32(); soldOut.Struct<sTradeItem>();
        Assert.Equal(3u, soldOut.U32());                                     // esgotada
        await b.SendAsync(new PacketWriter(0x7D).U32((uint)aId).Struct(new sTradeItem { iIndex = 0, iNum = 1 }));
        Assert.Equal((uint)TradeCode.SoldAlready, (await b.ExpectAsync(0xEA)).U32());

        await a.SendAsync(new PacketWriter(0x75));                           // fecha
        var closed = await b.ExpectAsync(0xE2);
        Assert.Equal(1u, closed.U32());
        Assert.Empty(env.Game.World.Rooms.Get(index)!.Shops);
        Assert.Empty(seller.Reserved);
        Assert.Equal(1400L, (await env.Players.LoadAsync(aId))!.Pang);
        Assert.Equal(100L, (await env.Players.LoadAsync(bId))!.Pang);
    }

    [Fact]
    public async Task RookieCannotOpenAndShopNeedsLounge()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();                       // nível 0
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x76));                          // fora do lounge
        Assert.Equal((uint)TradeCode.NotInLounge, (await c.ExpectAsync(0xE3)).U32());
        await c.SendAsync(new PacketWriter(0x08).U8(0).U32(0).U32(0).U8(30).U8(2).U8(5).U8(0).U8(0).Str("l").Str(""));
        await c.ExpectAsync(0x47);
        await c.SendAsync(new PacketWriter(0x76));
        Assert.Equal((uint)TradeCode.LevelLimit, (await c.ExpectAsync(0xE3)).U32());
    }
}
