using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Tests;

/// <summary>Itens que se abrem no My Room, aluguel, tutorial, escola e lista de servidores (GameHandler.Boxes.cs).</summary>
[Collection("db")]
public class BoxesTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> EnterAsync(GameEnv env, params (int Tid, int Qty)[] items)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        foreach (var (tid, qty) in items) Assert.Equal(ShopCode.Ok, (await shop.GiveAsync(p, tid, qty)).Code);
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id);
    }

    [Fact]
    public async Task PouchEnvelopeAndBoxesPayAndAreUsedUp()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, id) = await EnterAsync(env, (GameHandler.LuckyPouch, 2), (GameHandler.NewYearMoney, 1), (0x1A000054, 1));
        await using var _c = c;
        long pang0 = (await env.Players.LoadAsync(id))!.Pang;
        var p0 = (await env.Players.LoadAsync(id))!;
        int pouch = p0.FindType(GameHandler.LuckyPouch)!.Id, envelope = p0.FindType(GameHandler.NewYearMoney)!.Id;

        await c.SendAsync(new PacketWriter(0x59).U32(0));                    // bolsa da sorte
        var r = await c.ExpectAsync(0x121);
        Assert.Equal((0, (uint)pouch, 1u, (uint)GameHandler.PangPouchTid), (r.U8(), r.U32(), r.U32(), r.U32()));
        r.U32();
        Assert.Equal(1000, r.I32());

        await c.SendAsync(new PacketWriter(0x90).U32((uint)envelope));        // envelope de ano novo
        Assert.Equal(1, (await c.ExpectAsync(0xA5)).U8());                  // contagem do envelope
        Assert.Equal((uint)envelope, (await c.ExpectAsync(0xDA)).U32());
        await c.SendAsync(new PacketWriter(0xAA).U32(0x1A000054));            // caixa surpresa
        var box = await c.ExpectAsync(0x1A2);
        Assert.Equal((0u, 0x1A000054u, (uint)GameHandler.PangPouchTid, 3000u), (box.U32(), box.U32(), box.U32(), box.U32()));
        await c.SendAsync(new PacketWriter(0xAA).U32(0x1A000054));            // acabou
        Assert.Equal(8u, (await c.ExpectAsync(0x1A2)).U32());

        var p = (await env.Players.LoadAsync(id))!;
        Assert.Equal(pang0 + 1000 + 10000 + 3000, p.Pang);
        Assert.Equal(1, p.FindType(GameHandler.LuckyPouch)!.Quantity);
        Assert.Null(p.FindType(GameHandler.NewYearMoney));
    }

    [Fact]
    public async Task RentalTutorialSchoolAndServerList()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        await new ShopService(env.Players.Store, env.Data).GiveAsync(p, 0x40000000, 1, 30);   // mascote de 30 dias = alugado
        int mascot = p.FindType(0x40000000)!.Id, character = p.Character!.Id;
        var until = p.Find(mascot)!.ExpiresAt!.Value;
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        var tut0 = await c.ExpectAsync(0x11B);                              // tutorial no login
        Assert.Equal((3, 0, 0u), (tut0.U8(), tut0.U8(), tut0.U32()));

        await c.SendAsync(new PacketWriter(0xE8).U32((uint)mascot));         // estende 7 dias
        var ext = await c.ExpectAsync(0x194);
        Assert.Equal((0, 0x40000000u, (uint)mascot), (ext.U8(), ext.U32(), ext.U32()));
        var after = (await env.Players.LoadAsync(acc.Id))!.Find(mascot)!.ExpiresAt!.Value;
        Assert.InRange((after - until).TotalHours, 167.9, 168.1);
        await c.SendAsync(new PacketWriter(0xE8).U32((uint)character));      // personagem não é alugado
        Assert.Equal(1, (await c.ExpectAsync(0x194)).U8());
        await c.SendAsync(new PacketWriter(0xE9).U32((uint)mascot));         // apaga
        Assert.Equal(0, (await c.ExpectAsync(0x195)).U8());
        Assert.Null((await env.Players.LoadAsync(acc.Id))!.Find(mascot));

        await c.SendAsync(new PacketWriter(0xA6).U8(0).U8(1).U32(0x200));    // missão do básico
        var tut = await c.ExpectAsync(0x11B);
        Assert.Equal((3, 0, 0u, 0x200u), (tut.U8(), tut.U8(), tut.U32(), tut.U32()));
        await c.SendAsync(new PacketWriter(0x3B).U32(4));                    // escola
        var sc = await c.ExpectAsync(0x4F);
        Assert.Equal((0, 4u), (sc.U8(), sc.U32()));
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Equal((4, 0x200), (saved.School, saved.Tutorial[1]));

        await c.SendAsync(new PacketWriter(0x43));                           // servidores e canais
        var list = await c.ExpectAsync(0x9D);
        int n = list.U8();
        for (int i = 0; i < n; i++) list.Struct<Pangya.Protocol.KR645.sGameServerInfo>();
        Assert.True(list.U8() >= 1);                                         // ao menos um canal
    }
}
