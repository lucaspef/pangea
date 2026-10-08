using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Loja pessoal do lounge (docs/protocolo/SPEC-lounge-loja.md; cliente KR: ids C->S 0x74..0x7D, S->C 0xE1..0xEB).
/// O servidor confere tudo de novo (dono, item fora de uso, vendável no IFF, quantidade, preço, pang, visitante) e usa
/// sempre o preço da loja. Respostas de erro: o mesmo id com u32 código (TradeCode).
/// </summary>
public sealed partial class GameHandler
{
    const ushort CTradeOpen = 0x74, CTradeClose = 0x75, CTradeEdit = 0x76, CTradeEnter = 0x77, CTradeExit = 0x78, CTradeTitle = 0x79,
        CTradeVisitors = 0x7A, CTradeIncome = 0x7B, CTradeFinishEdit = 0x7C, CTradeBuy = 0x7D, CTradeBuyPackage = 0x11A;
    const ushort STradeOpened = 0xE1, STradeClosed = 0xE2, STradeEditing = 0xE3, STradeEntered = 0xE4, STradeExited = 0xE5,
        STradeTitle = 0xE6, STradeVisitors = 0xE7, STradeIncome = 0xE8, STradePublished = 0xE9, STradeSold = 0xEA, STradeItemLeft = 0xEB;
    const int NickField = 22;

    async ValueTask<bool> HandleTradeAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CTradeEdit: ShopEdit(); return true;
            case CTradeTitle: ShopTitle(p.Str(64)); return true;
            case CTradeFinishEdit: ShopFinishEdit(p); return true;
            case CTradeOpen: ShopOpen(); return true;
            case CTradeClose: lock (Rooms.Sync) { CloseMyShopLocked(); LeaveVisitedShopLocked(); } return true;
            case CTradeEnter: ShopEnter(p.U32()); return true;
            case CTradeExit:
                p.Skip(p.Remaining);
                lock (Rooms.Sync) LeaveVisitedShopLocked();
                conn.Send(new PacketWriter(STradeExited).U32((uint)TradeCode.Ok));
                return true;
            case CTradeVisitors:
                lock (Rooms.Sync) conn.Send(new PacketWriter(STradeVisitors).U32((uint)TradeCode.Ok).U32((uint)(MyShop()?.VisitCount ?? 0)));
                return true;
            case CTradeIncome:
                lock (Rooms.Sync) conn.Send(new PacketWriter(STradeIncome).U32((uint)TradeCode.Ok).U64((ulong)(MyShop()?.Income ?? 0)));
                return true;
            case CTradeBuy: await ShopBuyAsync(p); return true;
            case CTradeBuyPackage:                                                   // venda em pacote não é aberta aqui
                p.Skip(p.Remaining);
                conn.Send(new PacketWriter(STradeSold).U32((uint)TradeCode.NoPackage));
                return true;
            default: return false;
        }
    }

    PersonalShop? MyShop() => room?.Shops.GetValueOrDefault((uint)Player.AccountId);

    /// <summary>Loja que estou visitando (dono), se houver.</summary>
    uint visitingShop;

    static sTradeItem TradeStruct(TradeItem t, int qty) => new()
    {
        iIndex = t.Index, dwTid = (uint)t.TypeId, dwGuid = (uint)t.ItemId, iNum = qty, i64Price = t.Price,
    };

    static PacketWriter Items(PacketWriter w, PersonalShop s)
    {
        w.U32((uint)s.Items.Count);
        foreach (var t in s.Items) w.Struct(TradeStruct(t, t.Quantity));
        return w;
    }

    /// <summary>Atualiza o estado/título do avatar do dono (sSlotInfo) e avisa a sala (0xC2 sub 8).</summary>
    static void SetShopState(Room r, RoomPlayer owner, TradeState state)
    {
        uint st = owner.State & ~(PersonalShopRules.StateShopOpen | PersonalShopRules.StateShopSoldOut);
        if (state == TradeState.Open) st |= PersonalShopRules.StateShopOpen;
        else if (state == TradeState.SoldOut) st |= PersonalShopRules.StateShopSoldOut;
        owner.State = st;
        InGameOutput.Broadcast(r, new PacketWriter(SAvatarAction).U32(owner.Guid).U8(ActState).U32(st), except: owner.Session as GameHandler);
    }

    TradeCode CanUseShopLocked(out Room r, out RoomPlayer me)
    {
        r = room!;
        me = null!;
        if (!IsLounge(room) || room!.Find(this) is not { } rp) return TradeCode.NotInLounge;
        me = rp;
        return TradeCode.Ok;
    }

    /// <summary>0x76: criar a loja ou voltar a editá-la. 0xE3 u32 1, str nick, u32 uid para todo o lounge.</summary>
    void ShopEdit()
    {
        lock (Rooms.Sync)
        {
            var code = CanUseShopLocked(out var r, out var me);
            if (code == TradeCode.Ok && Player.Level < 1) code = TradeCode.LevelLimit;
            var shop = code == TradeCode.Ok ? r.Shops.GetValueOrDefault(me.Guid) : null;
            if (code == TradeCode.Ok && shop == null && r.Shops.Count + 1 > Math.Max(1, r.Settings.MaxPlayers * 8 / 10)) code = TradeCode.RoomLimit;
            if (code != TradeCode.Ok) { conn.Send(new PacketWriter(STradeEditing).U32((uint)code)); return; }
            if (shop == null) r.Shops[me.Guid] = shop = new PersonalShop(me);
            shop.State = TradeState.Editing;
            foreach (var v in shop.Visitors)                                    // visitantes saem (o cliente avisa "em edição")
                if (r.Find(v)?.Session is GameHandler h) h.visitingShop = 0;
            shop.Visitors.Clear();
            SetShopState(r, me, TradeState.Editing);
            InGameOutput.Broadcast(r, new PacketWriter(STradeEditing).U32((uint)TradeCode.Ok).Str(Player.Nickname).U32(me.Guid));
        }
    }

    /// <summary>0x79 str: título (1..31 bytes, único na sala). 0xE6 u32 1, str título, u32 uid, str login para todos.</summary>
    void ShopTitle(string title)
    {
        lock (Rooms.Sync)
        {
            var code = CanUseShopLocked(out var r, out var me);
            var shop = code == TradeCode.Ok ? r.Shops.GetValueOrDefault(me.Guid) : null;
            if (code == TradeCode.Ok && shop == null) code = TradeCode.Error;
            if (code == TradeCode.Ok) code = PersonalShopRules.CheckTitle(title);
            if (code == TradeCode.Ok)
                foreach (var s in r.Shops.Values)
                    if (s != shop && s.Title == title) { code = TradeCode.BadTitle; break; }
            if (code != TradeCode.Ok) { conn.Send(new PacketWriter(STradeTitle).U32((uint)code)); return; }
            shop!.Title = title;
            me.TradeTitle = title;
            InGameOutput.Broadcast(r, new PacketWriter(STradeTitle).U32((uint)TradeCode.Ok).Str(title).U32(me.Guid).Str(Player.Login));
        }
    }

    /// <summary>
    /// 0x7C u32 tipo (1 normal), u32 n (1..6), n × sTradeItem: publica a loja. 0xE9 (u32 1, u32 tipo, nick[22], u32 uid,
    /// u32 n, itens) só para o dono; 0xE1 (u32 1, str nick) para os outros (ícone da loja).
    /// </summary>
    void ShopFinishEdit(PacketReader p)
    {
        uint saleType = p.U32(), n = p.U32();
        var items = new List<TradeItem>();
        for (int i = 0; i < n && i <= PersonalShopRules.MaxItems && p.Remaining >= 0xAA; i++)
        {
            var t = p.Struct<sTradeItem>();
            items.Add(new TradeItem { Index = i, TypeId = (int)t.dwTid, ItemId = (int)t.dwGuid, Quantity = t.iNum, Price = t.i64Price });
        }
        p.Skip(p.Remaining);
        lock (Rooms.Sync)
        {
            var code = CanUseShopLocked(out var r, out var me);
            var shop = code == TradeCode.Ok ? r.Shops.GetValueOrDefault(me.Guid) : null;
            if (code == TradeCode.Ok && shop == null) code = TradeCode.Error;
            if (code == TradeCode.Ok && saleType != 1) code = TradeCode.NoPackage;
            if (code == TradeCode.Ok && n > PersonalShopRules.MaxItems) code = TradeCode.TooManyItems;
            if (code == TradeCode.Ok && shop!.Title.Length == 0) code = TradeCode.NoTitle;
            if (code == TradeCode.Ok) code = PersonalShopRules.CheckItems(Player, items, ctx.Data);
            if (code != TradeCode.Ok) { conn.Send(new PacketWriter(STradePublished).U32((uint)code)); return; }
            shop!.Items.Clear();
            shop.Items.AddRange(items);
            shop.SaleType = saleType;
            shop.State = TradeState.Open;
            shop.VisitCount = 0;
            SetShopState(r, me, TradeState.Open);
            var w = new PacketWriter(STradePublished, 32 + items.Count * 0xAA).U32((uint)TradeCode.Ok).U32(saleType)
                .Fixed(Player.Nickname, NickField).U32(me.Guid);
            conn.Send(Items(w, shop));
            InGameOutput.Broadcast(r, new PacketWriter(STradeOpened).U32((uint)TradeCode.Ok).Str(Player.Nickname), except: this);
            Log.Info($"{conn} abriu a loja '{shop.Title}' com {items.Count} item(ns)");
        }
    }

    /// <summary>0x74: reabre a loja (da edição, com os itens que já tinha). 0xE1 u32 1, str nick para todos.</summary>
    void ShopOpen()
    {
        lock (Rooms.Sync)
        {
            var code = CanUseShopLocked(out var r, out var me);
            var shop = code == TradeCode.Ok ? r.Shops.GetValueOrDefault(me.Guid) : null;
            if (code == TradeCode.Ok && (shop == null || shop.Items.Count == 0)) code = TradeCode.NoItems;
            if (code == TradeCode.Ok && PersonalShopRules.CheckItems(Player, shop!.Items, ctx.Data) != TradeCode.Ok) code = TradeCode.NotYours;
            if (code != TradeCode.Ok) { conn.Send(new PacketWriter(STradeOpened).U32((uint)code)); return; }
            shop!.State = TradeState.Open;
            SetShopState(r, me, TradeState.Open);
            InGameOutput.Broadcast(r, new PacketWriter(STradeOpened).U32((uint)TradeCode.Ok).Str(Player.Nickname));
        }
    }

    /// <summary>Fecha a minha loja (0x75, sair da sala/desconectar): 0xE2 u32 1, str nick, u32 uid para todos (sob o lock).</summary>
    void CloseMyShopLocked()
    {
        var r = room;
        if (r == null || r.Find(this) is not { } me || !r.Shops.Remove(me.Guid, out var shop)) return;
        foreach (var v in shop.Visitors)
            if (r.Find(v)?.Session is GameHandler h) h.visitingShop = 0;
        me.TradeTitle = "";
        SetShopState(r, me, TradeState.None);
        InGameOutput.Broadcast(r, new PacketWriter(STradeClosed).U32((uint)TradeCode.Ok).Str(Player.Nickname).U32(me.Guid));
        Log.Info($"{conn} fechou a loja (renda {shop.Income} pang)");
    }

    void LeaveVisitedShopLocked()
    {
        if (visitingShop != 0 && room?.Shops.GetValueOrDefault(visitingShop) is { } s) s.Visitors.Remove((uint)Player.AccountId);
        visitingShop = 0;
    }

    /// <summary>
    /// 0x77 u32 uid do dono: entrar na loja. 0xE4 u32 1, u32 tipo, nick[22] do dono, str título, u32 uid, u32 n, itens.
    /// </summary>
    void ShopEnter(uint ownerUid)
    {
        lock (Rooms.Sync)
        {
            var code = CanUseShopLocked(out var r, out var me);
            var shop = code == TradeCode.Ok ? r.Shops.GetValueOrDefault(ownerUid) : null;
            if (code == TradeCode.Ok)
                code = shop == null || ownerUid == me.Guid ? TradeCode.Closed
                    : shop.State == TradeState.Editing ? TradeCode.Editing
                    : shop.Visitors.Count >= PersonalShopRules.MaxVisitors && !shop.Visitors.Contains(me.Guid) ? TradeCode.TooManyVisitors
                    : TradeCode.Ok;
            if (code != TradeCode.Ok) { conn.Send(new PacketWriter(STradeEntered).U32((uint)code)); return; }
            LeaveVisitedShopLocked();
            if (shop!.Visitors.Add(me.Guid)) shop.VisitCount++;
            visitingShop = ownerUid;
            var w = new PacketWriter(STradeEntered, 128 + shop.Items.Count * 0xAA).U32((uint)TradeCode.Ok).U32(shop.SaleType)
                .Fixed(shop.Owner.Player.Nickname, NickField).Str(shop.Title).U32(ownerUid);
            conn.Send(Items(w, shop));
        }
    }

    /// <summary>
    /// 0x7D u32 uid do dono, sTradeItem (iIndex, iNum = quantidade): compra. Reserva sob o lock, grava as duas contas numa
    /// transação e só então avisa: 0xEA ao vendedor (u32 1, u8 1, u64 ganho, item vendido) e ao comprador (u32 1, u8 0,
    /// u64 pang, item, u8 1 pilha que já tinha / 2 objeto novo, sItemInfo); 0xEB (str nick, u32 uid, item com o que
    /// sobrou, u32 1 / 3 esgotada) ao dono e aos visitantes.
    /// </summary>
    async Task ShopBuyAsync(PacketReader p)
    {
        uint ownerUid = p.U32();
        if (p.Remaining < 0xAA) { p.Skip(p.Remaining); conn.Send(new PacketWriter(STradeSold).U32((uint)TradeCode.Error)); return; }
        var req = p.Struct<sTradeItem>();
        p.Skip(p.Remaining);
        int qty = Math.Max(req.iNum, 1);
        PersonalShop? shop;
        TradeItem? item = null;
        Room? r;
        lock (Rooms.Sync)
        {
            var code = CanUseShopLocked(out r, out var me);
            shop = code == TradeCode.Ok ? r.Shops.GetValueOrDefault(ownerUid) : null;
            if (code == TradeCode.Ok)
            {
                if (shop == null || visitingShop != ownerUid || !shop.Visitors.Contains(me.Guid)) code = TradeCode.Closed;
                else if (shop.State == TradeState.Editing) code = TradeCode.Editing;
                else
                {
                    foreach (var t in shop.Items) if (t.Index == req.iIndex) { item = t; break; }
                    code = item == null || item.Quantity == 0 ? TradeCode.SoldAlready
                        : qty > item.Quantity ? TradeCode.BadQuantity
                        : item.Price * qty > Player.Pang ? TradeCode.NoPang
                        : TradeCode.Ok;
                }
            }
            if (code != TradeCode.Ok) { conn.Send(new PacketWriter(STradeSold).U32((uint)code)); return; }
            item!.Quantity -= qty;                                               // reserva
        }
        var seller = shop!.Owner.Player;
        bool ok = false;
        (PlayerChanges Seller, PlayerChanges Buyer, Item Bought, bool NewStack)? tr = null;
        try
        {
            int newId = (await ctx.Players.Store.NewIdsAsync(1))[0];
            lock (Rooms.Sync) tr = PersonalShopRules.Transfer(seller, Player, item!, qty, newId);
            if (tr is { } t1)
            {
                await ctx.Players.Store.ApplyTradeAsync(seller.AccountId, t1.Seller, Player.AccountId, t1.Buyer);
                ok = true;
            }
        }
        catch (Exception e) { Log.Warn($"{conn} compra na loja de {seller.Login} falhou: {e.Message}"); }

        lock (Rooms.Sync)
        {
            if (!ok || tr is not { } t)
            {
                item!.Quantity += qty;                                           // desfaz a reserva
                conn.Send(new PacketWriter(STradeSold).U32((uint)TradeCode.SoldAlready));
                return;
            }
            PersonalShopRules.Commit(seller, Player, t.Seller, t.Buyer);
            long total = item!.Price * qty;
            shop.Income += total;
            bool soldOut = true;
            foreach (var x in shop.Items) if (x.Quantity > 0) { soldOut = false; break; }
            if (shop.Owner.Session is GameHandler owner)
                owner.Connection.Send(new PacketWriter(STradeSold).U32((uint)TradeCode.Ok).U8(1).U64((ulong)total).Struct(TradeStruct(item, qty)));
            conn.Send(new PacketWriter(STradeSold, 0x180).U32((uint)TradeCode.Ok).U8(0).U64((ulong)Player.Pang).Struct(TradeStruct(item, qty))
                .U8((byte)(t.NewStack ? 2 : 1)).Struct(PlayerStructs.ItemInfo(t.Bought)));
            var left = new PacketWriter(STradeItemLeft).Str(seller.Nickname).U32(ownerUid).Struct(TradeStruct(item, item.Quantity))
                .U32(soldOut ? 3u : 1u);
            if (shop.Owner.Session is GameHandler o2) o2.Connection.Send(left.Body);
            foreach (var v in shop.Visitors)
                if (r!.Find(v)?.Session is GameHandler h) h.Connection.Send(left.Body);
            left.Dispose();
            if (soldOut)
            {
                shop.State = TradeState.SoldOut;
                SetShopState(r!, shop.Owner, TradeState.SoldOut);
            }
            Log.Info($"{conn} comprou {item.TypeId:X8} x{qty} de {seller.Login} por {total} pang");
        }
    }
}
