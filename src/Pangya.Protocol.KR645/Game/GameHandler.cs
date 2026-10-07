using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Auth;
using Pangya.Domain.Game;
using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Serviços do game server (um por processo).</summary>
public sealed class GameContext(GameWorld world, SessionService sessions, PlayerService players)
{
    public GameWorld World { get; } = world;
    public SessionService Sessions { get; } = sessions;
    public PlayerService Players { get; } = players;
}

/// <summary>
/// Game server do cliente 645. Este arquivo: entrada até o lobby (docs/protocolo/SPEC-game.md, SPEC-player-shop.md):
/// hello -> 0x02 login -> 0x42 dados do jogador + 0x4B canais + inventário -> 0x04 entrar no canal -> 0x4C.
/// Salas: GameHandler.Room.cs. Partida: GameHandler.Play.cs.
/// </summary>
public sealed partial class GameHandler(Connection conn, GameContext ctx) : IConnectionHandler, IGameSession
{
    // ids C->S
    const ushort CLogin = 0x02, CEnterChannel = 0x04, CEnterChannelAlt = 0x83, CAfterChannel = 0x99, CHeartbeat = 0xF6, CUnknown55 = 0x55;
    // ids S->C
    const ushort SHello = 0x3D, SPlayerInfo = 0x42, SChannels = 0x4B, SEnterChannel = 0x4C, SCharacters = 0x6E, SCaddies = 0x6F,
        SEquip = 0x70, SItems = 0x71, SGiftBox = 0x78, SCookie = 0x94, SMascots = 0xDF, SItemCounts = 0xA5,
        SCardsClear = 0x12D, SCardPeriodsClear = 0x12E, SCardPeriods = 0x12F, SCards = 0x130;

    Player? player;
    Channel? channel;

    public Player Player => player!;
    public Connection Connection => conn;

    public ValueTask OnConnectedAsync()
    {
        conn.ParseKey = Random.Shared.Next(16);
        conn.SendRaw(new PacketWriter(SHello).U8(0).U8(0).U8((byte)conn.ParseKey));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDisconnectedAsync()
    {
        if (player == null) return ValueTask.CompletedTask;
        lock (ctx.World.Rooms.Sync)
        {
            ctx.World.Rooms.Lobby.Remove(this);
            LeaveRoom(notifySelf: false);
        }
        ctx.World.Leave(this);
        GameWorld.LeaveChannel(channel);
        channel = null;
        return ValueTask.CompletedTask;
    }

    public void Kick(string reason) => conn.Close(reason);

    public async ValueTask OnPacketAsync(PacketReader p)
    {
        if (player == null && p.Id != CLogin)
        {
            Log.Debug($"{conn} pacote 0x{p.Id:X4} antes do login: ignorado");
            return;
        }
        if (await HandleRoomAsync(p) || HandlePlay(p)) return;
        switch (p.Id)
        {
            case CLogin: await LoginAsync(p); break;
            case CEnterChannel or CEnterChannelAlt: EnterChannel(p.U8()); break;
            case CAfterChannel or CHeartbeat or CUnknown55: break;
            default: Log.Debug($"{conn} pacote não tratado 0x{p.Id:X4} ({p.Remaining} bytes)"); break;
        }
    }

    async Task LoginAsync(PacketReader p)
    {
        if (player != null) throw new PacketException("login repetido");
        var id = p.Str(22);
        var uid = p.U32();
        p.U32();                                        // MemberNo
        p.U16();
        var key = p.Str(64);
        var version = p.Str(16);
        if (ctx.World.IsFull) { conn.Close("servidor cheio"); return; }
        var accountId = await ctx.Sessions.ValidateGameLoginAsync(key);
        var loaded = accountId == uid ? await ctx.Players.LoadAsync(uid) : null;
        if (loaded == null || !string.Equals(loaded.Login, id, StringComparison.OrdinalIgnoreCase))
        {
            conn.Close($"login inválido no game server (id={id} uid={uid})");
            return;
        }
        player = loaded;
        ctx.World.Enter(this);
        Log.Info($"{conn} entrou: {player.Login} ({player.Nickname}) uid={uid} versão={version}");
        SendPlayerInfo();
        SendChannels();
        SendInventory();
    }

    void SendPlayerInfo()
    {
        var w = new PacketWriter(SPlayerInfo, 0xD00).U8(0).Str(Kr645.ClientVersion).Str("")
            .Struct(PlayerStructs.UserInfo(player!))
            .Struct(PlayerStructs.SystemTime(DateTime.Now))       // hora do servidor (loja, validade de itens)
            .U8(0).U8(0).U16(0).U16(0).U16(0)                     // flag, ?, bongdari x3
            .U32(0).U32(0).U32(0).U32(0)                          // flagBlock, controlServerService, ?, serverProperty
            .Zeros(0x119);                                        // GUILD_USER_INFO: sem guilda (guildId != 0 dispara RSS)
        conn.Send(w);
    }

    void SendChannels()
    {
        var w = new PacketWriter(SChannels).U8((byte)ctx.World.Channels.Count);
        foreach (var c in ctx.World.Channels)
        {
            var s = new sChannelInfo { Max_Num = (ushort)c.MaxUsers, Current_Num = (ushort)c.Count, Uid = (byte)c.Id };
            Cp949.Write(s.Name, c.Name);
            w.Struct(s);
        }
        conn.Send(w);
    }

    static bool InItemList(ItemGroup g) => g is ItemGroup.Part or ItemGroup.Club or ItemGroup.ClubSet or ItemGroup.Ball
        or ItemGroup.Usable or ItemGroup.Skin or ItemGroup.SetItem;

    /// <summary>
    /// Listas que o cliente guarda (o 0x42 limpa o inventário, então vêm depois dele). Em cada lista total == n,
    /// senão o cliente espera mais pacotes.
    /// </summary>
    void SendInventory()
    {
        var p = player!;
        var chars = p.OfGroup(ItemGroup.Character);
        var w = new PacketWriter(SCharacters, 8 + chars.Count * 0x1BC).U16((ushort)chars.Count).U16((ushort)chars.Count);
        foreach (var c in chars) w.Struct(PlayerStructs.Character(c));
        conn.Send(w);

        var caddies = p.OfGroup(ItemGroup.Caddie);
        w = new PacketWriter(SCaddies).U16((ushort)caddies.Count).U16((ushort)caddies.Count);
        foreach (var c in caddies) w.Struct(PlayerStructs.Caddie(c));
        conn.Send(w);

        var items = new List<Item>();
        foreach (var it in p.Items.Values)
            if (InItemList(it.Group)) items.Add(it);
        w = new PacketWriter(SItems, 8 + items.Count * 0xA8).U16((ushort)items.Count).U16((ushort)items.Count);
        foreach (var it in items) w.Struct(PlayerStructs.ItemInfo(it));
        conn.Send(w);

        conn.Send(new PacketWriter(SEquip).Struct(PlayerStructs.Equip(p)));

        var mascots = p.OfGroup(ItemGroup.Mascot);
        int nm = Math.Min(mascots.Count, 255);
        w = new PacketWriter(SMascots).U8((byte)nm);
        for (int i = 0; i < nm; i++) w.Struct(PlayerStructs.Mascot(mascots[i]));
        conn.Send(w);

        conn.Send(new PacketWriter(SGiftBox).U8(1).U16(1).U16(0).U16(0));   // caixa de presentes vazia (modo 1)
        conn.Send(new PacketWriter(SCardsClear));
        conn.Send(new PacketWriter(SCards).U32(0).U16(0));                  // cards: Fase 5
        conn.Send(new PacketWriter(SCardPeriodsClear));
        conn.Send(new PacketWriter(SCardPeriods).U16(0));
        conn.Send(new PacketWriter(SCookie).U64((ulong)p.Cookie));
    }

    void EnterChannel(byte id)
    {
        var r = ctx.World.JoinChannel(id, channel, out var joined);
        if (r == ChannelJoinResult.Ok) channel = joined;
        Log.Info($"{conn} canal {id}: {r}");
        conn.Send(new PacketWriter(SEnterChannel).U8((byte)r));
    }
}
