using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Caixa Mágica da caddie com as receitas reais do pangya.iff (docs/protocolo/SPEC-tiki-craft.md §1).</summary>
[Collection("db")]
public class MagicBoxTests(DbFixture fx)
{
    const int Coin = 0x1A0000F8, ScratchHelper = 0x1A00003D;

    [Fact]
    public void RecipesLoadFromIff()
    {
        _ = fx;
        var data = Kr645GameData.Load(TestEnv.Config.Data.IffPath);
        Assert.Equal(820, data.MagicBox.Count);
        var r2 = data.MagicBox[1];                                         // #2: 3 moedas -> 1 스크래치카드보조권
        Assert.Equal((ScratchHelper, 1, 0), (r2.Output, r2.OutputCount, r2.RandomGroup));
        Assert.Equal([Coin], r2.Elements);
        Assert.Equal([3], r2.ElementCounts);
        Assert.NotEqual(0, data.MagicBox[2].RandomGroup);                  // #3: sorteio
        foreach (var (_, outs) in data.MagicBoxRandom)
        {
            int sum = 0;
            foreach (var o in outs) sum += o.Weight;
            Assert.Equal(1000, sum);
        }
    }

    static async Task<(TestClient C, long Acc, Player P)> EnterWithAsync(GameEnv env, params (int Tid, int Qty)[] items)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        foreach (var (tid, qty) in items) Assert.Equal(ShopCode.Ok, (await shop.GiveAsync(p, tid, qty)).Code);
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id, p);
    }

    static PacketWriter Request(int index, int qty, params (int Tid, int Id)[] mats)
    {
        var w = new PacketWriter(0x7E).U16((ushort)index).U8((byte)qty).U8((byte)mats.Length);
        foreach (var (tid, id) in mats) w.U32((uint)tid).U32((uint)id);
        return w;
    }

    [Fact]
    public async Task FixedRecipeConsumesMaterialsAndUpdatesClientBeforeResult()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, acc, p) = await EnterWithAsync(env, (Coin, 7));
        await using var _c = c;
        int coin = p.FindType(Coin)!.Id;

        await c.SendAsync(Request(1, 2, (Coin, coin)));                    // 2 trocas = 6 moedas
        var a5 = await c.ExpectAsync(0xA5);
        Assert.Equal((1, (uint)Coin, (uint)coin, 1), (a5.U8(), a5.U32(), a5.U32(), (int)a5.U16()));
        var items = await c.ExpectAsync(0x71);
        Assert.Equal((1, 1), (items.U16(), items.U16()));
        var info = items.Struct<sItemInfo>();
        Assert.Equal(((uint)ScratchHelper, (short)2), (info.tid, info.Common[0]));
        var ed = await c.ExpectAsync(0xED);
        Assert.Equal((0u, 1, 1), (ed.U32(), (int)ed.U16(), (int)ed.U8()));
        Assert.Equal(((uint)ScratchHelper, info.guid, 2), (ed.U32(), ed.U32(), (int)ed.U16()));

        var saved = (await env.Players.LoadAsync(acc))!;
        Assert.Equal(1, saved.FindType(Coin)!.Quantity);
        Assert.Equal(2, saved.FindType(ScratchHelper)!.Quantity);

        await c.SendAsync(Request(1, 1, (Coin, coin)));                    // falta moeda: condição inválida, nada muda
        Assert.Equal(1u, (await c.ExpectAsync(0xED)).U32());
        await c.SendAsync(Request(1, 1, (0x1A0000F9, coin)));               // tid diferente do da receita
        Assert.Equal(1u, (await c.ExpectAsync(0xED)).U32());
        await c.SendAsync(Request(1, 1, (Coin, 12345)));                    // objeto que não é dele
        Assert.Equal(2u, (await c.ExpectAsync(0xED)).U32());
        await c.SendAsync(Request(5000, 1, (Coin, coin)));                  // receita inexistente
        Assert.Equal(2u, (await c.ExpectAsync(0xED)).U32());
        Assert.Equal(1, (await env.Players.LoadAsync(acc))!.FindType(Coin)!.Quantity);
    }

    [Fact]
    public async Task RandomRecipeDrawsFromItsGroupOnlyOnce()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var r = env.Data.MagicBox[2];
        var (acc, _) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        for (int i = 0; i < r.Elements.Length; i++) await shop.GiveAsync(p, r.Elements[i], r.ElementCounts[i] * 2);
        var mats = new (int, int)[r.Elements.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = (r.Elements[i], p.FindType(r.Elements[i])!.Id);
        var box = new MagicBoxService(shop, env.Data, new Random(7));

        Assert.Equal(MagicBoxCode.Invalid, (await box.ExchangeAsync(p, 2, 2, mats)).Code);   // sorteio: só 1 por vez
        var res = await box.ExchangeAsync(p, 2, 1, mats);
        Assert.Equal(MagicBoxCode.Ok, res.Code);
        var outs = env.Data.MagicBoxRandom[r.RandomGroup];
        bool inGroup = false;
        foreach (var o in outs) inGroup |= o.TypeId == res.Granted[0].TypeId;
        Assert.True(inGroup);
        foreach (var e in r.Elements) Assert.Equal(r.ElementCounts[Array.IndexOf(r.Elements, e)], (await env.Players.LoadAsync(acc.Id))!.FindType(e)!.Quantity);
    }

    [Fact]
    public async Task PartRecipeSwapsThePartAndRefusesOwnedOutput()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        int index = -1;
        for (int i = 0; i < env.Data.MagicBox.Count; i++)                   // 1ª receita de peça com uma peça de material
        {
            var x = env.Data.MagicBox[i];
            if (Item.GroupOf(x.Output) == ItemGroup.Part && x.RandomGroup == 0 && x.Level == 0 && x.Elements.Length > 0
                && Item.GroupOf(x.Elements[0]) == ItemGroup.Part && x.ElementCounts[0] == 1 && env.Data.Exists(x.Output)) { index = i; break; }
        }
        Assert.True(index >= 0);
        var r = env.Data.MagicBox[index];
        var (acc, _) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        async Task<(int, int)[]> GiveMaterialsAsync()
        {
            var mats = new (int, int)[r.Elements.Length];
            for (int i = 0; i < r.Elements.Length; i++)
            {
                var (code, g) = await shop.GiveAsync(p, r.Elements[i], r.ElementCounts[i]);
                Assert.Equal(ShopCode.Ok, code);
                mats[i] = (r.Elements[i], g[0].Id);
            }
            return mats;
        }
        var box = new MagicBoxService(shop, env.Data);
        var mats = await GiveMaterialsAsync();
        var res = await box.ExchangeAsync(p, index, 1, mats);
        Assert.Equal(MagicBoxCode.Ok, res.Code);
        Assert.Contains(res.Consumed, c => c.Id == mats[0].Item2 && c.Count == 0);
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Null(saved.Find(mats[0].Item2));                              // a peça de material foi apagada
        Assert.Equal(r.Output, saved.Find(res.Granted[0].Id)!.TypeId);
        Assert.Contains(res.Granted[0].Id, res.NewIds);

        p = saved;
        mats = await GiveMaterialsAsync();
        Assert.Equal(MagicBoxCode.AlreadyOwned, (await box.ExchangeAsync(p, index, 1, mats)).Code);
        Assert.NotNull((await env.Players.LoadAsync(acc.Id))!.Find(mats[0].Item2));   // nada foi gasto
    }
}
