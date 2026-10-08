using Pangya.Domain.Players;

namespace Pangya.Domain.Rooms;

/// <summary>Códigos do cliente para a loja pessoal (eResultTrade, FrTradeWarningDlg; SPEC-lounge-loja.md).</summary>
public enum TradeCode : uint
{
    Error = 0, Ok = 1, AlreadyOpen = 2, Closed = 3, Editing = 4, NoTitle = 5, BadTitle = 6, NoItems = 8, NotYours = 9,
    BadQuantity = 10, BadPrice = 11, PriceTooHigh = 12, SoldAlready = 13, TooManyVisitors = 14, NoPang = 21, LevelLimit = 23,
    TooManyItems = 24, NotInLounge = 27, RoomLimit = 28, NoPackage = 29,
}

public enum TradeState { None = 0, Open = 1, Editing = 2, SoldOut = 3 }

/// <summary>Um item à venda: índice na loja, objeto do dono, quantidade à venda e preço unitário (o servidor decide).</summary>
public sealed class TradeItem
{
    public int Index { get; init; }
    public int TypeId { get; init; }
    public int ItemId { get; init; }
    public int Quantity { get; set; }
    public long Price { get; init; }
}

/// <summary>Loja de um jogador no lounge. Os itens continuam no inventário do dono até serem vendidos.</summary>
public sealed class PersonalShop(RoomPlayer owner)
{
    public RoomPlayer Owner { get; } = owner;
    public string Title { get; set; } = "";
    public TradeState State { get; set; } = TradeState.Editing;
    public uint SaleType { get; set; } = 1;
    public List<TradeItem> Items { get; } = [];
    public HashSet<uint> Visitors { get; } = [];
    public long Income { get; set; }
    /// <summary>Total de visitas desde que abriu (0x7A).</summary>
    public int VisitCount { get; set; }
}

/// <summary>
/// Regras da loja pessoal (cliente KR + GB): só no lounge, até 6 itens, preço 1..30.000.000, só itens do dono, fora de
/// uso e marcados como vendáveis no IFF; até 15 visitantes; lojas por sala até 80 % das vagas. Sem taxa (o cliente KR
/// não cobra). Tudo sob o lock da sala; a compra grava as duas contas numa transação.
/// </summary>
public static class PersonalShopRules
{
    public const int MaxItems = 6, MaxVisitors = 15, TitleMaxBytes = 31;
    public const long MaxPrice = 30_000_000;
    /// <summary>Bits do estado do avatar (sSlotInfo.state): loja aberta / esgotada.</summary>
    public const uint StateShopOpen = 0x80, StateShopSoldOut = 0x100;

    public static bool Tradable(ItemGroup g) => g is ItemGroup.Part or ItemGroup.ClubSet or ItemGroup.Ball or ItemGroup.Usable;

    /// <summary>Título: 1..31 bytes cp949, não só espaços.</summary>
    public static TradeCode CheckTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title.Length == 0 ? TradeCode.NoTitle : TradeCode.BadTitle;
        return Core.Text.Cp949.Encoding.GetByteCount(title) > TitleMaxBytes ? TradeCode.BadTitle : TradeCode.Ok;
    }

    /// <summary>Confere os itens que o dono quer pôr à venda contra o inventário dele (ids, quantidades, uso, preço).</summary>
    public static TradeCode CheckItems(Player p, IReadOnlyList<TradeItem> items, IGameData data)
    {
        if (items.Count == 0) return TradeCode.NoItems;
        if (items.Count > MaxItems) return TradeCode.TooManyItems;
        var used = new Dictionary<int, int>();
        foreach (var t in items)
        {
            if (p.Find(t.ItemId) is not { Location: ItemLocation.Inventory } it || it.TypeId != t.TypeId) return TradeCode.NotYours;
            if (!Tradable(it.Group) || !data.CanTrade(it.TypeId) || it.TypeId == Item.BasicBall || PlayerActions.IsEquipped(p, it))
                return TradeCode.NotYours;
            int have = it.IsConsumable ? it.Quantity : 1;
            used[it.Id] = used.GetValueOrDefault(it.Id) + t.Quantity;
            if (t.Quantity < 1 || used[it.Id] > have) return TradeCode.BadQuantity;
            if (t.Price < 1) return TradeCode.BadPrice;
            if (t.Price > MaxPrice) return TradeCode.PriceTooHigh;
        }
        return TradeCode.Ok;
    }

    /// <summary>
    /// Monta as mudanças de uma compra de `qty` do item (empilháveis somam na pilha do comprador; o resto vira um objeto
    /// novo do comprador). newId = id reservado para o objeto novo. Devolve null se o dono não tem mais o item.
    /// </summary>
    public static (PlayerChanges Seller, PlayerChanges Buyer, Item Bought, bool NewStack)? Transfer(Player seller, Player buyer,
        TradeItem t, int qty, int newId)
    {
        if (seller.Find(t.ItemId) is not { Location: ItemLocation.Inventory } src || src.TypeId != t.TypeId) return null;
        long total = t.Price * qty;
        var s = new PlayerChanges { Pang = seller.Pang + total };
        var b = new PlayerChanges { Pang = buyer.Pang - total };
        Item bought;
        bool fresh;
        if (src.IsConsumable)
        {
            if (src.Quantity < qty) return null;
            var left = src.Clone();
            left.Quantity -= qty;
            if (left.Quantity == 0) s.Removed.Add(src.Id); else s.Updated.Add(left);
            var mine = buyer.FindType(src.TypeId);
            fresh = mine == null;
            bought = mine?.Clone() ?? new Item { Id = newId, TypeId = src.TypeId, Quantity = 0 };
            bought.Quantity = Math.Min(bought.Quantity + qty, short.MaxValue);
            (fresh ? b.Added : b.Updated).Add(bought);
        }
        else
        {
            if (qty != 1) return null;
            s.Removed.Add(src.Id);
            bought = new Item { Id = newId, TypeId = src.TypeId, Quantity = 1, Attrs = src.Clone().Attrs, ExpiresAt = src.ExpiresAt };
            fresh = true;
            b.Added.Add(bought);
        }
        return (s, b, bought, fresh);
    }

    /// <summary>Aplica em memória o que a transação gravou.</summary>
    public static void Commit(Player seller, Player buyer, PlayerChanges s, PlayerChanges b)
    {
        foreach (var id in s.Removed) seller.Items.Remove(id);
        foreach (var it in s.Updated) seller.Items[it.Id] = it;
        foreach (var it in b.Added) buyer.Items[it.Id] = it;
        foreach (var it in b.Updated) buyer.Items[it.Id] = it;
        seller.Pang = s.Pang ?? seller.Pang;
        buyer.Pang = b.Pang ?? buyer.Pang;
    }
}
