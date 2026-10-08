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
            default: return false;
        }
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
