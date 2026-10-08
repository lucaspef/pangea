using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Self Design (SPEC-self-design.md): chave de upload (0xC1 -> 0x14B) e registro/consulta/cópia do desenho
/// (0xB1 -> 0x126). O arquivo sobe e desce por HTTP (Pangya.Web, /UCC/...).
/// </summary>
public sealed partial class GameHandler
{
    const ushort CUcc = 0xB1, CSecurityKey = 0xC1, SUcc = 0x126, SSecurityKey = 0x14B;
    const byte UccContent = 1;

    async ValueTask<bool> HandleUccAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CSecurityKey: SecurityKey(p); return true;
            case CUcc:
                switch (p.U8())
                {
                    case 0: await RegisterUccAsync(p, final: true); break;
                    case 1: UccInfo(p); break;
                    case 2: await CopyUccAsync(p); break;
                    case 3: await RegisterUccAsync(p, final: false); break;
                    default: p.Skip(p.Remaining); break;
                }
                return true;
            default: return false;
        }
    }

    /// <summary>
    /// 0xC1 sub 0: u32 uid, u8 content (1 = UCC), u32 guid -> 0x14B u8 0, u8 content, u32 guid, str chave, u8 1 (0 =
    /// recusado). Só para peça Self Design de desenhar, do jogador, ainda não finalizada.
    /// </summary>
    void SecurityKey(PacketReader p)
    {
        byte sub = p.U8();
        if (sub != 0) { p.Skip(p.Remaining); return; }
        p.U32();
        byte content = p.U8();
        int guid = (int)p.U32();
        string key = "";
        bool ok = content == UccContent && Player.Find(guid) is { Location: ItemLocation.Inventory } it
            && ctx.Data.UccPart(it.TypeId) is { CanDraw: true } info && (Ucc.Status(it) & Ucc.Final) == 0;
        if (ok)
        {
            var it2 = Player.Find(guid)!;
            key = ctx.World.Ucc.Issue(Player.AccountId, guid, Ucc.FileName(ctx.Data.UccPart(it2.TypeId)!.Texture, Ucc.Index(it2)), DateTime.UtcNow);
        }
        Log.Info($"{conn} self design: chave para o item {guid} -> {(ok ? "ok" : "recusada")}");
        conn.Send(new PacketWriter(SSecurityKey).U8(0).U8(content).U32((uint)guid).Str(key).U8(ok ? (byte)1 : (byte)0));
    }

    /// <summary>Peça Self Design de desenhar do jogador com aquele typeid e índice.</summary>
    Item? MyUccItem(int typeId, string index)
    {
        foreach (var it in Player.Items.Values)
            if (it.TypeId == typeId && it.Location == ItemLocation.Inventory && Ucc.Index(it) == index
                && ctx.Data.UccPart(typeId) is { CanDraw: true })
                return it;
        return null;
    }

    /// <summary>
    /// 0xB1 sub 0 (final: u32 typeid, str índice, str nome) / sub 3 (temporário: u32 typeid, str índice), depois do
    /// upload HTTP. Respostas: sub 0 -> 0x126 u8 0, u8 ok, u32 guid, u32 typeid, str índice, str nome;
    /// sub 3 -> 0x126 u8 3, u32 typeid, str índice, u8 ok.
    /// </summary>
    async Task RegisterUccAsync(PacketReader p, bool final)
    {
        int tid = (int)p.U32();
        string index = p.Str(16);
        string name = final ? p.Str(64).Trim() : "";
        var it = MyUccItem(tid, index);
        bool ok = it != null && (Ucc.Status(it) & Ucc.Final) == 0 && (!final || Ucc.ValidName(name))
            && ctx.World.Ucc.TakeUploaded(Player.AccountId, it.Id, DateTime.UtcNow);
        if (ok)
        {
            if (final)
            {
                it!.Attrs["ucc_idx"] = index;
                it.Set("ucc_status", Ucc.Final);
                it.Set("ucc_seq", 1);
                it.Attrs["ucc_name"] = name;
                it.Attrs["ucc_date"] = DateTime.UtcNow;
            }
            else
            {
                it!.Attrs["ucc_idx"] = index;
                it.Set("ucc_status", Ucc.Status(it) | Ucc.Temp);
            }
            var ch = new PlayerChanges();
            ch.Updated.Add(it);
            await ctx.Players.Store.ApplyAsync(Player.AccountId, ch);
        }
        Log.Info($"{conn} self design: {(final ? $"desenho final '{name}'" : "salvamento temporário")} 0x{tid:X8}/{index} -> {(ok ? "ok" : "recusado")}");
        if (final)
            conn.Send(new PacketWriter(SUcc).U8(0).U8(ok ? (byte)1 : (byte)0).U32((uint)(it?.Id ?? 0)).U32((uint)tid).Str(index).Str(name));
        else
            conn.Send(new PacketWriter(SUcc).U8(3).U32((uint)tid).Str(index).U8(ok ? (byte)1 : (byte)0));
    }

    /// <summary>
    /// 0xB1 sub 1: u32 guid, u8 flag -> 0x126 u8 1, u32 guid, str índice, u8 flag, sItemInfo. Vale para itens de
    /// outros jogadores online (o cliente pede para ver o desenho deles); guid desconhecido: sem resposta.
    /// </summary>
    void UccInfo(PacketReader p)
    {
        int guid = (int)p.U32();
        byte flag = p.U8();
        Item? it = Player.Find(guid);
        if (it == null)
            foreach (var s in ctx.World.Online)
            {
                try { if (s.Player.Find(guid) is { } other) { it = other; break; } }
                catch (InvalidOperationException) { }                   // inventário mudando agora: tenta o próximo
            }
        if (it == null || it.Group != ItemGroup.Part || ctx.Data.UccPart(it.TypeId) == null) return;
        conn.Send(new PacketWriter(SUcc).U8(1).U32((uint)guid).Str(Ucc.Index(it)).U8(flag).Struct(PlayerStructs.ItemInfo(it)));
    }

    /// <summary>
    /// 0xB1 sub 2: u32 typeid de origem, str índice, u16 série, u32 guid do alvo. Origem = desenho final original
    /// (série 1) do jogador; alvo = peça de cópia dele, sem desenho, da mesma roupa. O alvo passa a usar o mesmo
    /// arquivo (mesmo índice). 0x126 u8 2, u32 tid origem, str índice, u16 série, u32 guid, u32 guid, u32 tid alvo,
    /// str índice, u16 série nova, u8 ok.
    /// </summary>
    async Task CopyUccAsync(PacketReader p)
    {
        int srcTid = (int)p.U32();
        string index = p.Str(16);
        ushort srcSeq = p.U16();
        int target = (int)p.U32();
        Item? src = null;
        foreach (var it in Player.Items.Values)
            if (it.TypeId == srcTid && it.Location == ItemLocation.Inventory && Ucc.Index(it) == index
                && (Ucc.Status(it) & Ucc.Final) != 0 && Ucc.Seq(it) == 1) { src = it; break; }
        var dst = Player.Find(target);
        var srcInfo = ctx.Data.UccPart(srcTid);
        var dstInfo = dst == null ? null : ctx.Data.UccPart(dst.TypeId);
        bool ok = src != null && srcSeq == 1 && srcInfo != null && dst is { Location: ItemLocation.Inventory } && dstInfo is { CanCopy: true }
            && (Ucc.Status(dst) & Ucc.Final) == 0 && dstInfo.Clothes == srcInfo.Clothes;
        int seq = 0;
        if (ok)
        {
            seq = src!.Int("ucc_copies") + 2;                                  // cópias: 2, 3, ...
            src.Set("ucc_copies", seq - 1);
            dst!.Attrs["ucc_idx"] = index;
            dst.Set("ucc_status", Ucc.Final);
            dst.Set("ucc_seq", seq);
            dst.Attrs["ucc_name"] = Ucc.Name(src);
            dst.Attrs["ucc_copier"] = Player.Nickname;
            dst.Attrs["ucc_date"] = DateTime.UtcNow;
            var ch = new PlayerChanges();
            ch.Updated.Add(src);
            ch.Updated.Add(dst);
            await ctx.Players.Store.ApplyAsync(Player.AccountId, ch);
        }
        Log.Info($"{conn} self design: cópia de 0x{srcTid:X8}/{index} para o item {target} -> {(ok ? $"ok (série {seq})" : "recusada")}");
        conn.Send(new PacketWriter(SUcc).U8(2).U32((uint)srcTid).Str(index).U16(srcSeq).U32((uint)target).U32((uint)target)
            .U32((uint)(dst?.TypeId ?? 0)).Str(index).U16((ushort)seq).U8(ok ? (byte)1 : (byte)0));
    }
}
