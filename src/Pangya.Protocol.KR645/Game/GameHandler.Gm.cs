using Pangya.Core.Logging;
using Pangya.Core.Net;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// GM (docs/protocolo/SPEC-chat-gm.md): chat azul, /notice e /kick, /disconnect. O cliente libera os comandos pela própria
/// identidade, mas qualquer cliente pode mandar os pacotes, então tudo é conferido aqui.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CNotice = 0x57, CGmCommand = 0x8C, CGmDisconnect = 0x61, CGmDestroyRoom = 0x60;
    const ushort SNoticeBoard = 0x40;
    /// <summary>0x3E: bit 0x80 no tipo 0 = texto azul de GM; tipo 7 = "알림 : texto" (aviso).</summary>
    const byte ChatGm = 0x80, ChatNotice = 7;
    const int IdentityGm = 0x04;
    const ushort GmKick = 10, GmDisconnect = 11;

    bool IsGm => (Player.IdentityFlags & IdentityGm) != 0;

    bool HandleGm(PacketReader p)
    {
        switch (p.Id)
        {
            case CNotice: Notice(p.Str(256)); return true;
            case CGmCommand: GmCommand(p); return true;
            case CGmDisconnect or CGmDestroyRoom: p.Skip(p.Remaining); return true;    // repetem o 0x8C 11/13
            default: return false;
        }
    }

    /// <summary>/notice: letreiro no topo (0x40) e "알림 : texto" no chat de todos os online.</summary>
    void Notice(string text)
    {
        text = text.Trim();
        if (!IsGm || text.Length == 0) { Log.Warn($"{conn} 0x57 aviso recusado (não é GM)"); return; }
        Log.Info($"{conn} aviso de GM: {text}");
        var board = new PacketWriter(SNoticeBoard).Str(text);
        var chat = RoomPackets.Chat("", text, ChatNotice);
        foreach (var s in ctx.World.Online)
        {
            if (s is not GameHandler h) continue;
            h.Connection.Send(board.Body);
            h.Connection.Send(chat.Body);
        }
    }

    /// <summary>0x8C u16 comando + dados. Por enquanto: 10 kick (u32 guid, u8 -p) e 11 disconnect (u32 guid).</summary>
    void GmCommand(PacketReader p)
    {
        if (p.Remaining < 2) return;
        ushort tag = p.U16();
        if (!IsGm) { p.Skip(p.Remaining); Log.Warn($"{conn} comando de GM {tag} recusado (não é GM)"); return; }
        switch (tag)
        {
            case GmKick or GmDisconnect:
            {
                uint guid = p.U32();
                p.Skip(p.Remaining);
                var target = ctx.World.Find(guid);
                if (target == null) { conn.Send(new PacketWriter(0x3F).Str("jogador nao encontrado")); return; }
                if ((target.Player.IdentityFlags & IdentityGm) != 0) { conn.Send(new PacketWriter(0x3F).Str("nao pode expulsar um GM")); return; }
                Log.Info($"{conn} GM {Player.Login} expulsou {target.Player.Login}");
                target.Kick($"expulso pelo GM {Player.Login}");
                return;
            }
            default:
                p.Skip(p.Remaining);
                Log.Info($"{conn} comando de GM {tag} ainda não implementado");
                return;
        }
    }
}
