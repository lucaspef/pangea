using System.Text.Json.Nodes;
using Pangya.Domain.Players;

namespace Pangya.Domain.Shop;

/// <summary>Resultado de compra (códigos do cliente KR, shoptask.cpp:107-256).</summary>
public enum ShopCode : uint { Ok = 0, Fail = 1, NoPang = 2, BadCode = 3, AlreadyOwned = 4, NotForSale = 0x13, TooMany = 0x15, NoCookie = 0x17 }

/// <summary>Um item pedido (o cliente não manda preço).</summary>
public readonly record struct BuyRequest(int TypeId, int Days, int Quantity);

/// <summary>O que foi entregue, como o cliente aplica: Id = objeto (ou o caddie, para item de caddie), Count = total atual.</summary>
public readonly record struct Granted(int TypeId, int Id, int Count, int Hours = 0, DateTime? End = null);

/// <summary>
/// Loja: cobra o que CItemManager::GetItemSalePrice (itemmanager.cpp:2833) calcula, confere estoque, saldo e posse,
/// entrega cada tipo de item como o cliente espera (docs/protocolo/SPEC-player-shop.md e SPEC-myroom.md seção 4)
/// e grava tudo numa transação (ou nada, se algo falhar).
/// </summary>
public sealed class ShopService(IPlayerStore store, IGameData data)
{
    public const int MaxLines = 20;
    const int MaxQuantity = 9999;
    static readonly int[] PeriodDays = [1, 7, 15, 30, 365];

    /// <summary>Preço total de uma linha; null = não pode ser vendido assim.</summary>
    public static long? Price(ShopItem s, int count, int days)
    {
        var group = Item.GroupOf(s.TypeId);
        if (group == ItemGroup.Usable)                      // preço do pacote de COM[0] unidades
            return (long)(s.PackSize > 0 ? count / s.PackSize : count) * s.UnitPrice;
        if (group is ItemGroup.CaddieItem or ItemGroup.Skin or ItemGroup.Mascot)
        {
            if (group == ItemGroup.Skin && s.UnitPricedSkin) return s.UnitPrice;
            int idx = Array.IndexOf(PeriodDays, days);
            if (idx < 0 || idx >= s.PeriodPrices.Length) return null;
            if ((group == ItemGroup.CaddieItem && idx == 1) || (group == ItemGroup.Mascot && idx == 2)) return null;
            return s.PeriodPrices[idx] > 0 ? s.PeriodPrices[idx] : null;
        }
        return s.UnitPrice;
    }

    public async Task<(ShopCode Code, List<Granted> Granted)> BuyAsync(Player p, IReadOnlyList<BuyRequest> requests)
    {
        if (requests.Count == 0) return (ShopCode.Fail, []);
        if (requests.Count > MaxLines) return (ShopCode.TooMany, []);
        long pang = 0, cookie = 0;
        foreach (var r in requests)
        {
            var s = data.GetShopItem(r.TypeId);
            if (s == null) return (ShopCode.BadCode, []);
            if (s.InStock is not (1 or 3)) return (ShopCode.NotForSale, []);
            var price = Price(s, Math.Clamp(r.Quantity, 1, MaxQuantity), r.Days);
            if (price is not { } v || v >= ShopItem.NotForSale) return (ShopCode.NotForSale, []);
            if (s.IsCash) cookie += v; else pang += v;
        }
        if (pang > p.Pang) return (ShopCode.NoPang, []);
        if (cookie > p.Cookie) return (ShopCode.NoCookie, []);

        var draft = new Draft(p, store);
        var granted = new List<Granted>();
        foreach (var r in requests)
        {
            int qty = Item.GroupOf(r.TypeId) == ItemGroup.Card ? 1 : Math.Clamp(r.Quantity, 1, MaxQuantity);
            var code = await GrantAsync(draft, r.TypeId, qty, r.Days, granted, 0);
            if (code != ShopCode.Ok) return (code, []);               // nada foi gravado: o rascunho é descartado
        }
        draft.Changes.Pang = p.Pang - pang;
        draft.Changes.Cookie = p.Cookie - cookie;
        await draft.CommitAsync();
        p.Pang -= pang;
        p.Cookie -= cookie;
        return (ShopCode.Ok, granted);
    }

    /// <summary>Horas inteiras que ainda restam de um prazo (0 se já venceu).</summary>
    static int RemainingHours(DateTime? until, DateTime now) =>
        until is { } u && u > now ? (int)Math.Ceiling((u - now).TotalHours - 1e-6) : 0;

    async Task<ShopCode> GrantAsync(Draft d, int tid, int qty, int days, List<Granted> granted, int depth)
    {
        var now = DateTime.UtcNow;
        switch (Item.GroupOf(tid))
        {
            case ItemGroup.SetItem:                                         // set: entrega cada peça
                if (depth > 2 || data.GetShopItem(tid) is not { } set) return ShopCode.Fail;
                foreach (var (etid, n) in set.SetElements)
                {
                    var c = await GrantAsync(d, etid, n, days, granted, depth + 1);
                    if (c != ShopCode.Ok) return c;
                }
                return ShopCode.Ok;
            case ItemGroup.Character:                                       // personagem com as partes padrão; o cliente já equipa
            {
                if (d.FindType(tid) != null) return ShopCode.AlreadyOwned;
                var it = await d.AddAsync(tid, 1);
                it.Set("parts", data.DefaultParts(tid));
                d.Equip.CharacterId = it.Id;
                granted.Add(new Granted(tid, it.Id, 0));
                return ShopCode.Ok;
            }
            case ItemGroup.Caddie:
            {
                if (d.FindType(tid) != null) return ShopCode.AlreadyOwned;
                var it = await d.AddAsync(tid, 1);
                granted.Add(new Granted(tid, it.Id, 0));
                return ShopCode.Ok;
            }
            case ItemGroup.CaddieItem:                                      // peça do caddie: o registro leva o id do CADDIE
            {
                var cad = d.FindType(0x1C000000 | ((tid >> 21) & 0x1F));
                if (cad == null) return ShopCode.Fail;
                cad = d.Edit(cad);
                int hours = Math.Max(days, 1) * 24 + (cad.Int("part") == tid ? RemainingHours(cad.Attrs["part_until"]?.GetValue<DateTime>(), now) : 0);
                hours = Math.Min(hours, 0xFFFF);
                cad.Set("part", tid);
                cad.Attrs["part_until"] = now.AddHours(hours);
                granted.Add(new Granted(tid, cad.Id, 1, Hours: hours));
                return ShopCode.Ok;
            }
            case ItemGroup.HairColor:                                       // cor de cabelo do 1º personagem compatível
            {
                if (data.GetShopItem(tid) is not { } hair) return ShopCode.Fail;
                Item? ch = null;
                foreach (var c in d.OfGroup(ItemGroup.Character))
                    if ((c.TypeId & 0x3FFFFFF) == hair.HairCharacter) { ch = c; break; }
                if (ch == null) return ShopCode.Fail;
                if (ch.Int("hair") == hair.HairColor) return ShopCode.AlreadyOwned;
                ch = d.Edit(ch);
                ch.Set("hair", hair.HairColor);
                granted.Add(new Granted(tid, ch.Id, 1));
                return ShopCode.Ok;
            }
            case ItemGroup.Mascot:                                          // mascote: comprar de novo estende a validade
            {
                var m = d.FindType(tid);
                if (m == null) { m = await d.AddAsync(tid, 1); m.Attrs["msg"] = "PANGYA!"; }
                else m = d.Edit(m);
                int hours = Math.Min(Math.Max(days, 1) * 24 + RemainingHours(m.ExpiresAt, now), 0xFFFF);
                m.ExpiresAt = now.AddHours(hours);
                m.Attrs["msg"] = "PANGYA!";                                 // o cliente também volta a mensagem ao comprar
                granted.Add(new Granted(tid, m.Id, 1, hours, m.ExpiresAt));
                return ShopCode.Ok;
            }
            case ItemGroup.Furniture:                                       // móvel: posição inicial do IFF, não arrumado
            {
                var pos = data.GetShopItem(tid)?.Position ?? [];
                var it = await d.AddAsync(tid, 1);
                if (pos.Length == 4)
                {
                    it.Attrs["x"] = pos[0]; it.Attrs["y"] = pos[1]; it.Attrs["z"] = pos[2]; it.Attrs["r"] = pos[3];
                }
                granted.Add(new Granted(tid, it.Id, 1));
                return ShopCode.Ok;
            }
            case ItemGroup.Card:                                            // card/pacote: uma pilha por typeid
            {
                var c = d.FindType(tid);
                c = c == null ? await d.AddAsync(tid, 0) : d.Edit(c);
                c.Quantity += Math.Max(qty, 1);
                granted.Add(new Granted(tid, c.Id, c.Quantity));
                return ShopCode.Ok;
            }
            case (ItemGroup)0x1C:
                return ShopCode.Fail;                                       // grupo não vendido no KR 645
            case ItemGroup.Ball or ItemGroup.Usable:                        // empilha; Count = total novo
            {
                var it = d.FindType(tid);
                it = it == null ? await d.AddAsync(tid, 0) : d.Edit(it);
                it.Quantity = Math.Min(it.Quantity + qty, short.MaxValue);
                granted.Add(new Granted(tid, it.Id, it.Quantity));
                return ShopCode.Ok;
            }
            default:
            {
                if (d.FindType(tid) != null) return ShopCode.AlreadyOwned;
                var it = await d.AddAsync(tid, 1);
                granted.Add(new Granted(tid, it.Id, 1));
                return ShopCode.Ok;
            }
        }
    }

    /// <summary>
    /// Rascunho de mudanças sobre o jogador: os itens alterados são cópias até o commit, então um erro no meio
    /// da compra não deixa o jogador pela metade.
    /// </summary>
    sealed class Draft(Player p, IPlayerStore store)
    {
        readonly Dictionary<int, Item> edited = [];
        readonly HashSet<int> added = [];
        public PlayerChanges Changes { get; } = new();
        public Equipment Equip { get; } = Copy(p.Equip);

        static Equipment Copy(Equipment e) => new()
        {
            CharacterId = e.CharacterId, CaddieId = e.CaddieId, ClubSetId = e.ClubSetId, BallTypeId = e.BallTypeId,
            MascotId = e.MascotId, ItemSlots = (int[])e.ItemSlots.Clone(), SkinTypeIds = (int[])e.SkinTypeIds.Clone(),
        };

        Item Current(Item it) => edited.GetValueOrDefault(it.Id) ?? it;

        public Item? FindType(int tid)
        {
            foreach (var it in edited.Values)
                if (it.TypeId == tid && it.Location == ItemLocation.Inventory) return it;
            var found = p.FindType(tid);
            return found == null ? null : Current(found);
        }

        public List<Item> OfGroup(ItemGroup g)
        {
            var list = p.OfGroup(g);
            for (int i = 0; i < list.Count; i++) list[i] = Current(list[i]);
            foreach (var id in added)
                if (edited[id].Group == g) list.Add(edited[id]);
            return list;
        }

        public Item Edit(Item it)
        {
            if (edited.TryGetValue(it.Id, out var e)) return e;
            var c = it.Clone();
            edited[it.Id] = c;
            return c;
        }

        public async Task<Item> AddAsync(int tid, int quantity)
        {
            var ids = await store.NewIdsAsync(1);
            var it = new Item { Id = ids[0], TypeId = tid, Quantity = quantity };
            edited[it.Id] = it;
            added.Add(it.Id);
            return it;
        }

        public async Task CommitAsync()
        {
            foreach (var (id, it) in edited)
                (added.Contains(id) ? Changes.Added : Changes.Updated).Add(it);
            Changes.Equip = Equip;
            await store.ApplyAsync(p.AccountId, Changes);
            foreach (var it in edited.Values) p.Items[it.Id] = it;      // só depois de gravado
            p.Equip = Equip;
        }
    }
}
