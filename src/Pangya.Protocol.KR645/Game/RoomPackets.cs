using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Players;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Pacotes de lista de salas e sala do cliente 645 (docs/protocolo/SPEC-room.md).</summary>
public static class RoomPackets
{
    // ids S->C
    public const ushort SRoomListOn = 0xF3, SRoomListOff = 0xF4, SRoomList = 0x45, SLobbyUsers = 0x44, SEnterRoom = 0x47,
        SRoomSettings = 0x48, SSlots = 0x46, SReady = 0x76, SNewMaster = 0x7A, STeam = 0x7B, SStartFailed = 0x7D,
        SLeftRoom = 0x4A, SChat = 0x3E, SGamePlayers = 0x74, SGameInit = 0x50;

    /// <summary>Modos "em massa" (IsMassGame @004238c0): slots sem sCharacterInfo, 0x74 sem sUserInfo.</summary>
    public static bool IsMassGame(GameMode m) => m is GameMode.Tournament or GameMode.Team30s or GameMode.GuildMatch
        or GameMode.Approach or GameMode.NewApproach or (GameMode)14;

    public static sRoomInfo RoomInfo(Room r)
    {
        var s = r.Settings;
        var i = new sRoomInfo
        {
            bPublic = (byte)(s.Password.Length == 0 ? 1 : 0),
            bAvailable = (byte)(r.State == RoomState.Waiting ? 1 : 0),     // aceita entrar (ordena e libera o botão)
            nUserLimit = s.MaxPlayers, nUserNum = (byte)r.Players.Count, nHole = s.Holes, gameType = (byte)s.Mode,
            roomGuid = (ushort)r.Index, holeType = s.HoleType, mapType = s.Course, shotTimeLimit = s.ShotTimeMs,
            gameTimeLimit = s.GameTimeMs, bSleep = (byte)(s.Sleep ? 1 : 0), masterUID = (int)r.OwnerId, realGameType = (byte)s.Mode,
            tidMatch = (uint)MatchTid(r),
        };
        Cp949.Write(i.title, s.Title);
        Cp949.Write(i.password, s.Password);
        r.Key.CopyTo(i.RoomKey.m_byKey);
        return i;
    }

    /// <summary>
    /// Troféu da sala (sRoomInfo.tidMatch): no torneio individual, o da partida em andamento ou, esperando, pela média de
    /// nível de quem está na sala; 0 nos outros modos.
    /// </summary>
    public static int MatchTid(Room r)
    {
        if (r.Settings.Mode != GameMode.Tournament) return 0;
        if (r.Game is TourneyGame t) return t.MatchTid;
        var levels = new List<int>(r.Players.Count);
        foreach (var p in r.Players) levels.Add(p.Player.Level);
        return Trophy.RoomTid(levels);
    }

    /// <summary>u16 do 0x46/0x7A: no lounge a avatar task só aceita 0xFFFF; nas outras salas, o índice da sala.</summary>
    public static ushort SlotKey(Room r) => r.Settings.Mode == GameMode.AvatarChat ? (ushort)0xFFFF : (ushort)r.Index;

    public static sSlotInfo SlotInfo(RoomPlayer p)
    {
        var s = new sSlotInfo
        {
            dwGuid = p.Guid, connectionRank = (byte)p.Slot, tidChar = (uint)(p.Player.Character?.TypeId ?? 0),
            level = (byte)Math.Max(1, p.Player.Level), dwUserUID = (uint)p.Player.AccountId, subRoomIndex = 0xFFFF,
            dwIdentity = (uint)p.Player.IdentityFlags,
        };
        Cp949.Write(s.sNick, p.Player.Nickname);
        s.bTeam = p.Team;
        if (p.Player.Guild is { } g)
        {
            Cp949.Write(s.sGuild, g.Name);
            Cp949.Write(s.szEmblemName, g.Mark);
            s.GuildId = (uint)g.Id;
        }
        s.location[0] = p.X;                                                   // lounge: posição, ângulo, pose e estado
        s.location[1] = p.Z;
        s.location[2] = p.Angle;
        s.action = p.Action;
        s.state = p.State;
        Cp949.Write(s.strTradeTitle, p.TradeTitle);
        s.bMaster = p.Master ? 1u : 0u;
        s.bReady = p.Ready ? 1u : 0u;
        return s;
    }

    public static sBriefUserInfo BriefUser(Player p, ushort roomIndex = 0xFFFF)
    {
        var b = new sBriefUserInfo { dwUid = (uint)p.AccountId, dwGuid = (uint)p.AccountId, roomIndex = roomIndex, level = (byte)p.Level,
            m_GuildId = (uint)(p.Guild?.Id ?? 0) };
        if (p.Guild is { } g) Cp949.Write(b.szEmblemName, g.Mark);
        Cp949.Write(b.sNick, p.Nickname);
        return b;
    }

    /// <summary>0x45: u8 n, u8 sub (0 tudo, 1 adiciona/atualiza, 2 remove, 3 atualiza), u16 0xFFFF (senão o cliente ignora), n × sRoomInfo.</summary>
    public static PacketWriter RoomList(byte sub, Room room)
    {
        var w = new PacketWriter(SRoomList).U8(1).U8(sub).U16(0xFFFF);
        return w.Struct(RoomInfo(room));
    }

    public static PacketWriter RoomListAll(RoomManager rooms)
    {
        int n = Math.Min(rooms.Rooms.Count, 255), i = 0;
        var w = new PacketWriter(SRoomList, 8 + n * 0xB2).U8((byte)n).U8(0).U16(0xFFFF);
        foreach (var r in rooms.Rooms)
        {
            if (i++ == n) break;
            w.Struct(RoomInfo(r));
        }
        return w;
    }

    /// <summary>0x47: resultado 0 + galeria 0 + sRoomInfo; ou só o código de erro.</summary>
    public static PacketWriter EnterRoom(Room r) => new PacketWriter(SEnterRoom).U8(0).U8(0).Struct(RoomInfo(r));
    public static PacketWriter EnterRoomFailed(JoinResult code) => new PacketWriter(SEnterRoom).U8((byte)code);

    /// <summary>0x48 configurações da sala.</summary>
    public static PacketWriter Settings(Room r)
    {
        var s = r.Settings;
        return new PacketWriter(SRoomSettings).U16((ushort)r.Index).U8((byte)s.Mode).U8(s.Course).U8(s.Holes).U8(s.HoleType)
            .U8(s.MaxPlayers).U8(0).U8((byte)(s.Sleep ? 1 : 0)).U32(s.ShotTimeMs).U32(s.GameTimeMs).U32(0)
            .Str(s.Password).Str(s.Title);
    }

    /// <summary>0x46 sub 0: lista completa de slots (+ sCharacterInfo fora dos modos em massa) + convidados (0).</summary>
    public static PacketWriter SlotsFull(Room r)
    {
        bool mass = IsMassGame(r.Settings.Mode);
        var w = new PacketWriter(SSlots, 16 + r.Players.Count * (0x152 + 0x1BC)).U8(0).U16(SlotKey(r)).U8((byte)r.Players.Count);
        foreach (var p in r.Players)
        {
            w.Struct(SlotInfo(p));
            if (!mass) w.Struct(Character(p));
        }
        return w.U8(0);
    }

    public static PacketWriter SlotAdd(Room r, RoomPlayer p)
    {
        var w = new PacketWriter(SSlots, 0x320).U8(1).U16(SlotKey(r)).Struct(SlotInfo(p));
        return IsMassGame(r.Settings.Mode) ? w : w.Struct(Character(p));
    }

    public static PacketWriter SlotRemove(Room r, uint guid) => new PacketWriter(SSlots).U8(2).U16(SlotKey(r)).U32(guid);
    public static PacketWriter SlotUpdate(Room r, RoomPlayer p) => new PacketWriter(SSlots).U8(3).U16(SlotKey(r)).U32(p.Guid).Struct(SlotInfo(p));

    static sCharacterInfo Character(RoomPlayer p) => p.Player.Character is { } c ? PlayerStructs.Character(c) : default;

    public static PacketWriter Chat(string nick, string message, byte kind = 0) => new PacketWriter(SChat).U8(kind).Str(nick).Str(message);

    /// <summary>
    /// 0x74: u8 0, u8 n, n × {sUserInfo, SYSTEMTIME, u8 m, m × sSCardAvilityPeriodInfo} (modos em massa: só u8,u8,u8 +
    /// SYSTEMTIME). Os cards de cada um fazem efeito para os outros clientes (o próprio jogador usa a lista do 0x12F).
    /// </summary>
    public static PacketWriter GamePlayers(Room r, IReadOnlyDictionary<int, Domain.Shop.CardInfo> cards)
    {
        var now = PlayerStructs.SystemTime(DateTime.Now);
        int n = Math.Min(r.Players.Count, 4);
        var w = new PacketWriter(SGamePlayers, 8 + n * (0xB92 + 17 + 16 * 0x41)).U8(0).U8((byte)n);
        if (IsMassGame(r.Settings.Mode)) return w.U8(0).U8(0).U8(0).Struct(now);
        for (int i = 0; i < n; i++)
        {
            var ui = PlayerStructs.UserInfo(r.Players[i].Player);
            ui.roomIndex = (ushort)r.Index;
            ui.info.dwGuid = r.Players[i].Guid;
            var active = PlayerStructs.ActiveCards(r.Players[i].Player, cards);
            int m = Math.Min(active.Count, 255);
            w.Struct(ui).Struct(now).U8((byte)m);
            for (int k = 0; k < m; k++) w.Struct(active[k]);
        }
        return w;
    }

    /// <summary>0x50 início da partida: o cliente troca para o CGolfTask.</summary>
    public static PacketWriter GameInit(Room r)
    {
        var s = r.Settings;
        var w = new PacketWriter(SGameInit, 160).U8(r.CoursePlayed).U8((byte)s.Mode).U8(s.HoleType).U8(s.Holes)
            .U32(r.GameSeed).U32(s.ShotTimeMs).U32(s.GameTimeMs);
        for (int i = 0; i < 18; i++)
        {
            w.U32(r.HoleSeeds[i]);
            if ((int)s.Mode == 14) w.U8(r.CoursePlayed);
            w.U8(r.HoleOrder[i]);
        }
        // gimmicks (CGimmickContainer::LoadFromPacket): u32 semente + 18 × {u8 n, n × GimmickDispositionInformation 0x14:
        // i32 tipo (0 moeda, 1 caixa), u32 índice, u32 0, u32 20, u8 buraco, 3 zeros}. Só o Wiz City tem itens.
        w.U32(r.Field?.Seed ?? 0);
        for (byte hole = 1; hole <= 18; hole++)
        {
            var types = r.Field != null && r.Field.PerHole.TryGetValue(hole, out var t) ? t : [];
            w.U8((byte)types.Length);
            for (int i = 0; i < types.Length; i++) w.I32(types[i]).U32((uint)i).U32(0).U32(20).U8(hole).Zeros(3);
        }
        return w;
    }
}
