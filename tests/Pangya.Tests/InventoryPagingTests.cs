using Pangya.Domain.Shop;

namespace Pangya.Tests;

/// <summary>Inventário grande no login vai em vários 0x71 (até 50 itens, total == n), como o servidor GB.</summary>
[Collection("db")]
public class InventoryPagingTests(DbFixture fx)
{
    [Fact]
    public async Task LargeInventoryIsSplitInPagesOfFifty()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        int given = 0;
        foreach (var b in env.Data is Pangya.Protocol.KR645.Kr645GameData d ? d.Iff.Balls : [])
            if (given < 120 && (await shop.GiveAsync(p, (int)b.c.TypeId, 1)).Code == ShopCode.Ok) given++;
        Assert.True(given > 100);
        int expected = 0;
        foreach (var it in (await env.Players.LoadAsync(acc.Id))!.Items.Values)
            if (it.Group is Domain.Players.ItemGroup.Ball or Domain.Players.ItemGroup.Usable or Domain.Players.ItemGroup.Part
                or Domain.Players.ItemGroup.ClubSet) expected++;

        var c = await env.ConnectAsync();
        await using var _c = c;
        await GameEnv.SendLoginAsync(c, acc, key);
        int pages = 0, received = 0;
        while (true)
        {
            var (id, r) = await c.ReceiveAsync();
            if (id == 0x70) break;                                          // equipamento vem depois da lista
            if (id != 0x71) continue;
            int total = r.U16(), n = r.U16();
            Assert.Equal(total, n);
            Assert.InRange(n, 1, 50);
            pages++;
            received += n;
        }
        Assert.Equal(expected, received);
        Assert.True(pages >= 3);
    }
}
