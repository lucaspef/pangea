using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Lista de salas e sala de espera (docs/protocolo/SPEC-room.md).</summary>
public sealed partial class GameHandler
{
    // ids C->S
    const ushort CEnterRoomList = 0x81, CLeaveRoomList = 0x82, CMakeRoom = 0x08, CJoinRoom = 0x09, CChangeRoom = 0x0A,
        CReady = 0x0D, CStart = 0x0E, CLeaveRoom = 0x0F, CTeam = 0x10, CChat = 0x03, CGameOptions = 0x69, CRoomAction = 0x63;

    Room? room;
    bool slotsResent;
    RoomManager Rooms => ctx.World.Rooms;

    /// <summary>Trata os pacotes de sala; false = não é de sala.</summary>
    async ValueTask<bool> HandleRoomAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CEnterRoomList:
                lock (Rooms.Sync)
                {
                    Rooms.Lobby.Add(this);
                    conn.Send(new PacketWriter(RoomPackets.SRoomListOn));
                    conn.Send(RoomPackets.RoomListAll(Rooms));
                    conn.Send(new PacketWriter(RoomPackets.SLobbyUsers).U8(1).U8(1).Struct(RoomPackets.BriefUser(Player)));
                }
                return true;
            case CLeaveRoomList:
                lock (Rooms.Sync) Rooms.Lobby.Remove(this);
                conn.Send(new PacketWriter(RoomPackets.SRoomListOff));
                return true;
            case CMakeRoom: MakeRoom(p); return true;
            case CJoinRoom: JoinRoom(p.U16(), p.Str(16)); return true;
            case CChangeRoom: ChangeRoom(p); return true;
            case CReady: SetReady(p.U8() == 0); return true;
            case CStart: p.U32(); Start(); return true;
            case CLeaveRoom: lock (Rooms.Sync) LeaveRoom(notifySelf: true); return true;
            case CTeam: SetTeam(p.U8()); return true;
            case CChat: await ChatAsync(p.Str(32), p.Str(256)); return true;
            case CGameOptions: p.Skip(p.Remaining); return true;           // opções e macros: sem resposta
            case CRoomAction: ResendSlotsOnce(); return true;
            default: return false;
        }
    }

    void Lobby(PacketWriter w)
    {
        foreach (var s in Rooms.Lobby) ((GameHandler)s).Connection.Send(w.Body);
        w.Dispose();
    }

    void MakeRoom(PacketReader p)
    {
        p.U8();                                                 // quick
        var s = new RoomSettings { ShotTimeMs = p.U32(), GameTimeMs = p.U32(), MaxPlayers = p.U8(), Mode = (GameMode)p.U8(), Holes = p.U8(), Course = p.U8(), HoleType = p.U8() };
        s.Title = p.Str(64);
        s.Password = p.Str(32);
        lock (Rooms.Sync)
        {
            if (room != null) LeaveRoom(notifySelf: false);
            var r = Rooms.Create(s, Player.AccountId);
            Log.Info($"{conn} criou a sala {r.Index}: modo={r.Settings.Mode} mapa={r.Settings.Course} buracos={r.Settings.Holes} '{r.Settings.Title}'");
            EnterRoom(r);
        }
    }

    void JoinRoom(ushort index, string password)
    {
        lock (Rooms.Sync)
        {
            if (room != null) return;
            var r = Rooms.Get(index);
            var res = RoomManager.CanJoin(r, password);
            if (res != JoinResult.Ok) { conn.Send(RoomPackets.EnterRoomFailed(res)); return; }
            EnterRoom(r!);
        }
    }

    /// <summary>Entra na sala: avisa quem já está, manda a sala completa (0x47, 0x48, 0x46) e atualiza a lista.</summary>
    void EnterRoom(Room r)
    {
        var rp = RoomManager.Join(r, new RoomPlayer { Guid = (uint)Player.AccountId, Player = Player, Session = this });
        room = r;
        slotsResent = false;
        Rooms.Lobby.Remove(this);
        InGameOutput.Broadcast(r, RoomPackets.SlotAdd(r, rp), except: this);
        conn.Send(RoomPackets.EnterRoom(r));
        conn.Send(RoomPackets.Settings(r));
        conn.Send(RoomPackets.SlotsFull(r));
        Lobby(RoomPackets.RoomList(1, r));
    }

    /// <summary>Sai da sala (chamado sob o lock). notifySelf: volta o cliente para a lista de salas.</summary>
    void LeaveRoom(bool notifySelf)
    {
        var r = room;
        if (r == null) return;
        room = null;
        var rp = r.Find(this);
        if (rp != null)
        {
            var (newMaster, closed) = Rooms.Leave(r, rp);
            if (closed)
            {
                Lobby(RoomPackets.RoomList(2, r));
                Log.Info($"sala {r.Index} fechada");
            }
            else
            {
                InGameOutput.Broadcast(r, RoomPackets.SlotRemove(r, rp.Guid));
                if (newMaster != null) InGameOutput.Broadcast(r, new PacketWriter(RoomPackets.SNewMaster).U32(newMaster.Guid).U16((ushort)r.Index));
                Lobby(RoomPackets.RoomList(3, r));
            }
        }
        if (!notifySelf) return;
        conn.Send(new PacketWriter(RoomPackets.SLeftRoom).U16(0xFFFF));     // volta para ROOMLIST (ou TOPPAGE)
        Rooms.Lobby.Add(this);
        conn.Send(RoomPackets.RoomListAll(Rooms));
    }

    /// <summary>0x0A: u16 0xFFFF, u8 n, n × (u8 chave, valor). Só o dono muda a sala.</summary>
    void ChangeRoom(PacketReader p)
    {
        p.U16();
        int n = p.U8();
        lock (Rooms.Sync)
        {
            var r = room;
            if (r == null || r.State != RoomState.Waiting || r.Find(this) is not { Master: true }) return;
            var s = r.Settings;
            for (int i = 0; i < n; i++)
            {
                switch (p.U8())
                {
                    case 0: s.Title = p.Str(64); break;
                    case 1: s.Password = p.Str(32); break;
                    case 2: s.Mode = (GameMode)p.U8(); break;
                    case 3: s.Course = Rooms.ValidCourse(p.U8()); break;
                    case 4: s.Holes = p.U8(); break;
                    case 5: s.HoleType = p.U8(); break;
                    case 6: s.ShotTimeMs = p.U8() * 1000u; break;
                    case 7: s.MaxPlayers = p.U8(); break;
                    case 8: { var v = p.U8(); s.GameTimeMs = v * (s.Mode == GameMode.NewApproach ? 1000u : 60000u); break; }
                    case 9: s.Sleep = p.U8() != 0; break;
                    default: i = n; break;                       // chave desconhecida: para de ler
                }
            }
            s.Normalize();
            InGameOutput.Broadcast(r, RoomPackets.Settings(r));
            Lobby(RoomPackets.RoomList(3, r));
        }
    }

    void SetReady(bool ready)
    {
        lock (Rooms.Sync)
        {
            if (room?.Find(this) is not { } me || room.State != RoomState.Waiting) return;
            me.Ready = ready;
            InGameOutput.Broadcast(room, new PacketWriter(RoomPackets.SReady).U32(me.Guid).U8((byte)(ready ? 0 : 1)));
        }
    }

    void SetTeam(byte team)
    {
        lock (Rooms.Sync)
        {
            if (room?.Find(this) is not { } me) return;
            me.Team = (byte)(team & 3);
            InGameOutput.Broadcast(room, new PacketWriter(RoomPackets.STeam).U32(me.Guid).U8(me.Team));
        }
    }

    void Start()
    {
        lock (Rooms.Sync)
        {
            var r = room;
            if (r == null || r.State != RoomState.Waiting) return;
            if (r.Find(this) is not { Master: true }) { conn.Send(new PacketWriter(RoomPackets.SStartFailed).U8(1)); return; }
            RoomManager.PrepareStart(r, Random.Shared, Rooms.Courses);
            var cfg = ctx.World.Config;
            var botDelay = TimeSpan.FromSeconds(cfg.BotDelaySeconds);
            r.Game = MassGame.IsMass(r.Settings.Mode)
                ? MassGame.For(r, new MassOutput(r), Rooms.Sync, botDelay)
                : StrokeGame.For(r, new InGameOutput(r, cfg.BotPasses, new BotGolfer(Random.Shared, cfg.BotAccuracy)), Rooms.Sync,
                    botDelay, TimeSpan.FromSeconds(cfg.TeeFallbackSeconds));
            InGameOutput.Broadcast(r, RoomPackets.GamePlayers(r, ctx.Data.Cards));
            InGameOutput.Broadcast(r, RoomPackets.GameInit(r));         // o cliente troca para a tela da partida
            Lobby(RoomPackets.RoomList(3, r));
            Log.Info($"sala {r.Index}: início mapa={r.CoursePlayed} buracos={r.Settings.Holes} jogadores={r.Players.Count}");
        }
    }

    /// <summary>Chat (0x03 str nick, str texto). Comandos na sala: !bot / !bot off.</summary>
    async Task ChatAsync(string _, string text)
    {
        var cmd = text.Trim().ToLowerInvariant();
        if (cmd is "!bot" or "/bot" or "!bot on")
        {
            var bot = await ctx.Players.CreateBotAsync();
            lock (Rooms.Sync) AddBot(bot);
            return;
        }
        if (cmd is "!bot off" or "/bot off" or "!nobot")
        {
            lock (Rooms.Sync) RemoveBot();
            return;
        }
        lock (Rooms.Sync)
        {
            var w = RoomPackets.Chat(Player.Nickname, text, IsGm ? ChatGm : (byte)0);   // bit 0x80 = texto azul de GM
            if (room != null) { InGameOutput.Broadcast(room, w); return; }
            foreach (var s in Rooms.Lobby)
                if (s != this) ((GameHandler)s).Connection.Send(w.Body);
            conn.Send(w);
        }
    }

    void AddBot(Domain.Players.Player bot)
    {
        var r = room;
        if (r == null || r.State != RoomState.Waiting || r.Bot != null) return;
        if (r.Players.Count >= Math.Min((int)r.Settings.MaxPlayers, 4))
        {
            conn.Send(RoomPackets.Chat("Server", "sala cheia"));
            return;
        }
        var rp = RoomManager.Join(r, new RoomPlayer { Guid = (uint)bot.AccountId, Player = bot, Ready = true, Team = 1 });
        InGameOutput.Broadcast(r, RoomPackets.SlotAdd(r, rp));
        Lobby(RoomPackets.RoomList(3, r));
    }

    void RemoveBot()
    {
        var r = room;
        if (r?.Bot is not { } bot || r.State != RoomState.Waiting) return;
        r.Remove(bot);
        InGameOutput.Broadcast(r, RoomPackets.SlotRemove(r, bot.Guid));
        Lobby(RoomPackets.RoomList(3, r));
    }

    /// <summary>
    /// 0x63 é o primeiro pacote que o cliente manda de dentro da sala: o avatar só é desenhado quando a tela da sala
    /// existe (lobbymain.cpp:3189/7149), então a lista de slots é reenviada uma vez.
    /// </summary>
    void ResendSlotsOnce()
    {
        lock (Rooms.Sync)
        {
            if (room == null || room.State != RoomState.Waiting || slotsResent) return;
            slotsResent = true;
            conn.Send(RoomPackets.SlotsFull(room));
        }
    }
}
