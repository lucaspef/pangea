using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Tests;

public class CouponKindTests
{
    [Theory]
    [InlineData(0x1A000028, 2)] [InlineData(0x1A000029, 2)] [InlineData(0x1A00002A, 2)]
    [InlineData(0x1A000030, 3)] [InlineData(0x1A000033, 3)] [InlineData(0x1A000034, 3)]
    [InlineData(0x1A00003D, 5)] [InlineData(0x1A000070, 4)] [InlineData(0x1A000015, 1)]
    [InlineData(0x18000028, 0)] [InlineData(0x14000000, 0)] [InlineData(0x1A000031, 0)]
    public void MatchesClient(int tid, int kind) => Assert.Equal(kind, LotteryService.CouponKind(tid));
}

/// <summary>Papel Shop e raspadinha de ponta a ponta (docs/protocolo/SPEC-papel-raspadinha.md).</summary>
[Collection("db")]
public class LotteryTests(DbFixture fx)
{
    const int PapelCoupon = 0x1A000028, ScratchCard = 0x1A000030;

    static async Task<(TestClient C, long AccountId)> EnterAsync(GameEnv env, long pang, params (int Tid, int Qty)[] items)
    {
        var (acc, key) = await env.NewPlayerAsync();
        await using (var db = await env.S.Db.OpenAsync())
        {
            await Dapper.SqlMapper.ExecuteAsync(db, "update players set pang = @pang where account_id = @id", new { pang, id = acc.Id });
            foreach (var (tid, qty) in items)
                await Dapper.SqlMapper.ExecuteAsync(db, "insert into items (account_id, type_id, quantity) values (@id, @tid, @qty)",
                    new { id = acc.Id, tid, qty });
        }
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id);
    }

    static List<(int Color, uint Tid, uint Id, uint Qty, byte Class)> Balls(PacketReader r, uint n)
    {
        var list = new List<(int, uint, uint, uint, byte)>();
        for (int i = 0; i < n; i++)
        {
            var b = (r.I32(), r.U32(), r.U32(), r.U32(), r.U8());
            Assert.Equal(0, r.U8());
            Assert.Equal(0, r.U16());
            list.Add(b);
        }
        return list;
    }

    /// <summary>Cada prêmio foi gravado com o id mandado ao cliente.</summary>
    static void AssertSaved(Player p, List<(int Color, uint Tid, uint Id, uint Qty, byte Class)> balls)
    {
        foreach (var b in balls)
        {
            Assert.True(p.Items.TryGetValue((int)b.Id, out var it), $"prêmio {b.Tid:X8} id {b.Id} não está no banco");
            Assert.Equal((int)b.Tid, it!.TypeId);
            Assert.True(it.Quantity >= (int)b.Qty);
        }
    }

    [Fact]
    public async Task PapelWithPangThenWithCouponThenNoMoney()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, accId) = await EnterAsync(env, 1000, (PapelCoupon, 1));
        await using var _c = c;

        await c.SendAsync(new PacketWriter(0x95));                                   // abrir: sem limite de jogadas
        var open = await c.ExpectAsync(0x109);
        Assert.Equal((0xFFFFFFFFu, 0u), (open.U32(), open.U32()));

        // 1ª jogada: o cupom vem antes do pang
        await c.SendAsync(new PacketWriter(0x6D));
        var used = await c.ExpectAsync(0xD3);
        var p = (await env.Players.LoadAsync(accId))!;
        uint coupon = used.U32();
        var r = await c.ExpectAsync(0xD4);
        Assert.Equal(0u, r.U32());
        uint n = r.U32();
        Assert.InRange(n, 1u, 5u);
        var balls = Balls(r, n);
        Assert.Equal((1000UL, 0UL), (r.U64(), r.U64()));                              // não cobrou pang
        var t = await c.ExpectAsync(0xF9);
        Assert.Equal((0xFFFFFFFFu, 0xFFFFFFFFu), (t.U32(), t.U32()));
        p = (await env.Players.LoadAsync(accId))!;
        AssertSaved(p, balls);
        uint wonCoupons = 0;
        foreach (var b in balls) if (b.Tid == PapelCoupon) wonCoupons += b.Qty;
        if (wonCoupons == 0) Assert.False(p.Items.ContainsKey((int)coupon));        // pilha gasta foi apagada
        else Assert.Equal((int)wonCoupons, p.Items[(int)coupon].Quantity);           // ganhou cupom de volta na mesma pilha
        foreach (var b in balls) Assert.Equal((int)b.Class, b.Color);

        // sem cupom (se não ganhou outro): paga 900
        if (wonCoupons == 0)
        {
            await c.SendAsync(new PacketWriter(0x6D));
            r = await c.ExpectAsync(0xD4);
            Assert.Equal(0u, r.U32());
            balls = Balls(r, r.U32());
            Assert.Equal(100UL, r.U64());
            await c.ExpectAsync(0xF9);
            p = (await env.Players.LoadAsync(accId))!;
            Assert.Equal(100, p.Pang);
            AssertSaved(p, balls);
            bool gotCoupon = false;
            foreach (var b in balls) gotCoupon |= b.Tid == PapelCoupon;
            if (!gotCoupon)
            {
                // 100 pang não paga: erro 2, nada muda
                await c.SendAsync(new PacketWriter(0x6D));
                r = await c.ExpectAsync(0xD4);
                Assert.Equal(2u, r.U32());
                await c.ExpectAsync(0xF9);
                Assert.Equal(p.Items.Count, (await env.Players.LoadAsync(accId))!.Items.Count);
            }
        }
    }

    [Fact]
    public async Task ScratchConsumesOneCardAndSavesPrizes()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, accId) = await EnterAsync(env, 0);
        await using (c)
        {
            await c.SendAsync(new PacketWriter(0x70));                                // sem cartão: erro
            Assert.Equal(1u, (await c.ExpectAsync(0xDB)).U32());
            await c.SendAsync(new PacketWriter(0x71).U32(13).Bytes("ABCDEFGHIJKLM"u8));
            Assert.Equal(2u, (await c.ExpectAsync(0xDC)).U32());                    // serial: não existe
        }

        (c, accId) = await EnterAsync(env, 0, (ScratchCard, 3));
        await using var _c = c;
        for (int i = 0; i < 2; i++)
        {
            await c.SendAsync(new PacketWriter(0x70));
            uint card = (await c.ExpectAsync(0xD3)).U32();
            var r = await c.ExpectAsync(0xDB);
            Assert.Equal(0u, r.U32());
            uint n = r.U32();
            Assert.InRange(n, 0u, 2u);
            var balls = Balls(r, n);
            var p = (await env.Players.LoadAsync(accId))!;
            AssertSaved(p, balls);
            Assert.Equal(3 - (i + 1), p.Items[(int)card].Quantity);
        }
    }

    [Fact]
    public async Task PrizesFollowConfiguredPoolsAndSkipOwnedUniqueItems()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, _) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        p.Pang = 10_000;
        // raro pedido mas sem pool de raros -> cookie vazio -> comum; 5 bolas da mesma pilha
        var cfg = new Core.Config.LotteryConfig
        {
            PapelBallWeights = [0, 0, 0, 0, 1], PapelCookiePerMille = 0, PapelRarePerMille = 1000, Rare = [],
            Normal = [new(0x14000001, 1, 2, 2)],
        };
        var shop = new ShopService(env.Players.Store, env.Data);
        var lot = new LotteryService(shop, env.Data, cfg, new Random(1));
        var r = await lot.PlayPapelAsync(p);
        Assert.Equal(0u, r.Code);
        Assert.Equal(5, r.Prizes.Count);
        foreach (var pz in r.Prizes) Assert.Equal((PrizeClass.Normal, 0x14000001, 2), (pz.Class, pz.TypeId, pz.Quantity));
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Equal(10, saved.FindType(0x14000001)!.Quantity);                      // 5 bolas × 2 na mesma pilha
        Assert.Equal(10_000 - 900, saved.Pang);

        // pool só com item inválido: erro 3, nada gravado nem cobrado
        var bad = new LotteryService(shop, env.Data, new Core.Config.LotteryConfig { Normal = [new(0x7FFFFFF0, 1)], Cookie = [] });
        Assert.Equal(3u, (await bad.PlayPapelAsync(saved)).Code);
        Assert.Equal(10_000 - 900, (await env.Players.LoadAsync(acc.Id))!.Pang);
    }

    [Fact]
    public async Task GiveDeliversWithoutChargingAndRejectsUnknownItems()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, _) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        var (code, granted) = await shop.GiveAsync(p, ScratchCard, 5);
        Assert.Equal((ShopCode.Ok, 5), (code, granted[0].Count));
        (code, _) = await shop.GiveAsync(p, ScratchCard, 2);
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Equal(7, saved.FindType(ScratchCard)!.Quantity);
        Assert.Equal(p.Pang, saved.Pang);
        Assert.Equal(ShopCode.BadCode, (await shop.GiveAsync(saved, 0x7FFFFFF0, 1)).Code);
    }
}
