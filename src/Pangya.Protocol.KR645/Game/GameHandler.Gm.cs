using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// GM (docs/protocolo/SPEC-chat-gm.md, SPEC-gm-comandos.md): chat azul, /notice, /kick, /disconnect, /visible, /wind,
/// /weather, /giveitem, /goldenbell, F10 (tirar da sala) e /destroy. O cliente libera os comandos pela própria identidade,
/// mas qualquer cliente pode mandar os pacotes, então tudo é conferido aqui e vai para a auditoria.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CNotice = 0x57, CGmCommand = 0x8C, CGmDisconnect = 0x61, CGmDestroyRoom = 0x60, CGmIdentity = 0x41,
        CGmItemDrop = 0x4E, CGmAdminSlot = 0x4C, CGmObserve = 0x5D, CGmJoin = 0x3E, CGmGallery = 0x3F;
    const ushort SNoticeBoard = 0x40, SIdentity = 0x98;
    /// <summary>0x3E: bit 0x80 no tipo 0 = texto azul de GM; tipo 7 = "알림 : texto" (aviso).</summary>
    const byte ChatGm = 0x80, ChatNotice = 7;
    const int IdentityGm = 0x04;
    const ushort GmKick = 10, GmDisconnect = 11, GmVisible = 3, GmWhisper = 4, GmChannel = 5, GmWind = 14, GmWeather = 15,
        GmGiveItem = 18, GmGoldenBell = 19;
    const ushort SWeather = 0x9C;
    /// <summary>Teto por entrega do /giveitem e /goldenbell (GB: 20000).</summary>
    const int GmMaxGive = 20000;

    /// <summary>Estado do toolkit (0x8C 3/4/5: bit0 visível, bit1 sussurro, bit2 canal). Começa visível.</summary>
    ushort gmStatus = 1;

    /// <summary>state do sBriefUserInfo: bit0 = GM visível na lista do lobby.</summary>
    ushort GmVisibleState => (ushort)(gmStatus & 1);

    static PacketWriter WeatherPacket(byte w) => new PacketWriter(SWeather).U8(w).U8(0).U8(0);

    /// <summary>Resposta de texto ao GM ("알림 : ..." no chat dele).</summary>
    void GmReply(string text) => conn.Send(RoomPackets.Chat("", text, ChatNotice));

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
            case CGmDisconnect: p.Skip(p.Remaining); return true;                     // repete o 0x8C 11
            case CGmDestroyRoom: GmDestroy(p.U16()); return true;                      // o 0x8C 13 que vem junto não traz a sala
            case CGmIdentity: GmIdentity(p.U32(), p.Remaining >= 2 ? p.Str(32) : ""); return true;
            case CGmAdminSlot: GmRemoveFromRoom(p.U32()); return true;
            case CGmItemDrop or CGmObserve or CGmJoin or CGmGallery:                   // sem sistema por trás ainda
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
            case GmVisible or GmWhisper or GmChannel when p.Remaining >= 2:
            {
                ushort flags = p.U16();
                bool visibleChanged = ((flags ^ gmStatus) & 1) != 0;
                gmStatus = (ushort)(flags & 7);
                if (visibleChanged)
                    lock (Rooms.Sync)
                    {
                        LobbyUser(LobbyUserUpdate);
                        if (room?.Find(this) is { } me) InGameOutput.Broadcast(room, RoomPackets.SlotUpdate(room, me), except: this);
                    }
                Audit("status", "", $"visível={flags & 1} sussurro={(flags >> 1) & 1} canal={(flags >> 2) & 1}");
                GmReply("Comando executado.");
                return;
            }
            case GmWind when p.Remaining >= 2:
            {
                byte v = p.U8(), d = p.U8();
                byte strength = v == 0xFF ? (byte)0 : (byte)Math.Min((int)v, 8);    // o cliente manda vel-1 (vel 0 = 0xFF)
                lock (Rooms.Sync)
                {
                    if (room?.Game is not StrokeGame { Over: false } g) { GmReply("Vento: so numa partida por turnos."); return; }
                    g.SetWind(strength, d);
                }
                Audit("wind", "", $"{strength} dir {d}");
                GmReply("Comando executado.");
                return;
            }
            case GmWeather when p.Remaining >= 1:
            {
                byte w = p.U8();
                if (w > 3) { GmReply("Clima invalido (0..3)."); return; }
                lock (Rooms.Sync)
                {
                    if (room == null) { GmReply("Clima: so dentro de uma sala."); return; }
                    room.Weather = w;
                    InGameOutput.Broadcast(room, WeatherPacket(w));
                }
                Audit("weather", room?.Index.ToString() ?? "", w.ToString());
                GmReply("Comando executado.");
                return;
            }
            case GmGiveItem when p.Remaining >= 12:
            {
                uint target = p.U32();
                int tid = (int)p.U32(), qty = (int)p.U32();
                _ = GmGiveAsync([target], tid, qty, "giveitem");
                return;
            }
            case GmGoldenBell when p.Remaining >= 8:
            {
                int tid = (int)p.U32(), qty = (int)p.U32();
                var targets = new List<uint>();
                lock (Rooms.Sync)
                    if (room != null)
                        foreach (var rp in room.Players)
                            if (!rp.IsBot) targets.Add((uint)rp.Player.AccountId);
                if (targets.Count == 0) { GmReply("Goldenbell: so dentro de uma sala."); return; }
                _ = GmGiveAsync(targets, tid, qty, "goldenbell");
                return;
            }
            default:
                p.Skip(p.Remaining);
                Log.Info($"{conn} comando de GM {tag} ainda não implementado");
                return;
        }
    }

    /// <summary>/giveitem e /goldenbell: item do IFF (1..20000; objeto único = 1) por carta do sistema para cada alvo.</summary>
    async Task GmGiveAsync(IReadOnlyList<uint> targets, int tid, int qty, string action)
    {
        try
        {
            if (ctx.Mail == null || qty < 1 || qty > GmMaxGive || !ctx.Data.Exists(tid)) { GmReply("Item ou quantidade invalida."); return; }
            if (Domain.Players.Item.GroupOf(tid) is not (Domain.Players.ItemGroup.Ball or Domain.Players.ItemGroup.Usable)) qty = 1;
            int sent = 0;
            foreach (var uid in targets)
            {
                if (ctx.Notes != null && !await ctx.Notes.Store.ExistsAsync(uid)) continue;   // conta com jogador
                await ctx.Mail.SendSystemAsync(uid, "@GM", "Presente do GM", [(tid, qty)]);
                if (ctx.World.Find(uid) is GameHandler h) h.Connection.Send(new PacketWriter(SNewMail));
                sent++;
            }
            Audit(action, string.Join(',', targets), $"0x{tid:X8} x{qty} -> {sent} carta(s)");
            GmReply(sent > 0 ? $"Enviado por carta para {sent} jogador(es)." : "Jogador nao encontrado.");
        }
        catch (Exception e) { Log.Error($"{conn} /{action} falhou", e); }
    }

    /// <summary>0x4C u32 guid (F10 na sala): tira o jogador da sala (não desconecta). Não vale contra GM.</summary>
    void GmRemoveFromRoom(uint guid)
    {
        if (!IsGm) { Log.Warn($"{conn} 0x4C recusado (não é GM)"); return; }
        GameHandler? target = null;
        lock (Rooms.Sync)
        {
            if (room?.Find(guid) is not { IsBot: false } rp || rp.Session is not GameHandler h || h == this) return;
            if ((h.Player.IdentityFlags & IdentityGm) != 0) { GmReply("Nao pode tirar um GM da sala."); return; }
            target = h;
            h.LeaveRoom(notifySelf: true);
        }
        Audit("room-kick", target.Player.Nickname);
        GmReply("Comando executado.");
    }

    /// <summary>0x60 u16 sala (/destroy, fora de sala): tira todos e a sala fecha sozinha quando esvazia.</summary>
    void GmDestroy(ushort index)
    {
        if (!IsGm) { Log.Warn($"{conn} /destroy recusado (não é GM)"); return; }
        int n = 0;
        lock (Rooms.Sync)
        {
            if (Rooms.Get(index) is not { } r) { GmReply("Sala nao encontrada."); return; }
            var humans = new List<GameHandler>();
            foreach (var rp in r.Players) if (rp.Session is GameHandler h) humans.Add(h);
            foreach (var h in humans) { h.LeaveRoom(notifySelf: true); n++; }
        }
        Audit("destroy", index.ToString(), $"{n} jogador(es) fora");
        GmReply("Comando executado.");
    }
}
