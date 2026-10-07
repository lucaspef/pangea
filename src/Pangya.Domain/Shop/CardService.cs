using Pangya.Domain.Players;

namespace Pangya.Domain.Shop;

/// <summary>Um card ativo/encaixado (linha de items com location = ActiveCard).</summary>
public readonly record struct ActiveCard(int Id, int TypeId, int PartTypeId, int PartId, int Slot, DateTime? Start, DateTime? End);

/// <summary>
/// Cards (docs/protocolo/SPEC-myroom.md seção 7): abrir pacote/caixa, usar card especial, encaixar e remover.
/// Pilhas de cards são itens (quantidade = pilha); cards encaixados/ativos são itens com location = ActiveCard.
/// </summary>
public sealed class CardService(IPlayerStore store, IGameData data, Random? random = null)
{
    readonly Random rng = random ?? Random.Shared;
    static readonly int[] Removers = [0x1A0000C2, 0x1A0000CD, 0x1A0000CE, 0x1A0000CF];
    /// <summary>Peso de sorteio por raridade (normal, raro, super raro, secreto) [suposição: chances reais desconhecidas].</summary>
    static readonly int[] RarityWeight = [70, 22, 7, 1];

    public static ActiveCard ToActive(Item it) => new(it.Id, it.TypeId, it.Int("parts_tid"), it.Int("parts_id"), it.Int("slot"),
        it.Attrs["start"]?.GetValue<DateTime>(), it.ExpiresAt);

    /// <summary>Abre pacote/caixa: tira 1 e sorteia 3 cards da série (tickets 0x7CC00001-03: 1 card de raridade mínima).</summary>
    public async Task<(Item Pack, int PackCountBefore, List<Item> Drawn)?> OpenPackAsync(Player p, int packTid, int packId)
    {
        var pack = p.Find(packId) is { Location: ItemLocation.Inventory } byId && byId.TypeId == packTid ? byId : p.FindType(packTid);
        if (pack == null || pack.Quantity < 1 || ((packTid >> 22) & 0xF) is not (CardInfo.SubPack or CardInfo.SubBox)) return null;
        var drawn = Draw(pack.TypeId);
        if (drawn.Count == 0) return null;
        var ch = new PlayerChanges();
        int before = pack.Quantity;
        var packEdit = pack.Clone();
        packEdit.Quantity--;
        if (packEdit.Quantity == 0) ch.Removed.Add(pack.Id); else ch.Updated.Add(packEdit);
        var stacks = new Dictionary<int, Item>();
        var result = new List<Item>(drawn.Count);
        foreach (var tid in drawn)
        {
            if (!stacks.TryGetValue(tid, out var st))
            {
                var existing = p.FindType(tid);
                if (existing != null) { st = existing.Clone(); ch.Updated.Add(st); }
                else { st = new Item { Id = (await store.NewIdsAsync(1))[0], TypeId = tid, Quantity = 0 }; ch.Added.Add(st); }
                stacks[tid] = st;
            }
            st.Quantity++;
            result.Add(st);
        }
        await store.ApplyAsync(p.AccountId, ch);
        if (packEdit.Quantity == 0) p.Items.Remove(pack.Id); else p.Items[pack.Id] = packEdit;
        foreach (var st in stacks.Values) p.Items[st.Id] = st;
        return (packEdit, before, result);
    }

    List<int> Draw(int packTid)
    {
        var pool = new List<int>();
        var weights = new List<int>();
        int n = 3, minRare = -1, vol;
        var cards = data.Cards;
        if (packTid is 0x7CC00001 or 0x7CC00002 or 0x7CC00003)
        {
            minRare = packTid == 0x7CC00001 ? 2 : packTid == 0x7CC00002 ? 1 : 0;
            n = 1;
            vol = -1;
        }
        else vol = cards.TryGetValue(packTid, out var info) && info.Volume > 0 ? info.Volume : (packTid & 0xFF) == 4 ? 2 : 1;
        foreach (var c in cards.Values)
        {
            if (c.SubType >= CardInfo.SubPack || !c.Final || c.Volume == 0) continue;
            if (vol >= 0 ? c.Volume != vol : c.Rarity < minRare) continue;
            pool.Add(c.TypeId);
            weights.Add(c.Rarity is >= 0 and < 4 ? RarityWeight[c.Rarity] : 1);
        }
        var drawn = new List<int>(n);
        if (pool.Count == 0) return drawn;
        int total = 0;
        foreach (var w in weights) total += w;
        for (int k = 0; k < n; k++)
        {
            int roll = rng.Next(total);
            for (int i = 0; i < pool.Count; i++)
                if ((roll -= weights[i]) < 0) { drawn.Add(pool[i]); break; }
        }
        return drawn;
    }

    /// <summary>
    /// Usa card especial: EXP/pang na hora; os outros viram efeito com prazo (UseTime minutos [suposição]),
    /// substituindo um efeito ativo da mesma habilidade. Devolve o card ativo (Id = pilha) e o pang ganho.
    /// </summary>
    public async Task<(ActiveCard Card, long PangGained, int LevelsUp)?> UseSpecialAsync(Player p, int cardTid)
    {
        if (p.FindType(cardTid) is not { Quantity: > 0 } stack || !data.Cards.TryGetValue(cardTid, out var info) || info.SubType != CardInfo.SubSpecial)
            return null;
        var ch = new PlayerChanges();
        var st = stack.Clone();
        st.Quantity--;
        if (st.Quantity == 0) ch.Removed.Add(st.Id); else ch.Updated.Add(st);
        var now = DateTime.UtcNow;
        long pang = 0;
        int levels = 0;
        Item? active = null;
        var removedActive = new List<int>();
        if (info.Ability == CardInfo.AbilityExp)
        {
            levels = Levels.AddExp(p, info.AbilityValue);
            (ch.Level, ch.Exp) = (p.Level, p.Exp);
        }
        else if (info.Ability is CardInfo.AbilityPang or CardInfo.AbilityRandomPang)
        {
            pang = info.Ability == CardInfo.AbilityPang ? info.AbilityValue : rng.Next(1, Math.Max(info.AbilityValue, 1) + 1);
            ch.Pang = p.Pang + pang;
        }
        else
        {
            foreach (var it in p.Items.Values)                     // mesma habilidade ativa: é substituída
                if (it.Location == ItemLocation.ActiveCard && it.Int("parts_id") == 0 && data.Cards.TryGetValue(it.TypeId, out var ai) && ai.Ability == info.Ability)
                {
                    ch.Removed.Add(it.Id);
                    removedActive.Add(it.Id);
                }
            active = new Item { Id = (await store.NewIdsAsync(1))[0], TypeId = cardTid, Location = ItemLocation.ActiveCard, ExpiresAt = now.AddMinutes(info.UseMinutes > 0 ? info.UseMinutes : 60) };
            active.Attrs["start"] = now;
            ch.Added.Add(active);
        }
        await store.ApplyAsync(p.AccountId, ch);
        if (st.Quantity == 0) p.Items.Remove(st.Id); else p.Items[st.Id] = st;
        foreach (var id in removedActive) p.Items.Remove(id);
        if (active != null) p.Add(active);
        p.Pang += pang;
        return (new ActiveCard(st.Id, cardTid, 0, 0, 0, now, active?.ExpiresAt), pang, levels);
    }

    /// <summary>
    /// Encaixa card de personagem (slot 1-4) ou de caddie (5-8, o cliente subtrai 4) num personagem/caddie/peça do jogador.
    /// Substitui o card que estava no mesmo slot.
    /// </summary>
    public async Task<ActiveCard?> AttachAsync(Player p, int cardId, int cardTid, int partTid, int partId, int slot)
    {
        var stack = p.Find(cardId) is { Location: ItemLocation.Inventory } s && s.TypeId == cardTid ? s : p.FindType(cardTid);
        int sub = (cardTid >> 22) & 0xF;
        if (stack is not { Quantity: > 0 } || sub is not (CardInfo.SubCharacter or CardInfo.SubCaddie) || slot is < 1 or > 8) return null;
        // a peça é um personagem, caddie ou parte do jogador (parte padrão: o id é o do personagem)
        if (p.Find(partId) is not { Location: ItemLocation.Inventory }) return null;
        int nslot = sub == CardInfo.SubCaddie && slot > 4 ? slot - 4 : slot;
        var ch = new PlayerChanges();
        var st = stack.Clone();
        st.Quantity--;
        if (st.Quantity == 0) ch.Removed.Add(st.Id); else ch.Updated.Add(st);
        var replaced = new List<int>();
        foreach (var it in p.Items.Values)
            if (it.Location == ItemLocation.ActiveCard && it.Int("parts_id") == partId && it.Int("slot") == nslot && ((it.TypeId >> 22) & 0xF) == sub)
            {
                ch.Removed.Add(it.Id);
                replaced.Add(it.Id);
            }
        var active = new Item { Id = (await store.NewIdsAsync(1))[0], TypeId = cardTid, Location = ItemLocation.ActiveCard };
        active.Set("parts_tid", partTid);
        active.Set("parts_id", partId);
        active.Set("slot", nslot);
        ch.Added.Add(active);
        await store.ApplyAsync(p.AccountId, ch);
        if (st.Quantity == 0) p.Items.Remove(st.Id); else p.Items[st.Id] = st;
        foreach (var id in replaced) p.Items.Remove(id);
        p.Add(active);
        return new ActiveCard(st.Id, cardTid, partTid, partId, slot, null, null);
    }

    /// <summary>Remove (destrói) os cards encaixados numa peça, gastando 1 removedor. false = recusado.</summary>
    public async Task<bool> RemoveAsync(Player p, int removerTid, int removerId, int partId)
    {
        if (Array.IndexOf(Removers, removerTid) < 0 || partId == 0) return false;
        var rem = p.Find(removerId) is { Location: ItemLocation.Inventory } r && r.TypeId == removerTid ? r : p.FindType(removerTid);
        if (rem is not { Quantity: > 0 }) return false;
        var ch = new PlayerChanges();
        var re = rem.Clone();
        re.Quantity--;
        if (re.Quantity == 0) ch.Removed.Add(re.Id); else ch.Updated.Add(re);
        var removed = new List<int>();
        foreach (var it in p.Items.Values)
            if (it.Location == ItemLocation.ActiveCard && it.Int("parts_id") == partId) { ch.Removed.Add(it.Id); removed.Add(it.Id); }
        await store.ApplyAsync(p.AccountId, ch);
        if (re.Quantity == 0) p.Items.Remove(re.Id); else p.Items[re.Id] = re;
        foreach (var id in removed) p.Items.Remove(id);
        return true;
    }
}
