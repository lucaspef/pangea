using Pangya.Core.Net;
using Pangya.Domain.Mail;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Correio de ponta a ponta (docs/protocolo/SPEC-correio-presentes.md).</summary>
[Collection("db")]
public class MailTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id, string Nick)> EnterAsync(GameEnv env, long pang, params (int Tid, int Qty)[] items)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        foreach (var (tid, qty) in items) await new ShopService(env.Players.Store, env.Data).GiveAsync(p, tid, qty);
        await env.Players.Store.ApplyAsync(acc.Id, new PlayerChanges { Pang = pang });
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id, acc.Nickname!);
    }

    static PacketWriter Send(long me, long to, string text, params (int Id, int Tid, int Count)[] items)
    {
        var w = new PacketWriter(0xBB).U32((uint)me).U32((uint)to).U8((byte)items.Length).U64(0).Str("").Str(text);
        foreach (var (id, tid, count) in items) w.Struct(new sMailIncludeItem { dwIDX = (uint)id, dwTID = (uint)tid, iCount = count });
        return w;
    }

    [Fact]
    public async Task SendReadTakeDeleteAndGift()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        int tid = 0;
        foreach (var x in ((Kr645GameData)env.Data).Iff.Items)
            if (Item.GroupOf((int)x.c.TypeId) == ItemGroup.Usable && env.Data.CanTrade((int)x.c.TypeId)) { tid = (int)x.c.TypeId; break; }
        var (a, aId, aNick) = await EnterAsync(env, 2000, (tid, 10));
        var (b, bId, bNick) = await EnterAsync(env, 0);
        await using var _a = a;
        await using var _b = b;
        int stack = (await env.Players.LoadAsync(aId))!.FindType(tid)!.Id;

        await a.SendAsync(new PacketWriter(0x07).U8(0).Str(bNick));                 // busca do destinatário
        var found = await a.ExpectAsync(0x9F);
        Assert.Equal((0, (uint)bId), (found.U8(), found.U32()));
        await a.SendAsync(new PacketWriter(0x07).U8(0).Str("ninguem_aqui"));
        Assert.Equal(1, (await a.ExpectAsync(0x9F)).U8());

        await a.SendAsync(Send(aId, aId, "eu"));                                    // para si mesmo
        Assert.Equal((byte)MailCode.NoReceiver, (await a.ExpectAsync(0x13F)).U8());
        await a.SendAsync(Send(aId, bId, "x", (12345, tid, 1)));                    // item de outro
        Assert.Equal((byte)MailCode.NotYours, (await a.ExpectAsync(0x13F)).U8());
        await a.SendAsync(Send(aId, bId, "toma", (stack, tid, 3)));                 // 3 unidades, taxa 500
        var sent = await a.ExpectAsync(0x13E);
        Assert.Equal(1u, sent.U32());
        Assert.Equal(3, sent.Struct<sMailIncludeItem>().iCount);
        Assert.Equal(1500UL, (await a.ExpectAsync(0xC6)).U64());
        await b.ExpectAsync(0x31);                                                   // aviso de carta nova
        Assert.Equal((7, 1500L), ((await env.Players.LoadAsync(aId))!.FindType(tid)!.Quantity, (await env.Players.LoadAsync(aId))!.Pang));

        await b.SendAsync(new PacketWriter(0xE6));                                   // não lidas
        var unread = await b.ExpectAsync(0x15E);
        Assert.Equal(1u, unread.U32());
        var brief = unread.Struct<sMailInfoBrief>();
        Assert.Equal((1, (uint)tid, 3, (byte)0), (brief.itemCount, brief.item.dwTID, brief.item.iCount, brief.bRead));
        await b.SendAsync(new PacketWriter(0xBC).U32(1));                            // lista
        var list = await b.ExpectAsync(0x140);
        Assert.Equal((1u, 1u, 1u), (list.U32(), list.U32(), list.U32()));
        await b.SendAsync(new PacketWriter(0xBD).U32(brief.id));                     // ler
        var read = await b.ExpectAsync(0x142);
        Assert.Equal(brief.id, read.U32());
        Assert.Equal(aNick, read.Str());
        read.Str();
        Assert.Equal(("toma", 1, 1u), (read.Str(), (int)read.U8(), read.U32()));
        await b.SendAsync(new PacketWriter(0xBE).U32(1).U32(brief.id).U32(1));      // com anexo: não apaga
        Assert.Equal(9u, (await b.ExpectAsync(0x15D)).U32());
        await b.SendAsync(new PacketWriter(0xBF).U32(brief.id));                     // pegar
        var taken = await b.ExpectAsync(0x144);
        Assert.Equal(1u, taken.U32());
        var got = taken.Struct<sMailIncludeItem>();
        Assert.Equal(((uint)tid, 3), (got.dwTID, got.iCount));
        Assert.Equal(3, (await env.Players.LoadAsync(bId))!.FindType(tid)!.Quantity);
        await b.SendAsync(new PacketWriter(0xBF).U32(brief.id));                     // de novo: nada
        Assert.Equal(1, (await b.ExpectAsync(0x145)).U8());
        await b.SendAsync(new PacketWriter(0xBE).U32(1).U32(brief.id).U32(1));      // agora apaga
        await b.ExpectAsync(0x15C);

        // presente da loja: vira carta para B
        long cost = ShopService.Price(env.Data.GetShopItem(0x18000008)!, 5, 0)!.Value;
        await a.SendAsync(new PacketWriter(0x1F).Str(bNick).U32((uint)bId).Str("presente!").U8(0).U16(1)
            .Struct(new sBuyItem { TypeCode = 0x18000008, ItemCount = 5 }));
        var gift = await a.ExpectAsync(0x68);
        Assert.Equal(0u, gift.U32());
        Assert.Equal((ulong)(1500 - cost), gift.U64());
        await b.ExpectAsync(0x31);

        // carta do sistema com pang: vai para o saldo (0xC6), não para o inventário
        await env.Game.Context.Mail!.SendSystemAsync(bId, "@GM", "bonus", [(MailService.PangPouch, 777)]);
        await b.SendAsync(new PacketWriter(0xBC).U32(1));
        var l2 = await b.ExpectAsync(0x140);
        l2.U32(); l2.U32();
        Assert.Equal(2u, l2.U32());
        var newest = l2.Struct<sMailInfoBrief>();
        await b.SendAsync(new PacketWriter(0xBF).U32(newest.id));
        Assert.Equal(0u, (await b.ExpectAsync(0x144)).U32());
        Assert.Equal(777UL, (await b.ExpectAsync(0xC6)).U64());
    }
}
