using Pangya.Core.Logging;
using Pangya.Core.Net;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// GM (docs/protocolo/SPEC-chat-gm.md): chat azul, /notice e /kick, /disconnect. O cliente libera os comandos pela própria
/// identidade, mas qualquer cliente pode mandar os pacotes, então tudo é conferido aqui.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CNotice = 0x57, CGmCommand = 0x8C, CGmDisconnect = 0x61, CGmDestroyRoom = 0x60, CGmIdentity = 0x41,
        CGmItemDrop = 0x4E, CGmAdminSlot = 0x4C, CGmObserve = 0x5D, CGmJoin = 0x3E, CGmGallery = 0x3F;
    const ushort SNoticeBoard = 0x40, SIdentity = 0x98;
    /// <summary>0x3E: bit 0x80 no tipo 0 = texto azul de GM; tipo 7 = "알림 : texto" (aviso).</summary>
    const byte ChatGm = 0x80, ChatNotice = 7;
    const int IdentityGm = 0x04;
    const ushort GmKick = 10, GmDisconnect = 11;

    bool IsGm => (Player.IdentityFlags & IdentityGm) != 0;

    /// <summary>Registra a ação de GM na auditoria (sem esperar; falha só vira log).</summary>
    void Audit(string action, string target, string details = "") =>
        _ = ctx.Audit?.WriteAsync(Player.AccountId, Player.Nickname, "gm:" + action, target, details);

    bool HandleGm(PacketReader p)
    {
        switch (p.Id)
        {
            case CNotice: Notice(p.Str(256)); return true;
            case CGmCommand: GmCommand(p); return true;
            case CGmDisconnect or CGmDestroyRoom: p.Skip(p.Remaining); return true;    // repetem o 0x8C 11/13
            case CGmIdentity: GmIdentity(p.U32(), p.Remaining >= 2 ? p.Str(32) : ""); return true;
            case CGmItemDrop or CGmAdminSlot or CGmObserve or CGmJoin or CGmGallery:     // sem sistema por trás ainda
                Log.Info($"{conn} comando de GM 0x{p.Id:X2} ({(IsGm ? "GM" : "recusado: não é GM")}) ignorado");
                p.Skip(p.Remaining);
                return true;
            default: return false;
        }
    }

    /// <summary>
    /// 0x41 u32 identidade (0xFFFFFFFF = consultar), str nick (/identity admin|user): só GM, só a própria visão. Responde
    /// 0x98 u32 ao próprio cliente (ver o jogo como jogador comum, por exemplo) sem gravar: a identidade da conta não
    /// muda, então o GM não perde o acesso por engano.
    /// </summary>
    void GmIdentity(uint value, string nick)
    {
        if (!IsGm || (nick.Length > 0 && !string.Equals(nick, Player.Nickname, StringComparison.OrdinalIgnoreCase)))
        {
            Log.Warn($"{conn} /identity recusado (GM={IsGm}, alvo '{nick}')");
            return;
        }
        uint shown = value == 0xFFFFFFFF ? (uint)Player.IdentityFlags : value;
        conn.Send(new PacketWriter(SIdentity).U32(shown));
        Audit("identity", Player.Nickname, $"0x{shown:X} (só na tela)");
        Log.Info($"{conn} /identity -> 0x{shown:X} (só na tela; conta continua 0x{Player.IdentityFlags:X})");
    }

    /// <summary>/notice: letreiro no topo (0x40) e "알림 : texto" no chat de todos os online.</summary>
    void Notice(string text)
    {
        text = text.Trim();
        if (!IsGm || text.Length == 0) { Log.Warn($"{conn} 0x57 aviso recusado (não é GM)"); return; }
        Log.Info($"{conn} aviso de GM: {text}");
        Audit("notice", "", text);
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
                Audit(tag == GmKick ? "kick" : "disconnect", target.Player.Nickname);
                return;
            }
            default:
                p.Skip(p.Remaining);
                Log.Info($"{conn} comando de GM {tag} ainda não implementado");
                return;
        }
    }
}
