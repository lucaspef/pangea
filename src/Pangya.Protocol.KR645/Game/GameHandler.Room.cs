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
                    bool already = Rooms.Lobby.Contains(this);
                    if (!already) LobbyUser(LobbyUserAdd);                      // os outros me veem
                    Rooms.Lobby.Add(this);
                    conn.Send(new PacketWriter(RoomPackets.SRoomListOn));
                    conn.Send(RoomPackets.RoomListAll(Rooms));
                    SendLobbyUsers();
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
            case CRoomAction: RoomAction(p); return true;
            case CAvatarData: LoungeSpState(p.U32()); p.Skip(p.Remaining); return true;   // lounge: estado SP do avatar
            default: return false;
        }
    }

    /// <summary>0x44 sub: 1 entrou na lista, 2 saiu, 3 atualiza (sala em que está; 0xFFFF = no lobby).</summary>
    const byte LobbyUserAdd = 1, LobbyUserRemove = 2, LobbyUserUpdate = 3;
    const int MaxLobbyUsers = 250;                                         // 0xC4 cada: cabe num pacote

    /// <summary>Minha entrada na lista de usuários de quem está na lista de salas (eu inclusive, se estiver).</summary>
    void LobbyUser(byte sub) =>
        Lobby(new PacketWriter(RoomPackets.SLobbyUsers).U8(sub).U8(1).Struct(RoomPackets.BriefUser(Player, (ushort)(room?.Index ?? 0xFFFF), GmVisibleState)));

    /// <summary>Lista completa do meu canal (no lobby e em salas), com a sala de cada um.</summary>
    void SendLobbyUsers()
    {
        var list = new List<GameHandler>();
        foreach (var s in ctx.World.Online)
            if (s is GameHandler h && h.player != null && h.channel == channel && list.Count < MaxLobbyUsers) list.Add(h);
        var w = new PacketWriter(RoomPackets.SLobbyUsers, 8 + list.Count * 0xC4).U8(LobbyUserAdd).U8((byte)list.Count);
        foreach (var h in list) w.Struct(RoomPackets.BriefUser(h.Player, (ushort)(h.room?.Index ?? 0xFFFF), h.GmVisibleState));
        conn.Send(w);
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
        if (s.Mode == GameMode.GuildMatch && (Player.Guild is not { } g || !Domain.Guilds.GuildClass.IsMember(g.Class)))
        {
            conn.Send(RoomPackets.EnterRoomFailed(JoinResult.GuildRequired));   // "길드에 가입해야 합니다."
            return;
        }
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
            var res = RoomManager.CanJoin(r, password, Player);
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
        LobbyUser(LobbyUserUpdate);                                         // a lista do lobby mostra a sala
        // GuildMatch: quem já está só aprende a guilda nova pelo 0x45 da própria sala (antes do slot, para achar o lado)
        if (r.Settings.Mode == GameMode.GuildMatch) InGameOutput.Broadcast(r, RoomPackets.RoomList(3, r), except: this);
        InGameOutput.Broadcast(r, RoomPackets.SlotAdd(r, rp), except: this);
        conn.Send(RoomPackets.EnterRoom(r));
        conn.Send(RoomPackets.Settings(r));
        conn.Send(RoomPackets.SlotsFull(r));
        if (IsLounge(r)) StartLoungeSync(r);                                // a avatar task ainda não existia: reenvia
        Lobby(RoomPackets.RoomList(1, r));
    }

    /// <summary>Sai da sala (chamado sob o lock). notifySelf: volta o cliente para a lista de salas.</summary>
    void LeaveRoom(bool notifySelf)
    {
        var r = room;
        if (r == null) return;
        CloseMyShopLocked();                                                // a loja fecha com quem sai do lounge
        LeaveVisitedShopLocked();
        room = null;
        MessengerPlaying(false);
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
                if (r.Settings.Mode == GameMode.GuildMatch) InGameOutput.Broadcast(r, RoomPackets.RoomList(3, r));   // lado pode ter esvaziado
                InGameOutput.Broadcast(r, RoomPackets.SlotRemove(r, rp.Guid));
                if (newMaster != null) InGameOutput.Broadcast(r, new PacketWriter(RoomPackets.SNewMaster).U32(newMaster.Guid).U16(RoomPackets.SlotKey(r)));
                Lobby(RoomPackets.RoomList(3, r));
            }
        }
        if (!notifySelf) return;
        conn.Send(new PacketWriter(RoomPackets.SLeftRoom).U16(0xFFFF));     // volta para ROOMLIST (ou TOPPAGE)
        Rooms.Lobby.Add(this);
        conn.Send(RoomPackets.RoomListAll(Rooms));
        LobbyUser(LobbyUserUpdate);                                         // fora da sala, para todos e para mim
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
                    case 2:                                                  // GuildMatch só ao criar (lados e regras próprias)
                    {
                        var m = (GameMode)p.U8();
                        if (m != GameMode.GuildMatch && s.Mode != GameMode.GuildMatch) s.Mode = m;
                        break;
                    }
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
            if (room?.Find(this) is not { } me || room.Settings.Mode == GameMode.GuildMatch) return;   // GuildMatch: time = guilda
            me.Team = (byte)(team & 3);
            InGameOutput.Broadcast(room, new PacketWriter(RoomPackets.STeam).U32(me.Guid).U8(me.Team));
        }
    }

    /// <summary>GuildMatch: duas guildas, os dois lados com a mesma quantidade (≥ 1). null = pode começar.</summary>
    static byte? GuildStartError(Room r)
    {
        if (r.GuildSides[0] == null || r.GuildSides[1] == null) return 1;
        int red = 0, blue = 0;
        foreach (var p in r.Players)
            if (!p.IsBot) { if (p.Team == 0) red++; else blue++; }
        return red == 0 || blue == 0 ? (byte)1 : red != blue ? (byte)2 : null;
    }

    void Start()
    {
        lock (Rooms.Sync)
        {
            var r = room;
            if (r == null || r.State != RoomState.Waiting) return;
            if (r.Find(this) is not { Master: true } || IsLounge(r)) { conn.Send(new PacketWriter(RoomPackets.SStartFailed).U8(1)); return; }
            if (r.Settings.Mode == GameMode.GuildMatch && GuildStartError(r) is { } err)
            {
                conn.Send(new PacketWriter(RoomPackets.SStartFailed).U8(err));     // 1 falta gente/guilda, 2 times desiguais
                return;
            }
            RoomManager.PrepareStart(r, Random.Shared, Rooms.Courses);
            var cfg = ctx.World.Config;
            var botDelay = TimeSpan.FromSeconds(cfg.BotDelaySeconds);
            // aprendizado guardado do nível (memória dos buracos deste mapa + calibração); cresce a cada partida
            var golfer = BotGolfer.For(r.BotLevel, Random.Shared, cfg.BotAccuracy, ctx.BotKnowledge.For(r.BotLevel));
            golfer.Course = r.CoursePlayed;
            if (r.Bot is { } bot)                                       // kit do nível: vai no 0x74 e muda a física nos clientes
            {
                ctx.Players.EquipBot(bot.Player, r.BotLevel);
                var (stats, driveUp) = ctx.Data.PlayStats(bot.Player);
                (golfer.PowerStat, golfer.DriveUp, golfer.AccuracyStat) = (stats[0], driveUp, stats[2]);
                foreach (var it in bot.Player.Equip.ItemSlots) if (it != 0) golfer.Items.Add(it);
                Log.Info($"sala {r.Index}: bot {r.BotLevel} nível={bot.Player.Level} stats={string.Join('/', stats)} anéis=+{driveUp}jd");
            }
            r.Game = MassGame.IsMass(r.Settings.Mode)
                ? MassGame.For(r, new MassOutput(r, cfg.TreasureHunter), Rooms.Sync, botDelay, cfg.Rewards.TrophiesCountBots)
                : StrokeGame.For(r, new InGameOutput(r, cfg.BotPasses, golfer, cfg.BotFastForward, TimeSpan.FromSeconds(cfg.BotFastForwardDelaySeconds), cfg.TreasureHunter), Rooms.Sync,
                    botDelay, TimeSpan.FromSeconds(cfg.TeeFallbackSeconds));
            InGameOutput.Broadcast(r, RoomPackets.GamePlayers(r, ctx.Data.Cards));
            if (r.Game is TourneyGame { Pairs.Count: > 0 } tg)          // GuildMatch: pares antes do 0x50 (só o lobby trata)
                InGameOutput.Broadcast(r, MassOutput.GuildPairs(tg.Pairs));
            InGameOutput.Broadcast(r, RoomPackets.GameInit(r));         // o cliente troca para a tela da partida
            foreach (var rp in r.Players)                               // amigos do mensageiro: "jogando"
                if (rp.Session is GameHandler gh) gh.MessengerPlaying(true);
            Lobby(RoomPackets.RoomList(3, r));
            Log.Info($"sala {r.Index}: início mapa={r.CoursePlayed} buracos={r.Settings.Holes} jogadores={r.Players.Count}");
        }
    }

    /// <summary>
    /// Chat (0x03 str nick, str texto). Comandos na sala: !bot, !bot off, !bot nível (easy/normal/hard/veryhard/impossible;
    /// também facil/dificil/muitodificil/impossivel). O cliente nunca manda texto que começa com "/".
    /// </summary>
    async Task ChatAsync(string _, string text)
    {
        var cmd = text.Trim().ToLowerInvariant();
        if (cmd.StartsWith("!bot ") && BotGolfer.ParseLevel(cmd[5..]) is { } level)
        {
            bool add;
            lock (Rooms.Sync)
            {
                if (room == null || room.State != RoomState.Waiting) return;
                room.BotLevel = level;
                add = room.Bot == null;
                if (room.Bot is { } present)                                    // já está na sala: troca o kit e reenvia a vaga
                {
                    ctx.Players.EquipBot(present.Player, level);
                    InGameOutput.Broadcast(room, RoomPackets.SlotRemove(room, present.Guid));
                    InGameOutput.Broadcast(room, RoomPackets.SlotAdd(room, present));
                }
                InGameOutput.Broadcast(room, new PacketWriter(0x3F).Str($"Bot: {level}"));
            }
            if (add)
            {
                var b = await ctx.Players.CreateBotAsync();
                lock (Rooms.Sync) AddBot(b);
            }
            return;
        }
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
        if (r?.Settings.Mode == GameMode.GuildMatch) return;                 // bot não tem guilda
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
    /// <summary>Primeiro 0x63 de uma sala normal: reenvia os slots uma vez (chamado sob o lock).</summary>
    void ResendSlotsOnceLocked()
    {
        if (room == null || room.State != RoomState.Waiting || slotsResent) return;
        slotsResent = true;
        conn.Send(RoomPackets.SlotsFull(room));
    }
}
