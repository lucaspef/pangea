using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// My Room e funções ligadas (docs/protocolo/SPEC-myroom.md): entrada e móveis, armário, caixa de presentes/correio
/// (vazios), upgrades, info do jogador, mascote, caddie, cards e troca rápida de equipamento.
/// </summary>
public sealed partial class GameHandler
{
    // ids C->S
    const ushort CMyRoomEnter = 0xAD, CMyRoomPresence = 0xAF, CPlace = 0xB9, CMyRoomAuthority = 0xB0, CFurnitureSave = 0xAE,
        CFurnitureUse = 0xD9, CLockerState = 0xD5, CLockerOpen = 0xCE, CLockerPang = 0xD7, CLockerPage = 0xCF, CLockerPut = 0xD0,
        CLockerTake = 0xD1, CLockerBank = 0xD6, CLockerSetPw = 0xD2, CLockerChangePw = 0xD3, CLockerLock = 0xD4,
        CGiftPage = 0x92, CMailList = 0xBC, CMailRead = 0xBD, CMailDelete = 0xBE, CMailTake = 0xBF, CMailSend = 0xBB,
        CUpgrade = 0x4B, CUserInfo = 0x2F, CBongdari = 0x95, CMascotMessage = 0x73, CCaddieWarning = 0x6B,
        CCardOpen = 0xC2, CCardUse = 0xB5, CCardAttach = 0xC0, CCardRemove = 0xE2, CQuickEquip = 0x0B, CQuickEquipRoom = 0x0C;
    // ids S->C
    const ushort SMyRoomAuthority = 0x123, SMyRoomAvatar = 0x16E, SFurnitureList = 0x125, SFurnitureSaved = 0x124,
        SLockerState = 0x175, SLockerOpened = 0x171, SLockerPang = 0x177, SLockerPage = 0x172, SLockerPut = 0x173, SLockerTake = 0x174,
        SLockerBank = 0x176, SPang = 0xC6, SUpgrade = 0xA3, SMascotMessage = 0xE0, SCardOpened = 0x14C, SCardResult = 0x158,
        SCardRemoved = 0x18D, SQuickEquip = 0x49, SUserInfoDone = 0x87, SBongdari = 0x109;

    async ValueTask<bool> HandleMyRoomAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CMyRoomEnter: MyRoomEnter(p.U32(), p.U32()); return true;
            case CMyRoomPresence: await MyRoomPresenceAsync(p.U32(), p.U8()); return true;
            case CPlace or CFurnitureUse: p.Skip(p.Remaining); return true;                 // sem resposta
            case CMyRoomAuthority:
                var auth = p.Struct<sRealMyRoomAuthority>();
                await ctx.Actions.SetMyRoomPrivateAsync(Player, auth.bPrivate != 0);
                return true;
            case CFurnitureSave: await SaveFurnitureAsync(p); return true;
            case CLockerState: conn.Send(new PacketWriter(SLockerState).U32(0).U32(0x14)); return true;   // 0x14 = sem senha
            case CLockerOpen: p.Skip(p.Remaining); conn.Send(new PacketWriter(SLockerOpened).U32(0)); return true;
            case CLockerPang: conn.Send(new PacketWriter(SLockerPang).U64((ulong)Player.LockerPang)); return true;
            case CLockerPage: LockerPage(p); return true;
            case CLockerPut: await LockerPutAsync(p); return true;
            case CLockerTake: await LockerTakeAsync(p); return true;
            case CLockerBank: await LockerBankAsync(p.U8() == 1, (long)Math.Min(p.U64(), long.MaxValue)); return true;
            case CLockerSetPw: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x17B).U32(0)); return true;
            case CLockerChangePw: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x179).U32(0)); return true;
            case CLockerLock: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x178).U32(0).U8(0)); return true;
            case CGiftPage: p.Skip(p.Remaining); conn.Send(new PacketWriter(SGiftBox).U8(1).U16(1).U16(0).U16(0)); return true;
            case CMailList: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x140).U32(1).U32(1).U32(0)); return true;
            case CMailRead: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x143).U8(1)); return true;
            case CMailDelete: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x15C)); return true;
            case CMailTake: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x145).U8(1)); return true;
            case CMailSend: p.Skip(p.Remaining); conn.Send(new PacketWriter(0x13F).U8(1)); return true;
            case CUpgrade: await UpgradeAsync(p.U8(), p.U8(), p.U32()); p.Skip(p.Remaining); return true;
            case CUserInfo: await UserInfoAsync(p.U32(), p.Remaining > 0 ? p.U8() : (byte)5); return true;
            case CBongdari: conn.Send(new PacketWriter(SBongdari).U32(0).U32(0).U32(0)); return true;
            case CMascotMessage: await MascotMessageAsync(p.U32(), p.Str(64)); return true;
            case CCaddieWarning: await ctx.Actions.CaddieWarningAsync(Player, (int)p.U32(), p.U8() != 0); return true;
            case CCardOpen: await CardOpenAsync((int)p.U32(), (int)p.U32()); return true;
            case CCardUse: await CardUseAsync((int)p.U32()); return true;
            case CCardAttach: await CardAttachAsync(p); return true;
            case CCardRemove: await CardRemoveAsync((int)p.U32(), (int)p.U32(), (int)p.U32(), (int)p.U32()); return true;
            case CQuickEquip or CQuickEquipRoom: await QuickEquipAsync(p.U8(), (int)p.U32()); return true;
            default: return false;
        }
    }

    // ------------------------------------------------------------------ entrada e móveis

    /// <summary>0xAD u32 eu, u32 dono -> 0x123 u32 1 + sRealMyRoomAuthority. Depois o cliente manda 0xAF(uid, 1).</summary>
    void MyRoomEnter(uint me, uint owner)
    {
        if (owner == 0) owner = (uint)Player.AccountId;
        var a = new sRealMyRoomAuthority { ownerUID = owner, bPrivate = (byte)((Player.Flags & PlayerActions.FlagMyRoomPrivate) != 0 ? 1 : 0) };
        conn.Send(new PacketWriter(SMyRoomAuthority).U32(1).Struct(a));
        myRoomOwner = owner;
    }

    uint myRoomOwner;

    /// <summary>0xAF u32 uid, u8 1 = entrou: 0x16E (avatar: sSlotInfo + sCharacterInfo) + 0x125 móveis do dono.</summary>
    async Task MyRoomPresenceAsync(uint uid, byte inRoom)
    {
        if (inRoom != 1) return;
        var me = new Domain.Rooms.RoomPlayer { Guid = (uint)Player.AccountId, Player = Player, Session = this, Slot = 1 };
        var slot = RoomPackets.SlotInfo(me);
        slot.tidMascot = (uint)(Player.Find(Player.Equip.MascotId)?.TypeId ?? 0);
        conn.Send(new PacketWriter(SMyRoomAvatar, 0x320).Struct(slot)
            .Struct(Player.Character is { } ch ? PlayerStructs.Character(ch) : default));
        var owner = myRoomOwner == 0 || myRoomOwner == Player.AccountId ? Player
            : ctx.World.Find(myRoomOwner)?.Player ?? await ctx.Players.LoadAsync(myRoomOwner) ?? Player;
        var furniture = owner.OfGroup(ItemGroup.Furniture);
        var w = new PacketWriter(SFurnitureList, 16 + furniture.Count * 0x1B).U32(1).U16((ushort)furniture.Count);
        foreach (var f in furniture)
        {
            var e = new sFurniture_List
            {
                id = (uint)f.Id, typeId = (uint)f.TypeId, x = Float(f, "x"), y = Float(f, "y"), z = Float(f, "z"), r = Float(f, "r"),
            };
            e.bArrange = (uint)f.Int("arranged");
            w.Struct(e);
        }
        conn.Send(w);
    }

    static float Float(Item it, string key) => it.Attrs[key]?.GetValue<float>() ?? 0f;

    /// <summary>0xAE u16 n, n × sFurniture_List (só os que mudaram) -> 0x124 u32 0.</summary>
    async Task SaveFurnitureAsync(PacketReader p)
    {
        int n = Math.Min((int)p.U16(), 500);
        var moves = new List<(int, float, float, float, float, bool)>(n);
        for (int i = 0; i < n && p.Remaining >= 0x1B; i++)
        {
            var f = p.Struct<sFurniture_List>();
            if (float.IsFinite(f.x) && float.IsFinite(f.y) && float.IsFinite(f.z) && float.IsFinite(f.r))
                moves.Add(((int)f.id, f.x, f.y, f.z, f.r, f.bArrange != 0));
        }
        await ctx.Actions.SaveFurnitureAsync(Player, moves);
        conn.Send(new PacketWriter(SFurnitureSaved).U32(0));
    }

    // ------------------------------------------------------------------ armário

    const int LockerPerPage = 20;

    /// <summary>0xCF u32, u16 página -> 0x172 u16 páginas, u16 página, u8 n, n × sStoredItemInfo.</summary>
    void LockerPage(PacketReader p)
    {
        if (p.Remaining >= 4) p.U32();
        int page = p.Remaining >= 2 ? p.U16() : 1;
        var items = PlayerActions.LockerItems(Player);
        int pages = Math.Max(1, (items.Count + LockerPerPage - 1) / LockerPerPage);
        page = Math.Clamp(page, 1, pages);
        int start = (page - 1) * LockerPerPage, n = Math.Min(LockerPerPage, items.Count - start);
        var w = new PacketWriter(SLockerPage, 8 + n * 0xAE).U16((ushort)pages).U16((ushort)page).U8((byte)n);
        for (int i = 0; i < n; i++)
        {
            var it = items[start + i];
            var s = new sStoredItemInfo { dwStorageID = (uint)(start + i + 1) };
            s.itemInfo.iIndex = start + i;
            s.itemInfo.dwTid = (uint)it.TypeId;
            s.itemInfo.dwGuid = (uint)it.Id;
            s.itemInfo.iNum = it.Quantity;
            w.Struct(s);
        }
        conn.Send(w);
    }

    /// <summary>0xD0 u8, sStoredItemInfo: guarda. O cliente tira o item da lista com 0xA5 {tid, id, 0}.</summary>
    async Task LockerPutAsync(PacketReader p)
    {
        p.U8();
        var s = p.Struct<sStoredItemInfo>();
        var it = await ctx.Actions.LockerPutAsync(Player, (int)s.itemInfo.dwGuid);
        if (it == null) { conn.Send(new PacketWriter(SLockerPut).U32(1)); return; }
        conn.Send(new PacketWriter(SItemCounts).U8(1).U32((uint)it.TypeId).U32((uint)it.Id).U16(0));
        conn.Send(new PacketWriter(SLockerPut).U32(0));
    }

    /// <summary>0xD1 u8, sStoredItemInfo: retira. O item volta com uma lista 0x71 de um.</summary>
    async Task LockerTakeAsync(PacketReader p)
    {
        p.U8();
        var s = p.Struct<sStoredItemInfo>();
        var it = await ctx.Actions.LockerTakeAsync(Player, (int)s.itemInfo.dwGuid);
        if (it == null) { conn.Send(new PacketWriter(SLockerTake).U32(1)); return; }
        conn.Send(new PacketWriter(SItems).U16(1).U16(1).Struct(PlayerStructs.ItemInfo(it)));
        conn.Send(new PacketWriter(SLockerTake).U32(0));
    }

    /// <summary>0xD6 u8 direção (1 deposita), u64 valor -> 0x176 código, 0xC6 pang, 0x177 pang do armário.</summary>
    async Task LockerBankAsync(bool deposit, long amount)
    {
        bool ok = await ctx.Actions.LockerBankAsync(Player, deposit, amount);
        conn.Send(new PacketWriter(SLockerBank).U32(ok ? 0u : 1u));
        conn.Send(PangUpdate());
        conn.Send(new PacketWriter(SLockerPang).U64((ulong)Player.LockerPang));
    }

    /// <summary>0xC6 u64 pang, u64 0: só atualiza o pang mostrado.</summary>
    PacketWriter PangUpdate() => new PacketWriter(SPang).U64((ulong)Player.Pang).U64(0);

    // ------------------------------------------------------------------ upgrades, info, mascote

    /// <summary>0x4B u8 tipo (bit0 club set, bit1 descer), u8 atributo, u32 id -> 0xA3 u8 resultado [+ tipo, atributo, id, i64 gasto].</summary>
    async Task UpgradeAsync(byte type, byte stat, uint id)
    {
        var (res, cost) = await ctx.Actions.UpgradeAsync(Player, (type & 1) != 0, (type & 2) != 0, stat, (int)id);
        var w = new PacketWriter(SUpgrade).U8((byte)res);
        if (res is UpgradeResult.Up or UpgradeResult.Down) w.U8(type).U8(stat).U32(id).I64(cost);
        conn.Send(w);
    }

    /// <summary>
    /// 0x2F u32 uid, u8 temporada -> 0x14F (cria a entrada), 0x14E equip, 0x156 personagem, 0x150 estatísticas,
    /// 0x151 troféus, 0x154/0x152/0x153 listas vazias, 0x87 u32 1 (ou 0x87 u32 3 = fecha, uid desconhecido).
    /// </summary>
    async Task UserInfoAsync(uint uid, byte season)
    {
        Player? target = uid == 0 || uid == Player.AccountId ? Player : ctx.World.Find(uid)?.Player;
        if (target == null && uid == PlayerService.BotAccountId)
            lock (Rooms.Sync) target = room?.Bot?.Player;
        target ??= uid < PlayerService.BotAccountId ? await ctx.Players.LoadAsync(uid) : null;
        if (target == null) { conn.Send(new PacketWriter(SUserInfoDone).U32(3)); return; }
        var ui = PlayerStructs.UserInfo(target);
        var stats = ui.stat;
        if (season == 0)                                   // temporada anterior: sem partidas, mesmo nível
        {
            stats = default;
            stats.Level = ui.stat.Level;
            for (int i = 0; i < 6; i++) stats.cBestScore[i] = 127;
        }
        conn.Send(new PacketWriter(0x14F).U8(season).U32(uid).U16(ui.roomIndex).Struct(ui.info).U32(0));
        conn.Send(new PacketWriter(0x14E).U8(season).U32(uid).Struct(ui.userEquip));
        conn.Send(new PacketWriter(0x156).U32(uid).Struct(ui.charInfo));
        conn.Send(new PacketWriter(0x150).U8(season).U32(uid).Struct(stats));
        conn.Send(new PacketWriter(0x151).U8(season).U32(uid).Zeros(0x4E));
        conn.Send(new PacketWriter(0x154).U8(season).U32(uid).U16(0));
        conn.Send(new PacketWriter(0x152).U8(season).U32(uid).U16(0));
        conn.Send(new PacketWriter(0x153).U8(season).U32(uid).U16(0));
        conn.Send(new PacketWriter(SUserInfoDone).U32(1).U8(season).U32(uid));
    }

    /// <summary>0x73 u32 id, str mensagem -> 0xE0 u8 4, u32 id, str mensagem, u64 pang (ou u8 1, u32 id).</summary>
    async Task MascotMessageAsync(uint id, string message)
    {
        var msg = await ctx.Actions.MascotMessageAsync(Player, (int)id, message);
        conn.Send(msg == null ? new PacketWriter(SMascotMessage).U8(1).U32(id)
            : new PacketWriter(SMascotMessage).U8(4).U32(id).Str(msg).U64((ulong)Player.Pang));
    }

    // ------------------------------------------------------------------ cards

    static sCards CardStruct(Item it, int count) => new() { uid = (uint)it.Id, typeId = (uint)it.TypeId, count = count, type = 1 };

    /// <summary>0xC2 u32 tid, u32 id -> 0x14C u32 0, sCards do pacote (contagem antes), u8 n, n × {sCards, u32 1}; u32 1 = falhou.</summary>
    async Task CardOpenAsync(int tid, int id)
    {
        var r = await ctx.Cards.OpenPackAsync(Player, tid, id);
        if (r is not { } ok) { conn.Send(new PacketWriter(SCardOpened).U32(1)); return; }
        var w = new PacketWriter(SCardOpened).U32(0).Struct(CardStruct(ok.Pack, ok.PackCountBefore)).U8((byte)ok.Drawn.Count);
        foreach (var c in ok.Drawn) w.Struct(CardStruct(c, 1)).U32(1);
        conn.Send(w);
    }

    PacketWriter CardResult(ActiveCard c) => new PacketWriter(SCardResult).U32(0).U32((uint)c.Id).U32((uint)c.TypeId)
        .U32((uint)c.PartTypeId).U32((uint)c.PartId).U32((uint)c.Slot).U32(1)
        .Struct(PlayerStructs.SystemTime(c.Start?.ToLocalTime())).Struct(PlayerStructs.SystemTime(c.End?.ToLocalTime())).U16(0);

    /// <summary>0xB5 u32 tid (card especial) -> 0x158 (+ 0xC6 se deu pang); u32 1 + u8 = falhou.</summary>
    async Task CardUseAsync(int tid)
    {
        var r = await ctx.Cards.UseSpecialAsync(Player, tid);
        if (r is not { } ok) { conn.Send(new PacketWriter(SCardResult).U32(1).U8(1)); return; }
        conn.Send(CardResult(ok.Card));
        if (ok.PangGained > 0) conn.Send(PangUpdate());
    }

    /// <summary>0xC0 (0x3A bytes): u32 id do card, u32 tid, u32 tid da peça, u32 id da peça, u32 slot -> 0x158.</summary>
    async Task CardAttachAsync(PacketReader p)
    {
        if (p.Remaining < 20) { p.Skip(p.Remaining); conn.Send(new PacketWriter(SCardResult).U32(1).U8(1)); return; }
        int cardId = (int)p.U32(), cardTid = (int)p.U32(), partTid = (int)p.U32(), partId = (int)p.U32(), slot = (int)p.U32();
        p.Skip(p.Remaining);
        var r = await ctx.Cards.AttachAsync(Player, cardId, cardTid, partTid, partId, slot);
        conn.Send(r is { } ok ? CardResult(ok) : new PacketWriter(SCardResult).U32(1).U8(1));
    }

    /// <summary>0xE2 u32 tid do removedor, u32 id, u32 tid da peça, u32 id da peça -> 0x18D u8 1, u32 id da peça, u32 tid | u8 0, u8 0.</summary>
    async Task CardRemoveAsync(int removerTid, int removerId, int partTid, int partId)
    {
        bool ok = await ctx.Cards.RemoveAsync(Player, removerTid, removerId, partId);
        conn.Send(ok ? new PacketWriter(SCardRemoved).U8(1).U32((uint)partId).U32((uint)removerTid) : new PacketWriter(SCardRemoved).U8(0).U8(0));
    }

    // ------------------------------------------------------------------ troca rápida de equipamento

    /// <summary>
    /// 0x0B (lobby/My Room) / 0x0C (na sala): u8 tipo, u32 valor -> 0x49 u8 tipo, u32 guid, dados novos
    /// (caddie 0x19 / u32 bola / club 0x12 / personagem 0x1BC / mascote 0x3F) para a sala inteira (ou só para si).
    /// </summary>
    async Task QuickEquipAsync(byte kind, int value)
    {
        if (!await ctx.Actions.QuickEquipAsync(Player, kind, value))
        {
            Log.Info($"{conn} troca de equipamento {kind}={value} recusada");
            return;
        }
        var ui = PlayerStructs.UserInfo(Player);
        var w = new PacketWriter(SQuickEquip).U8(kind).U32((uint)Player.AccountId);
        switch (kind)
        {
            case 1: w.Struct(ui.caddieInfo); break;
            case 2: w.U32(ui.userEquip.tidBall); break;
            case 3: w.Struct(ui.clubInfo); break;
            case 4: w.Struct(ui.charInfo); break;
            default: w.Struct(ui.mascotInfo); break;
        }
        lock (Rooms.Sync)
        {
            if (room != null)
            {
                InGameOutput.Broadcast(room, w);
                if (room.Find(this) is { } me) InGameOutput.Broadcast(room, RoomPackets.SlotUpdate(room, me), except: this);
                return;
            }
        }
        conn.Send(w);
    }
}
