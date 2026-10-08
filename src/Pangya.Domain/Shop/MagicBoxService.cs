using Pangya.Domain.Players;

namespace Pangya.Domain.Shop;

/// <summary>Receita da Caixa Mágica (CadieMagicBox.iff). Index = uiNumber-1; RandomGroup ≠ 0 = resultado sorteado.</summary>
public sealed record MagicBoxRecipe(int Index, int Level, int Output, int OutputCount, int[] Elements, int[] ElementCounts, int RandomGroup);

/// <summary>Uma saída possível de um sorteio (CadieMagicBoxRandom.iff); Weight em milésimos.</summary>
public readonly record struct MagicBoxOutput(int TypeId, int Count, int Weight);

/// <summary>Códigos do 0xED (recycledlg): 1 condição inválida, 2 item não existe, 3 limite de 20.000, 4 já possui.</summary>
public enum MagicBoxCode : uint { Ok = 0, Invalid = 1, NoItem = 2, Limit = 3, AlreadyOwned = 4 }

/// <summary>Material depois da troca: Count = o que sobrou (0 = apagado).</summary>
public readonly record struct Consumed(int TypeId, int Id, int Count);

public sealed record MagicBoxResult(MagicBoxCode Code, List<Consumed> Consumed, List<Granted> Granted, HashSet<int> NewIds)
{
    public static MagicBoxResult Fail(MagicBoxCode code) => new(code, [], [], []);
}

/// <summary>
/// Caixa Mágica da caddie (docs/protocolo/SPEC-tiki-craft.md §1): troca materiais por um item, pelas receitas do IFF.
/// O cliente manda só a receita, a quantidade e {tid, id} de cada material; o servidor confere tudo e decide o
/// sorteio. Materiais e resultado são gravados juntos (ou nada).
/// </summary>
public sealed class MagicBoxService(ShopService shop, IGameData data, Random? random = null)
{
    /// <summary>Limite por pilha que o cliente impõe (ConfirmInsertItem).</summary>
    public const int MaxStack = 20_000;
    readonly Random rng = random ?? Random.Shared;

    /// <summary>Grupos que o resultado pode ter: os que o cliente atualiza pelo 0x71/0xA5 (caddie e anel ainda não).</summary>
    static bool Deliverable(int tid) => Item.GroupOf(tid) is ItemGroup.Part or ItemGroup.ClubSet or ItemGroup.Ball
        or ItemGroup.Usable or ItemGroup.SetItem;

    static bool Stacks(int tid) => Item.GroupOf(tid) is ItemGroup.Ball or ItemGroup.Usable;

    public async Task<MagicBoxResult> ExchangeAsync(Player p, int index, int qty, IReadOnlyList<(int TypeId, int Id)> materials)
    {
        var recipes = data.MagicBox;
        if (index < 0 || index >= recipes.Count) return MagicBoxResult.Fail(MagicBoxCode.NoItem);
        var r = recipes[index];
        bool random = r.RandomGroup != 0;
        if (qty < 1 || ((random || !Stacks(r.Output)) && qty != 1)) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
        if (p.Level < r.Level || materials.Count != r.Elements.Length) return MagicBoxResult.Fail(MagicBoxCode.Invalid);

        // resultado: fixo ou sorteado pelos pesos do grupo
        int outTid = r.Output, outCount = r.OutputCount * qty;
        if (random)
        {
            if (!data.MagicBoxRandom.TryGetValue(r.RandomGroup, out var outs) || Draw(outs) is not { } o)
                return MagicBoxResult.Fail(MagicBoxCode.NoItem);
            (outTid, outCount) = (o.TypeId, Math.Max(o.Count, 1));
        }
        if (!data.Exists(outTid)) return MagicBoxResult.Fail(MagicBoxCode.NoItem);
        if (!Deliverable(outTid)) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
        if (Stacks(outTid) && (p.FindType(outTid)?.Quantity ?? 0) + outCount > MaxStack) return MagicBoxResult.Fail(MagicBoxCode.Limit);

        var d = new ShopDraft(p, shop.Store);
        var consumed = new List<Consumed>();
        for (int i = 0; i < r.Elements.Length; i++)
        {
            var (tid, id) = materials[i];
            if (tid != r.Elements[i]) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
            if (p.Find(id) is not { Location: ItemLocation.Inventory } it || it.TypeId != tid) return MagicBoxResult.Fail(MagicBoxCode.NoItem);
            int need = r.ElementCounts[i] * qty;
            if (Stacks(tid))
            {
                if (tid == Item.BasicBall) return MagicBoxResult.Fail(MagicBoxCode.Invalid);   // a bola básica nunca é gasta
                var e = d.Edit(it);
                if (e.Quantity < need || PlayerActions.Free(p, it) < need) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
                e.Quantity -= need;
                consumed.Add(new Consumed(tid, id, e.Quantity));
                continue;
            }
            // não empilhável (peças): gasta a cópia escolhida e, se a receita pedir mais, outras livres do mesmo tipo
            var copies = new List<Item> { it };
            foreach (var other in p.Items.Values)
                if (copies.Count < need && other.Id != id && other.TypeId == tid && other.Location == ItemLocation.Inventory) copies.Add(other);
            if (copies.Count < need) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
            for (int k = 0; k < need; k++)
            {
                if (PlayerActions.IsBusy(p, copies[k]) || d.IsRemoved(copies[k].Id)) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
                d.Remove(copies[k]);
                consumed.Add(new Consumed(tid, copies[k].Id, 0));
            }
        }

        var granted = new List<Granted>();
        var code = await shop.GrantAsync(d, outTid, outCount, 0, granted, 0);
        if (code == ShopCode.AlreadyOwned) return MagicBoxResult.Fail(MagicBoxCode.AlreadyOwned);
        if (code != ShopCode.Ok) return MagicBoxResult.Fail(MagicBoxCode.Invalid);
        var newIds = new HashSet<int>();
        foreach (var g in granted) if (!p.Items.ContainsKey(g.Id)) newIds.Add(g.Id);
        await d.CommitAsync();
        return new MagicBoxResult(MagicBoxCode.Ok, consumed, granted, newIds);
    }

    MagicBoxOutput? Draw(MagicBoxOutput[] outs)
    {
        int total = 0;
        foreach (var o in outs) total += Math.Max(o.Weight, 0);
        if (total == 0) return null;
        int roll = rng.Next(total);
        foreach (var o in outs)
        {
            if (roll < Math.Max(o.Weight, 0)) return o;
            roll -= Math.Max(o.Weight, 0);
        }
        return null;
    }
}
