using Pangya.Core.Config;
using Pangya.Domain.Players;

namespace Pangya.Domain.Shop;

/// <summary>Classe do prêmio, como o cliente mostra (0 item de pang, 1 item de cookie, 2 raro).</summary>
public enum PrizeClass : byte { Normal = 0, Cookie = 1, Rare = 2 }

/// <summary>Um prêmio entregue: Id = objeto gravado no banco (o cliente cria/soma o item local com ele).</summary>
public readonly record struct Prize(PrizeClass Class, int TypeId, int Id, int Quantity);

/// <summary>Resultado do Papel Shop: Code 0 ok, 2 sem dinheiro, 3 item inválido; Coupon = cupom gasto (0 = pagou em pang).</summary>
public sealed record PapelResult(uint Code, List<Prize> Prizes, int Coupon = 0);

/// <summary>Resultado da raspadinha: Code 0 ok (Prizes pode ser vazio = não ganhou); Card = cartão gasto.</summary>
public sealed record ScratchResult(uint Code, List<Prize> Prizes, int Card = 0);

/// <summary>
/// Papel Shop (봉다리) e raspadinha (docs/protocolo/SPEC-papel-raspadinha.md). O servidor decide tudo: forma de
/// pagamento (cupom antes de pang), quantas bolas, quais itens. Prêmios são gravados junto com o débito numa
/// transação, e o cliente recebe o id real de cada um (ele mesmo adiciona ao inventário local).
/// </summary>
public sealed class LotteryService(ShopService shop, IGameData data, LotteryConfig cfg, Random? random = null)
{
    public const int PapelCouponKind = 2, ScratchCardKind = 3;
    const int MaxRedraws = 10;
    readonly Random rng = random ?? Random.Shared;

    /// <summary>CItemManager::GetCouponKind (@0x746C40): só usáveis 0x1A……; 0 = não é cupom.</summary>
    public static int CouponKind(int typeId)
    {
        if (Item.GroupOf(typeId) != ItemGroup.Usable || (typeId & 0x2000000) == 0) return 0;
        switch (typeId & 0x1FFFFFF)
        {
            case 0x15 or 0x16 or 0x17 or 0x1D or 0x1E or 0x2B or 0x2C or 0x2D or 0x2E or 0x3A or 0x3C or 0x4E: return 1;
            case 0x28 or 0x29 or 0x2A: return 2;
            case 0x30 or 0x33 or 0x34 or 0xA3: return 3;
            case 0x36 or 0xD2: return 4;
            case >= 0x63 and <= 0x75: return 4;
            case 0x3D or 0x3F or 0x53: return 5;
            default: return 0;
        }
    }

    /// <summary>Primeira pilha de cupom do tipo pedido (menor id, como a loja).</summary>
    static Item? FindCoupon(Player p, int kind)
    {
        Item? best = null;
        foreach (var it in p.Items.Values)
            if (it.Location == ItemLocation.Inventory && PlayerActions.Free(p, it) > 0 && CouponKind(it.TypeId) == kind && (best == null || it.Id < best.Id))
                best = it;
        return best;
    }

    public async Task<PapelResult> PlayPapelAsync(Player p)
    {
        var d = new ShopDraft(p, shop.Store);
        int coupon = 0;
        if (FindCoupon(p, PapelCouponKind) is { } c)
        {
            d.Edit(c).Quantity--;
            coupon = c.Id;
        }
        else if (p.Pang < cfg.PapelPrice) return new PapelResult(2, []);
        else d.Changes.Pang = p.Pang - cfg.PapelPrice;

        int n = Math.Min(1 + Pick(cfg.PapelBallWeights), 5);                // o cliente só aceita 1..5
        var prizes = new List<Prize>(n);
        for (int i = 0; i < n; i++)
        {
            var prize = await DrawAsync(d, cfg.PapelCookiePerMille, cfg.PapelRarePerMille);
            if (prize == null) return new PapelResult(3, []);           // pool vazio ou sem item válido: nada é gravado
            prizes.Add(prize.Value);
        }
        await d.CommitAsync();
        if (coupon == 0) p.Pang -= cfg.PapelPrice;
        return new PapelResult(0, prizes, coupon);
    }

    public async Task<ScratchResult> ScratchAsync(Player p)
    {
        if (FindCoupon(p, ScratchCardKind) is not { } card) return new ScratchResult(1, []);
        var d = new ShopDraft(p, shop.Store);
        d.Edit(card).Quantity--;
        int n = Math.Min(Pick(cfg.ScratchCountWeights), 2);                 // a tela tem 2 espaços
        var prizes = new List<Prize>(n);
        for (int i = 0; i < n; i++)
            if (await DrawAsync(d, cfg.ScratchCookiePerMille, cfg.ScratchRarePerMille) is { } prize) prizes.Add(prize);
        await d.CommitAsync();
        return new ScratchResult(0, prizes, card.Id);
    }

    /// <summary>Sorteia a classe e o item e entrega no rascunho; re-sorteia itens únicos que o jogador já tem.</summary>
    async Task<Prize?> DrawAsync(ShopDraft d, int cookiePerMille, int rarePerMille)
    {
        int roll = rng.Next(1000);
        var cls = roll < rarePerMille ? PrizeClass.Rare : roll < rarePerMille + cookiePerMille ? PrizeClass.Cookie : PrizeClass.Normal;
        for (int attempt = 0; attempt <= MaxRedraws; attempt++)
        {
            var pool = cls switch { PrizeClass.Rare => cfg.Rare, PrizeClass.Cookie => cfg.Cookie, _ => cfg.Normal };
            if (attempt == MaxRedraws || !HasValid(pool)) (cls, pool) = (PrizeClass.Normal, cfg.Normal);   // última tentativa: comum
            if (cls == PrizeClass.Rare && !HasValid(cfg.Rare)) (cls, pool) = (PrizeClass.Cookie, cfg.Cookie);
            var pc = PickPrize(pool);
            if (pc == null) return null;
            bool stacks = Item.GroupOf(pc.TypeId) is ItemGroup.Ball or ItemGroup.Usable;
            int qty = stacks && cls != PrizeClass.Rare ? rng.Next(Math.Max(1, pc.Min), Math.Max(pc.Min, pc.Max) + 1) : 1;
            var granted = new List<Granted>(1);
            var code = await shop.GrantAsync(d, pc.TypeId, qty, 0, granted, 0);
            if (code == ShopCode.AlreadyOwned) continue;
            if (code != ShopCode.Ok || granted.Count != 1) return null;
            return new Prize(cls, pc.TypeId, granted[0].Id, qty);
        }
        return null;
    }

    /// <summary>Grupos que o cliente adiciona sozinho ao inventário e que não pedem prazo nem dono (SPEC §0).</summary>
    bool Allowed(PrizeConfig pc) => pc.Weight > 0 && data.Exists(pc.TypeId)
        && Item.GroupOf(pc.TypeId) is ItemGroup.Part or ItemGroup.ClubSet or ItemGroup.Ball or ItemGroup.Usable;

    bool HasValid(PrizeConfig[] pool)
    {
        foreach (var pc in pool) if (Allowed(pc)) return true;
        return false;
    }

    PrizeConfig? PickPrize(PrizeConfig[] pool)
    {
        int total = 0;
        foreach (var pc in pool) if (Allowed(pc)) total += pc.Weight;
        if (total == 0) return null;
        int r = rng.Next(total);
        foreach (var pc in pool)
        {
            if (!Allowed(pc)) continue;
            if (r < pc.Weight) return pc;
            r -= pc.Weight;
        }
        return null;
    }

    /// <summary>Índice sorteado pelos pesos (0 se todos forem zero).</summary>
    int Pick(int[] weights)
    {
        int total = 0;
        foreach (var w in weights) total += Math.Max(w, 0);
        if (total == 0) return 0;
        int r = rng.Next(total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (r < Math.Max(weights[i], 0)) return i;
            r -= Math.Max(weights[i], 0);
        }
        return 0;
    }
}
