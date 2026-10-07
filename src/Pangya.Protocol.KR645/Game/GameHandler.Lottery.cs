using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Papel Shop (봉다리) e raspadinha (docs/protocolo/SPEC-papel-raspadinha.md). O cliente não escolhe nada: manda só
/// "jogar"/"raspar" e espera a resposta (sem ela os botões ficam travados), então toda requisição tem resposta.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CPapelOpen = 0x95, CPapelPlay = 0x6D, CScratch = 0x70, CScratchSerial = 0x71, CScratchReload = 0x72;
    const ushort SPapelOpen = 0x109, SPapelResult = 0xD4, SCouponUsed = 0xD3, SPapelTimes = 0xF9, SScratchResult = 0xDB,
        SScratchSerial = 0xDC;
    const uint Unlimited = 0xFFFFFFFF;

    async ValueTask<bool> HandleLotteryAsync(PacketReader p)
    {
        switch (p.Id)
        {
            // u32 bônus disponíveis, u32 "faltam N", u32 ignorado; -1 = sem bônus (sem aviso de jogadas acabando)
            case CPapelOpen: conn.Send(new PacketWriter(SPapelOpen).U32(Unlimited).U32(0).U32(0)); return true;
            case CPapelPlay: await PapelPlayAsync(); return true;
            case CScratch: await ScratchAsync(); return true;
            case CScratchSerial:                                                   // sem tabela de seriais: "não existe"
                p.Skip(p.Remaining);
                conn.Send(new PacketWriter(SScratchSerial).U32(2));
                return true;
            case CScratchReload: return true;                                       // cartões comprados na web: não há
            case CMagicBox: await MagicBoxAsync(p); return true;
            default: return false;
        }
    }

    /// <summary>0x6D -> [0xD3 cupom] + 0xD4 resultado (pang e cookie absolutos) + 0xF9 jogadas restantes.</summary>
    async Task PapelPlayAsync()
    {
        var r = await ctx.Lottery.PlayPapelAsync(Player);
        if (r.Coupon != 0) conn.Send(new PacketWriter(SCouponUsed).U32((uint)r.Coupon));
        var w = new PacketWriter(SPapelResult, 32 + r.Prizes.Count * 20).U32(r.Code);
        if (r.Code == 0)
        {
            w.U32((uint)r.Prizes.Count);
            foreach (var pz in r.Prizes) BonusBall(w, pz, (int)pz.Class);
            w.U64((ulong)Player.Pang).U64((ulong)Player.Cookie);
        }
        conn.Send(w);
        conn.Send(new PacketWriter(SPapelTimes).U32(Unlimited).U32(Unlimited));
        Log.Info($"{conn} papel: {Describe(r.Code, r.Prizes)} cupom={r.Coupon} pang={Player.Pang}");
    }

    /// <summary>0x70 -> 0xD3 cartão (antes: o 0xDB recontaria os cartões) + 0xDB resultado.</summary>
    async Task ScratchAsync()
    {
        var r = await ctx.Lottery.ScratchAsync(Player);
        if (r.Card != 0) conn.Send(new PacketWriter(SCouponUsed).U32((uint)r.Card));
        var w = new PacketWriter(SScratchResult, 16 + r.Prizes.Count * 20).U32(r.Code);
        if (r.Code == 0)
        {
            w.U32((uint)r.Prizes.Count);
            foreach (var pz in r.Prizes) BonusBall(w, pz, 0);
        }
        conn.Send(w);
        Log.Info($"{conn} raspadinha: {Describe(r.Code, r.Prizes)} cartão={r.Card}");
    }

    /// <summary>sBonusBall (0x14): i32 cor, u32 typeid, u32 id, u32 qtd, u8 classe, u8 0 (sem prazo), u16 0.</summary>
    static void BonusBall(PacketWriter w, Prize pz, int color) =>
        w.I32(color).U32((uint)pz.TypeId).U32((uint)pz.Id).U32((uint)pz.Quantity).U8((byte)pz.Class).U8(0).U16(0);

    static string Describe(uint code, List<Prize> prizes)
    {
        if (code != 0) return $"erro {code}";
        var s = new System.Text.StringBuilder();
        foreach (var pz in prizes) s.Append($"{pz.TypeId:X8}x{pz.Quantity}{(pz.Class == PrizeClass.Rare ? "(raro)" : "")} ");
        return prizes.Count == 0 ? "nada" : s.ToString().TrimEnd();
    }
}
