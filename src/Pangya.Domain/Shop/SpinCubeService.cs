using Pangya.Domain.Mail;
using Pangya.Domain.Players;

namespace Pangya.Domain.Shop;

/// <summary>Resultado de abrir caixa (0x1A2): Code 0 ok, 4 não é caixa que abre, 8 falta cubo/chave.</summary>
public sealed record SpinCubeResult(uint Code, List<Consumed> Consumed, int PrizeTypeId = 0, int PrizeQty = 0, Granted? Item = null,
    bool NewItem = false, bool Mailed = false)
{
    public static SpinCubeResult Fail(uint code) => new(code, []);
}

/// <summary>
/// Spin Cube (스핀 큐브) do Wiz City: as caixas do campo dão cubos; no My Room, 1 cubo + 1 Lucky Key abrem um prêmio
/// (C->S 0xF1 -> S->C 0x1A2; docs/protocolo/SPEC-caixas-reciclagem.md e o fielditems.py do emulador). Precisa do
/// pangya.iff com o RandomBox no Item.iff e a chave 0x1A00015C (projectg_zzzzitems.pak).
/// Com correio (<paramref name="mail"/>), o prêmio chega numa carta do sistema; sem ele, direto no inventário.
/// </summary>
public sealed class SpinCubeService(ShopService shop, IGameData data, Random? random = null, MailService? mail = null)
{
    public const int SpinCube = 0x1A00015B, LuckyKey = 0x1A00015C, MaxCubes = 50;
    public const string MailSender = "@Pangya", MailMessage = "Premio do Spin Cube";   // ASCII: o cliente lê CP949
    /// <summary>"Bolsa de pang": no 0x1A2 o prêmio em pang vai com este typeid e a quantidade = pang.</summary>
    public const int PangPouch = 0x1A000010;
    public const uint Ok = 0, NotABox = 4, Missing = 8;

    /// <summary>
    /// Prêmios (typeid, quantidade, peso) [suposição do emulador, baseada na caixa 7 do servidor S9: bolsa de pang
    /// 500, consumíveis 300-400, raros 100].
    /// </summary>
    public static readonly (int TypeId, int Qty, int Weight)[] Prizes =
    [
        (PangPouch, 3000, 300), (PangPouch, 10000, 150), (PangPouch, 30000, 50),
        (0x18000002, 3, 150), (0x18000003, 3, 150), (0x18000004, 3, 150), (0x18000005, 2, 100),
        (0x18000027, 2, 100), (0x18000009, 5, 100), (0x1A000011, 3, 100), (LuckyKey, 1, 100),
    ];

    readonly Random rng = random ?? Random.Shared;

    public async Task<SpinCubeResult> OpenAsync(Player p, int boxTid)
    {
        if (boxTid != SpinCube) return SpinCubeResult.Fail(NotABox);
        var cube = p.FindType(SpinCube);
        var key = p.FindType(LuckyKey);
        if (cube is not { Quantity: > 0 } || key is not { Quantity: > 0 } || PlayerActions.Free(p, cube) == 0 || PlayerActions.Free(p, key) == 0)
            return SpinCubeResult.Fail(Missing);
        if (mail != null) return await OpenToMailAsync(p, cube, key);

        var d = new ShopDraft(p, shop.Store);
        var c = d.Edit(cube);
        var k = d.Edit(key);
        c.Quantity--;
        k.Quantity--;
        var consumed = new List<Consumed> { new(SpinCube, c.Id, c.Quantity), new(LuckyKey, k.Id, k.Quantity) };
        var (tid, qty) = Draw();
        Granted? item = null;
        bool isNew = false;
        if (tid == PangPouch) d.Changes.Pang = p.Pang + qty;
        else
        {
            if (!data.Exists(tid)) return SpinCubeResult.Fail(NotABox);
            var granted = new List<Granted>(1);
            if (await shop.GrantAsync(d, tid, qty, 0, granted, 0) != ShopCode.Ok || granted.Count != 1) return SpinCubeResult.Fail(NotABox);
            item = granted[0];
            isNew = !p.Items.ContainsKey(item.Value.Id);
        }
        await d.CommitAsync();
        if (tid == PangPouch) p.Pang += qty;
        return new SpinCubeResult(Ok, consumed, tid, qty, item, isNew);
    }

    /// <summary>
    /// Como no KR original: o prêmio vai numa carta do sistema e o cubo e a chave saem na mesma transação (o cliente avisa
    /// "상품이 우편으로 전달되었습니다" e pede o 0x15E logo depois do 0x1A2).
    /// </summary>
    async Task<SpinCubeResult> OpenToMailAsync(Player p, Item cube, Item key)
    {
        var (tid, qty) = Draw();
        if (tid != PangPouch && !data.Exists(tid)) return SpinCubeResult.Fail(NotABox);
        var ch = new PlayerChanges();
        var consumed = new List<Consumed>(2);
        var after = new List<Item>(2);
        foreach (var it in new[] { cube, key })
        {
            var x = it.Clone();
            x.Quantity--;
            if (x.Quantity == 0) ch.Removed.Add(x.Id); else ch.Updated.Add(x);
            consumed.Add(new Consumed(x.TypeId, x.Id, x.Quantity));
            after.Add(x);
        }
        await mail!.SendSystemAsync(p.AccountId, MailSender, MailMessage, [(tid, qty)], ch);
        foreach (var x in after) if (x.Quantity == 0) p.Items.Remove(x.Id); else p.Items[x.Id] = x;
        return new SpinCubeResult(Ok, consumed, tid, qty, Mailed: true);
    }

    (int TypeId, int Qty) Draw()
    {
        int total = 0;
        foreach (var x in Prizes) total += x.Weight;
        int r = rng.Next(total);
        foreach (var x in Prizes)
        {
            if (r < x.Weight) return (x.TypeId, x.Qty);
            r -= x.Weight;
        }
        return (Prizes[0].TypeId, Prizes[0].Qty);
    }
}
