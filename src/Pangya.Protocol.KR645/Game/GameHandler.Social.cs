using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Pacotes pequenos de sala/lobby e de itens (SPEC-coverage.md do emulador): ícone sobre a cabeça, expulsar, detalhe da
/// sala, ausente, desistir no solo, chat de equipe, apagar item, trocar nick e recontratar caddie.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CHeadIcon = 0x18, CBanish = 0x26, CBanishVote = 0x27, CRoomDetail = 0x2D, CIdle = 0x32, CGiveUpSolo = 0x37,
        CChangeNick = 0x38, CCaddieRehire = 0x39, CTeamChat = 0x54, CDeleteItem = 0x64;
    const ushort CWhisper = 0x2A, CWhisperRejected = 0xE0, CInvite = 0xB2, CInviteAck = 0x29, CDirectMove = 0xAC,
        CQuickInterests = 0xB6, CQuickMatch = 0xB7, CQuickAnswer = 0xB8, CPrivateTrade = 0xDB;
    const ushort SWhisper = 0x82, SInviteResult = 0x127, SInvited = 0x81, SQuickMatch = 0x133;
    /// <summary>0x3E tipos de aviso de sussurro: 4 não recebe, 6 não está conectado.</summary>
    const byte ChatNoWhisper = 4, ChatNotConnected = 6;
    /// <summary>0x127 códigos: 2 sala cheia, 6 já está numa sala, 0x17 não está num lugar onde dá para convidar.</summary>
    const ushort InviteRoomFull = 2, InviteInRoom = 6, InviteNotHere = 0x17;
    const ushort SHeadIcon = 0x5B, SRoomDetail = 0x84, STeamChat = 0xAE, SChangeNick = 0x4E, SCaddieRehire = 0x91;

    async ValueTask<bool> HandleSocialAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CHeadIcon: HeadIcon(p.U16()); return true;
            case CBanish: Banish(p.U32()); return true;
            case CBanishVote or CIdle: p.Skip(p.Remaining); return true;            // sem votação; ausente é só visual
            case CRoomDetail: RoomDetail(p.U16()); return true;
            case CGiveUpSolo: GiveUpSolo(); return true;
            case CTeamChat: TeamChat(p.Str(256)); return true;
            case CDeleteItem: await DeleteItemAsync((int)p.U32(), (int)p.U32()); return true;
            case CChangeNick:                                                        // o nick é a chave da conta: ainda não
                p.Skip(p.Remaining);
                conn.Send(new PacketWriter(SChangeNick).U32(7));                      // 7 = "troca de nick suspensa"
                return true;
            case CCaddieRehire: CaddieRehire(p.U32()); return true;
            case CWhisper: Whisper(p.Str(32), p.Str(256)); return true;
            case CWhisperRejected:
                if (FindOnline(p.Str(32)) is { } from && from != this)
                    from.Connection.Send(RoomPackets.Chat(Player.Nickname, "", ChatNoWhisper));
                return true;
            case CInvite: Invite(p.Str(32), p.U32()); return true;
            case CInviteAck: InviteAck(p.U32()); return true;
            case CDirectMove: p.U8(); JoinRoom(p.U16(), ""); return true;
            case CQuickInterests or CQuickAnswer: p.Skip(p.Remaining); return true;
            case CPrivateTrade:                     // troca direta: desligada no cliente 645 (conteúdo 0x77; SPEC-troca-direta.md)
                Log.Info($"{conn} troca direta (0xDB) ignorada: precisa do cliente com o conteúdo 0x77 ligado");
                p.Skip(p.Remaining);
                return true;
            case CQuickMatch: p.Skip(p.Remaining); conn.Send(new PacketWriter(SQuickMatch).U8(1)); return true;   // 1 = sem alvo
            default: return false;
        }
    }

    GameHandler? FindOnline(string nick)
    {
        foreach (var s in ctx.World.Online)
            if (s is GameHandler h && h.player != null && string.Equals(h.player.Nickname, nick, StringComparison.OrdinalIgnoreCase)) return h;
        return null;
    }

    /// <summary>
    /// 0x2A str nick, str texto (sussurro) -> 0x82 u8 0 (eco), nick do alvo, texto para mim e 0x82 u8 1, meu nick, texto
    /// para o alvo (bit 0x80 = cor de GM); alvo fora: 0x3E tipo 6 "não está conectado".
    /// </summary>
    void Whisper(string nick, string text)
    {
        if (text.Length == 0) return;
        var t = FindOnline(nick);
        if (t == null) { conn.Send(RoomPackets.Chat(nick, "", ChatNotConnected)); return; }
        byte gm = IsGm ? ChatGm : (byte)0;
        conn.Send(new PacketWriter(SWhisper).U8(gm).Str(t.Player.Nickname).Str(text));
        if (t != this) t.Connection.Send(new PacketWriter(SWhisper).U8((byte)(1 | gm)).Str(Player.Nickname).Str(text));
    }

    /// <summary>
    /// 0xB2 str nick, u32 uid: convidar para a minha sala. 0x127 u16 0, u32 servidor, u8 canal, u16 sala, u32 uid, str nick,
    /// u32 convite (o cliente confirma com 0x29) ou só o código (2 cheia, 6 já em sala, 0x17 não dá para convidar).
    /// </summary>
    void Invite(string nick, uint uid)
    {
        lock (Rooms.Sync)
        {
            var r = room;
            var t = (ctx.World.Find(uid) as GameHandler) ?? FindOnline(nick);
            ushort code = r == null || t == null || t == this ? InviteNotHere
                : t.room != null ? InviteInRoom
                : r.Players.Count >= r.Settings.MaxPlayers ? InviteRoomFull : (ushort)0;
            if (code != 0) { conn.Send(new PacketWriter(SInviteResult).U16(code)); return; }
            uint id = ctx.NewInvite(this, t!, r!.Index);
            conn.Send(new PacketWriter(SInviteResult).U16(0).U32((uint)ctx.World.Config.Id).U8((byte)(channel?.Id ?? 0))
                .U16((ushort)r.Index).U32((uint)t!.Player.AccountId).Str(t.Player.Nickname).U32(id));
        }
    }

    /// <summary>0x29 u32 convite -> 0x81 para o convidado: u16 0, u32 servidor, u8 canal, u16 sala, u32 uid, str nick, u32 id.</summary>
    void InviteAck(uint id)
    {
        if (ctx.TakeInvite(id) is not { } inv || inv.From != this) return;
        inv.To.Connection.Send(new PacketWriter(SInvited).U16(0).U32((uint)ctx.World.Config.Id).U8((byte)(channel?.Id ?? 0))
            .U16((ushort)inv.Room).U32((uint)Player.AccountId).Str(Player.Nickname).U32(id));
    }

    /// <summary>0x18 u16 ícone (0xFFFF limpa) -> 0x5B u32 guid, u16 para os outros da sala.</summary>
    void HeadIcon(ushort icon)
    {
        lock (Rooms.Sync)
            if (room != null) InGameOutput.Broadcast(room, new PacketWriter(SHeadIcon).U32((uint)Player.AccountId).U16(icon), except: this);
    }

    /// <summary>0x26 u32 uid: o dono tira alguém da sala (o bot também). O expulso volta para a lista (0x4A).</summary>
    void Banish(uint uid)
    {
        GameHandler? target = null;
        lock (Rooms.Sync)
        {
            var r = room;
            if (r == null || r.State != RoomState.Waiting || r.Find(this) is not { Master: true } || uid == Player.AccountId) return;
            var rp = r.Find(uid);
            if (rp == null) return;
            if (rp.IsBot) { RemoveBot(); return; }
            target = rp.Session as GameHandler;
            target?.LeaveRoom(notifySelf: true);
        }
        if (target != null) Log.Info($"{conn} expulsou {target.Player.Login} da sala");
    }

    /// <summary>
    /// 0x2D u16 sala -> 0x84 u8 n (0 = "a sala não existe"), sRoomDetail 12 B {u8 buracos, u32 tempo de jogo, u8 mapa,
    /// u8 modo, u8 ordem dos buracos, u32 troféu}, n × {u32 uid, u8 nível, u8 buraco, u32 capacidade, u32 0, u32 ladder}.
    /// </summary>
    void RoomDetail(ushort index)
    {
        lock (Rooms.Sync)
        {
            var r = Rooms.Get(index);
            if (r == null) { conn.Send(new PacketWriter(SRoomDetail).U8(0)); return; }
            var s = r.Settings;
            var w = new PacketWriter(SRoomDetail, 16 + r.Players.Count * 18).U8((byte)r.Players.Count)
                .U8(s.Holes).U32(s.GameTimeMs).U8(s.Course).U8((byte)s.Mode).U8(s.HoleType).U32(0);
            foreach (var p in r.Players)
                w.U32((uint)p.Player.AccountId).U8((byte)p.Player.Level).U8(0).U32(0).U32(0).U32(0);
            conn.Send(w);
        }
    }

    /// <summary>0x37: desistir da partida stroke sozinho ("desistir e voltar para a sala"): placar e fim, sem recompensa.</summary>
    void GiveUpSolo()
    {
        lock (Rooms.Sync)
            if (room?.Game is StrokeGame g && room.Settings.Mode == GameMode.Stroke && g.GiveUp((uint)Player.AccountId))
                Log.Info($"{conn} desistiu da partida solo");
    }

    /// <summary>0x54 str -> 0xAE str nick, str texto para os da mesma equipe ("nick>팀에게 : texto").</summary>
    void TeamChat(string text)
    {
        lock (Rooms.Sync)
        {
            var r = room;
            if (r?.Find(this) is not { } me || text.Length == 0) return;
            var w = new PacketWriter(STeamChat).Str(Player.Nickname).Str(text);
            foreach (var p in r.Players)
                if (p.Team == me.Team && p.Session is GameHandler h) h.Connection.Send(w.Body);
            w.Dispose();
        }
    }

    /// <summary>
    /// 0x64 u32 tid, u32 quantidade (apagar item, My Room). Sem resposta própria no cliente: 0xA5 com a contagem nova
    /// (0 apaga) e 0xA8 vazio para fechar a espera.
    /// </summary>
    async Task DeleteItemAsync(int tid, int count)
    {
        var it = await ctx.Actions.DeleteItemAsync(Player, tid, count);
        if (it != null) conn.Send(new PacketWriter(SItemCounts).U8(1).U32((uint)it.TypeId).U32((uint)it.Id).U16((ushort)Math.Max(it.Quantity, 0)));
        conn.Send(new PacketWriter(SBought).U16(0));                         // 0xA8 vazio: fecha a espera
        Log.Info($"{conn} apagar item {tid:X8} x{count}: {(it == null ? "recusado" : $"sobrou {it.Quantity}")}");
    }

    /// <summary>0x39 u32 caddie: recontratar. Os caddies aqui não vencem, então é de graça: 0x91 u8 2, u32 caddie, u64 pang.</summary>
    void CaddieRehire(uint id)
    {
        bool mine = Player.Find((int)id) is { Group: Domain.Players.ItemGroup.Caddie };
        conn.Send(mine ? new PacketWriter(SCaddieRehire).U8(2).U32(id).U64((ulong)Player.Pang) : new PacketWriter(SCaddieRehire).U8(4));
    }
}
