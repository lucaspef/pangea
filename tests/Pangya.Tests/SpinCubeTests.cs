using Pangya.Core.Net;
using Pangya.Domain.Shop;

namespace Pangya.Tests;

/// <summary>Spin Cube do Wiz City: 0xF1 -> 0x1A2 (1 cubo + 1 Lucky Key = 1 prêmio).</summary>
[Collection("db")]
public class SpinCubeTests(DbFixture fx)
{
    [Fact]
    public void ClientDataHasCubeAndKey()
    {
        _ = fx;
        var data = Pangya.Protocol.KR645.Kr645GameData.Load(TestEnv.Config.Data.IffPath);
        Assert.True(data.Exists(SpinCubeService.SpinCube));
        Assert.True(data.Exists(SpinCubeService.LuckyKey));                 // vem do projectg_zzzzitems.pak (tools/sync-iff.py)
        Assert.Contains((byte)0x13, data.Courses);                         // Wiz City ativo no Course.iff do cliente
    }

    [Fact]
    public async Task OpenConsumesCubeAndKeyAndDeliversPrize()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        await shop.GiveAsync(p, SpinCubeService.SpinCube, 2);

        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0xF1).U32(SpinCubeService.SpinCube));   // sem chave
        var fail = await c.ExpectAsync(0x1A2);
        Assert.Equal((8u, (uint)SpinCubeService.SpinCube), (fail.U32(), fail.U32()));
        await c.SendAsync(new PacketWriter(0xF1).U32(0x18000000));                   // não é caixa
        Assert.Equal(4u, (await c.ExpectAsync(0x1A2)).U32());
    }

    [Fact]
    public async Task PrizesAreSavedWithCubeAndKeyGone()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, _) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        await shop.GiveAsync(p, SpinCubeService.SpinCube, 20);
        await shop.GiveAsync(p, SpinCubeService.LuckyKey, 20);
        var box = new SpinCubeService(shop, env.Data, new Random(3));
        long pang0 = p.Pang, pangWon = 0;
        int keysWon = 0;
        for (int i = 0; i < 20; i++)
        {
            var r = await box.OpenAsync(p, SpinCubeService.SpinCube);
            Assert.Equal(0u, r.Code);
            Assert.Equal(2, r.Consumed.Count);
            if (r.PrizeTypeId == SpinCubeService.PangPouch) pangWon += r.PrizeQty;
            else if (r.PrizeTypeId == SpinCubeService.LuckyKey) keysWon += r.PrizeQty;
        }
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Null(saved.FindType(SpinCubeService.SpinCube));                       // 20 cubos gastos: pilha apagada
        Assert.Equal(keysWon, saved.FindType(SpinCubeService.LuckyKey)?.Quantity ?? 0);
        Assert.Equal(pang0 + pangWon, saved.Pang);
        Assert.Equal(SpinCubeService.Missing, (await box.OpenAsync(saved, SpinCubeService.SpinCube)).Code);
    }
}
