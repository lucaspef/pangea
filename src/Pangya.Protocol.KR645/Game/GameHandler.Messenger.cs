using Pangya.Core.Logging;
using Pangya.Core.Net;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// O que o game server faz pelo mensageiro (docs/protocolo/SPEC-messenger.md §7): 0x88 -> 0xFA (lista de mensageiros;
/// o cliente apaga a do login a cada queda do MSN), 0x3C/0x11F (lista de amigos pelo game, para correio e presente) e o
/// status "jogando" dos amigos.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CMessengerServers = 0x88, CMessengerRelay = 0x3C, SMessengerServers = 0xFA, SRelayResult = 0x93;
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
                p.Skip(p.Remaining);
                conn.Send(new PacketWriter(SRelayResult).U16(RelayNote).U32(1));      // 1 = "falha ao mandar o bilhete" (ainda não há bilhetes)
                break;
            default:
                Log.Info($"{conn} 0x3C sub 0x{sub:X} sem tratamento ({p.Remaining} bytes)");
                p.Skip(p.Remaining);
                break;
        }
    }

    /// <summary>Avisa os amigos (0x115) que este jogador começou/terminou uma partida.</summary>
    void MessengerPlaying(bool playing)
    {
        if (ctx.Messenger?.Find(Player.AccountId) is { } m) _ = m.SetPlayingAsync(playing);
    }
}
