using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Caixa Mágica da caddie (docs/protocolo/SPEC-tiki-craft.md §1): 0x7E -> 0xA5/0x71 (inventário) + 0xED.</summary>
public sealed partial class GameHandler
{
    const ushort CMagicBox = 0x7E, SMagicBox = 0xED, COpenBox = 0xF1, SOpenBox = 0x1A2;
    const int RecycleItemSize = 0x50;

    /// <summary>
    /// 0xF1 u32 caixa (My Room) -> 0xA5 cubo e chave, prêmio (0xC6 pang / 0x71 ou 0xA5 item), 0x1A2 u32 código, u32 caixa,
    /// u32 prêmio, u32 quantidade. O cliente trava a tela até o 0x1A2, então sempre responde.
    /// </summary>
    async Task OpenBoxAsync(uint boxTid)
    {
        var r = await ctx.SpinCube.OpenAsync(Player, (int)boxTid);
        Log.Info($"{conn} abrir caixa {boxTid:X8}: código {r.Code}" + (r.Code == 0 ? $", prêmio {r.PrizeTypeId:X8} x{r.PrizeQty}" : ""));
        if (r.Code != 0) { conn.Send(new PacketWriter(SOpenBox).U32(r.Code).U32(boxTid).U32(0).U32(0)); return; }
        var w = new PacketWriter(SItemCounts, 32).U8((byte)r.Consumed.Count);
        foreach (var c in r.Consumed) w.U32((uint)c.TypeId).U32((uint)c.Id).U16((ushort)c.Count);
        conn.Send(w);
        if (r.PrizeTypeId == Domain.Shop.SpinCubeService.PangPouch) conn.Send(PangUpdate());
        else if (r.Item is { } g && Player.Find(g.Id) is { } it)
            conn.Send(r.NewItem ? new PacketWriter(SItems).U16(1).U16(1).Struct(PlayerStructs.ItemInfo(it))
                : new PacketWriter(SItemCounts).U8(1).U32((uint)it.TypeId).U32((uint)it.Id).U16((ushort)it.Quantity));
        conn.Send(new PacketWriter(SOpenBox).U32(0).U32(boxTid).U32((uint)r.PrizeTypeId).U32((uint)r.PrizeQty));
    }

    /// <summary>
    /// 0x7E u16 receita, u8 vezes, u8 n, n × {u32 tid, u32 id}. O 0xED não mexe no inventário do cliente, então os
    /// materiais gastos (0xA5) e o resultado (0x71 se novo, 0xA5 se somou numa pilha) vão antes dele.
    /// </summary>
    async Task MagicBoxAsync(PacketReader p)
    {
        int index = p.U16(), qty = p.U8(), n = p.U8();
        if (n > 4) { p.Skip(p.Remaining); conn.Send(new PacketWriter(SMagicBox).U32((uint)MagicBoxCode.Invalid)); return; }
        var mats = new (int, int)[n];
        for (int i = 0; i < n; i++) mats[i] = ((int)p.U32(), (int)p.U32());
        var r = await ctx.MagicBox.ExchangeAsync(Player, index, qty, mats);
        Log.Info($"{conn} caixa mágica: receita {index + 1} x{qty} -> {r.Code}" + (r.Granted.Count > 0 ? $" {r.Granted[0].TypeId:X8}x{r.Granted[0].Count}" : ""));
        if (r.Code != MagicBoxCode.Ok) { conn.Send(new PacketWriter(SMagicBox).U32((uint)r.Code)); return; }

        var w = new PacketWriter(SItemCounts, 8 + r.Consumed.Count * 10).U8((byte)r.Consumed.Count);
        foreach (var c in r.Consumed) w.U32((uint)c.TypeId).U32((uint)c.Id).U16((ushort)c.Count);
        conn.Send(w);
        foreach (var g in r.Granted)
        {
            if (Player.Find(g.Id) is not { } it) continue;
            conn.Send(r.NewIds.Contains(g.Id) ? new PacketWriter(SItems).U16(1).U16(1).Struct(PlayerStructs.ItemInfo(it))
                : new PacketWriter(SItemCounts).U8(1).U32((uint)it.TypeId).U32((uint)it.Id).U16((ushort)it.Quantity));
        }
        // sRecycleItem (0x50): u32 tid, u32 id, u16 quantidade, resto zerado (o cliente só usa o tid na tela)
        w = new PacketWriter(SMagicBox, 16 + r.Granted.Count * RecycleItemSize).U32(0).U16((ushort)index).U8((byte)r.Granted.Count);
        foreach (var g in r.Granted) w.U32((uint)g.TypeId).U32((uint)g.Id).U16((ushort)Math.Min(g.Count, ushort.MaxValue)).Zeros(RecycleItemSize - 10);
        conn.Send(w);
    }
}
