using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Loja e equipamento (docs/protocolo/SPEC-player-shop.md). Abrir/fechar a loja é só no cliente.</summary>
public sealed partial class GameHandler
{
    // ids C->S
    const ushort CBuy = 0x1D, CGift = 0x1F, CEquip = 0x20, CCookieQuery = 0x3D, CShopOpen = 0x5C;
    // ids S->C
    const ushort SBought = 0xA8, SBuyResult = 0x66, SGiftResult = 0x68, SEquipResult = 0x69;
    const byte EquipOk = 4, EquipFail = 0;

    async ValueTask<bool> HandleShopAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CBuy: await BuyAsync(p); return true;
            case CGift: p.Skip(p.Remaining); conn.Send(new PacketWriter(SGiftResult).U32((uint)ShopCode.Fail)); return true;   // presentes: ainda não
            case CCookieQuery: conn.Send(new PacketWriter(SCookie).U64((ulong)Player.Cookie)); return true;
            case CShopOpen: return true;
            case CEquip: await EquipAsync(p); return true;
            default: return false;
        }
    }

    /// <summary>0x1D: u8 aluguel, u16 n, n × sBuyItem. Resposta 0xA8 (itens) + 0x66 (código; se 0: pang e cookie novos).</summary>
    async Task BuyAsync(PacketReader p)
    {
        p.U8();                                             // aluguel: tudo é entregue como permanente/período
        int n = p.U16();
        if (n > ShopService.MaxLines) { conn.Send(new PacketWriter(SBuyResult).U32((uint)ShopCode.TooMany)); return; }
        var reqs = new List<BuyRequest>(n);
        for (int i = 0; i < n; i++)
        {
            var b = p.Struct<sBuyItem>();
            reqs.Add(new BuyRequest((int)b.TypeCode, b.DayCount, (int)Math.Min(b.ItemCount, int.MaxValue)));
        }
        var (code, granted) = await ctx.Shop.BuyAsync(Player, reqs);
        Log.Info($"{conn} loja: {n} item(ns) -> {code}; pang={Player.Pang} cookie={Player.Cookie}");
        if (code != ShopCode.Ok)
        {
            conn.Send(new PacketWriter(SBuyResult).U32((uint)code));
            return;
        }
        var w = new PacketWriter(SBought, 8 + granted.Count * 0x26).U16((ushort)granted.Count);
        foreach (var g in granted)
            w.Struct(new sBuyItemResult
            {
                Typeid = (uint)g.TypeId, guid = (uint)g.Id, Time = (ushort)g.Hours, Count = (ushort)Math.Min(g.Count, ushort.MaxValue),
                endDate = PlayerStructs.SystemTime(g.End?.ToLocalTime()),
            });
        conn.Send(w);
        conn.Send(new PacketWriter(SBuyResult).U32(0).U64((ulong)Player.Pang).U64((ulong)Player.Cookie));
    }

    /// <summary>
    /// 0x20 u8 tipo + dados -> 0x69 u8 4 (ok) / 0, u8 tipo, dados. Tudo é conferido contra o que o jogador possui:
    /// 0 roupas do personagem (sCharacterInfo), 1 caddie, 2 itens da bolsa, 3 bola + club set, 4 skins, 5 personagem, 8 mascote.
    /// </summary>
    async Task EquipAsync(PacketReader p)
    {
        var type = p.U8();
        var pl = Player;
        var e = pl.Equip;
        var w = new PacketWriter(SEquipResult).U8(EquipOk).U8(type);
        var changes = new PlayerChanges();
        bool ok = true;
        switch (type)
        {
            case 0:
                var info = p.Struct<sCharacterInfo>();
                var ch = pl.Find((int)info.guid);
                ok = ch != null && ch.Group == ItemGroup.Character && ch.Location == ItemLocation.Inventory && ApplyOutfit(ch, info);
                if (ok) { changes.Updated.Add(ch!); w.Struct(PlayerStructs.Character(ch!)); }
                break;
            case 1:
                var cad = (int)p.U32();
                ok = cad == 0 || Owns(cad, ItemGroup.Caddie);
                if (ok) e.CaddieId = cad;
                break;
            case 2:
                var slots = new int[10];
                var used = new Dictionary<int, int>();
                int k = 0;
                for (int i = 0; i < 10; i++)
                {
                    int tid = (int)p.U32();
                    if (tid == 0 || pl.FindType(tid) is not { } it || used.GetValueOrDefault(tid) >= it.Quantity) continue;
                    used[tid] = used.GetValueOrDefault(tid) + 1;                  // só unidades que existem
                    slots[k++] = tid;
                }
                e.ItemSlots = slots;
                foreach (var s in slots) w.U32((uint)s);
                break;
            case 3:
                int ball = (int)p.U32(), club = (int)p.U32();
                ok = (ball == Item.BasicBall || pl.FindType(ball) is { Group: ItemGroup.Ball }) && Owns(club, ItemGroup.ClubSet);
                if (ok) { e.BallTypeId = ball; e.ClubSetId = club; }
                break;
            case 4:
                var skins = new int[6];
                for (int i = 0; i < 6; i++)
                {
                    int tid = (int)p.U32();
                    skins[i] = tid != 0 && pl.FindType(tid) is { Group: ItemGroup.Skin } ? tid : 0;
                    w.U32((uint)skins[i]);
                }
                e.SkinTypeIds = skins;
                break;
            case 5:
                var charId = (int)p.U32();
                ok = Owns(charId, ItemGroup.Character);
                if (ok) { e.CharacterId = charId; w.U32((uint)charId); }
                break;
            case 8:
                var mascot = (int)p.U32();
                ok = mascot == 0 || Owns(mascot, ItemGroup.Mascot);
                if (ok) { e.MascotId = mascot; w.Struct(PlayerStructs.Mascot(pl.Find(mascot))); }
                break;
            default:                                                    // 7 = iniciar item de período: sem resposta de sucesso
                p.Skip(p.Remaining);
                w.Dispose();
                return;
        }
        if (!ok)
        {
            w.Dispose();
            conn.Send(new PacketWriter(SEquipResult).U8(EquipFail));
            return;
        }
        changes.Equip = e;
        await ctx.Players.Store.ApplyAsync(pl.AccountId, changes);
        conn.Send(w);
    }

    bool Owns(int id, ItemGroup g) => Player.Find(id) is { } it && it.Group == g && it.Location == ItemLocation.Inventory;

    /// <summary>
    /// Roupas novas do personagem: cada parte tem de ser a padrão daquele personagem ou uma parte que o jogador possui
    /// (o id na ItemIdList tem de ser dele e do mesmo typeid). Cabelo e camisa não mudam por aqui.
    /// </summary>
    bool ApplyOutfit(Item ch, in sCharacterInfo info)
    {
        var defaults = ctx.Data.DefaultParts(ch.TypeId);
        var parts = new int[24];
        var partIds = new int[24];
        for (int i = 0; i < 24; i++)
        {
            int tid = (int)info.tidParts[i], id = (int)info.ItemIdList[i];
            if (tid == 0) continue;
            if (id != 0)
            {
                if (Player.Find(id) is not { } part || part.TypeId != tid || part.Location != ItemLocation.Inventory) return false;
                partIds[i] = id;
            }
            else if (tid != defaults[i] && Player.FindType(tid) == null) return false;
            parts[i] = tid;
        }
        var aux = new int[5];
        for (int i = 0; i < 5; i++)
        {
            int tid = (int)info.tidAuxParts[i];
            if (tid != 0 && Player.FindType(tid) == null) return false;
            aux[i] = tid;
        }
        ch.Set("parts", parts);
        ch.Set("part_ids", partIds);
        ch.Set("aux", aux);
        return true;
    }
}
