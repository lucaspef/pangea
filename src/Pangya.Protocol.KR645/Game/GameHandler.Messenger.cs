using Pangya.Core.Logging;
using Pangya.Core.Net;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// O que o game server faz pelo mensageiro (docs/protocolo/SPEC-messenger.md §7): 0x88 -> 0xFA (lista de mensageiros;
/// o cliente apaga a do login a cada queda do MSN), 0x3C/0x11F (lista de amigos pelo game, para correio e presente) e o
/// status "jogando" dos amigos. E pelo ranking (SPEC-ranking.md §4.3): 0x47 -> 0xA0 (endereço).
/// </summary>
public sealed partial class GameHandler
{
    const ushort CMessengerServers = 0x88, CMessengerRelay = 0x3C, SMessengerServers = 0xFA, SRelayResult = 0x93, SNotes = 0xB0,
        CRankingButton = 0x47, SRankingAddress = 0xA0;
    /// <summary>controlServerService: o botão Ranking só mostra "랭킹 관련 부분 점검중입니다." (sem servidor de ranking).</summary>
    const uint RankingOff = 0x10000;
    const ushort RelayFriendList = 0x11F, RelayNote = 0x111;

    async ValueTask<bool> HandleMessengerAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CMessengerServers:
                var list = ctx.Registry != null ? await ctx.Registry.ListAsync("messenger") : [];
                conn.Send(Login.LoginHandler.ServerList(SMessengerServers, list));
                return true;
            case CMessengerRelay:
                await RelayAsync(p);
                return true;
            case CRankingButton:                                             // botão Ranking -> 0xA0 str IP, u32 porta
                p.Skip(p.Remaining);
                if (ctx.Ranking is { } rk) conn.Send(new PacketWriter(SRankingAddress).Str(rk.Address).U32((uint)rk.Port));
                return true;
            default: return false;
        }
    }

    /// <summary>0x3C u16 sub: 0x11F lista de amigos (responde no formato do MSN, 0x2E/0x102); 0x111 bilhete.</summary>
    async Task RelayAsync(PacketReader p)
    {
        ushort sub = p.U16();
        switch (sub)
        {
            case RelayFriendList:
                p.Skip(p.Remaining);
                if (ctx.Messenger != null)
                    foreach (var page in await ctx.Messenger.ListPagesAsync(Player.AccountId)) conn.Send(page);
                break;
            case RelayNote:
                await NoteAsync(p.U32(), p.Str(64), p.U8());
                break;
            default:
                Log.Info($"{conn} 0x3C sub 0x{sub:X} sem tratamento ({p.Remaining} bytes)");
                p.Skip(p.Remaining);
                break;
        }
    }

    /// <summary>
    /// 0x3C/0x111 u32 uid, str bilhete (≤63), u8 origem -> 0x93 u16 0x111, u32 código [, u64 pang total]. Custa 10 pang;
    /// o destinatário recebe na hora se estiver no mensageiro ou no lobby, senão no próximo login.
    /// </summary>
    async Task NoteAsync(uint to, string text, byte from)
    {
        var code = ctx.Notes == null ? Domain.Messenger.NoteCode.Failed : await ctx.Notes.SendAsync(Player, to, text);
        Log.Info($"{conn} bilhete para {to} (origem {from}): {code}");
        var w = new PacketWriter(SRelayResult).U16(RelayNote).U32((uint)code);
        conn.Send(code == Domain.Messenger.NoteCode.Ok ? w.U64((ulong)Player.Pang) : w);
        if (code == Domain.Messenger.NoteCode.Ok) await DeliverNotesAsync(to, ctx);
    }

    /// <summary>
    /// Mostra os bilhetes mais novos a quem tem algum ainda não entregue: pelo mensageiro (0x2E/0x103, qualquer tela) ou
    /// pelo game (0xB0, só tratado no lobby). Fora dos dois, ficam para depois.
    /// </summary>
    internal static async Task DeliverNotesAsync(long to, GameContext ctx)
    {
        if (ctx.Notes is not { } svc || await svc.Store.UndeliveredAsync(to) == 0) return;
        PacketWriter w;
        Core.Net.Connection target;
        if (ctx.Messenger?.Find(to) is { } m)
            (w, target) = (Messenger.MessengerContext.Sub(Messenger.MessengerContext.SubNotes, 512), m.Connection);
        else if (ctx.World.Find(to) is GameHandler g && g.Where.Room < 0 && g.player != null)
            (w, target) = (new PacketWriter(SNotes, 512), g.Connection);
        else return;
        var recent = await svc.Store.RecentAsync(to, Domain.Messenger.NoteService.ListSize);
        await svc.Store.MarkDeliveredAsync(to);
        target.Send(Messenger.MessengerContext.NoteList(w, recent));
    }

    /// <summary>Avisa os amigos (0x115) que este jogador começou/terminou uma partida.</summary>
    void MessengerPlaying(bool playing)
    {
        if (ctx.Messenger?.Find(Player.AccountId) is { } m) _ = m.SetPlayingAsync(playing);
    }
}
