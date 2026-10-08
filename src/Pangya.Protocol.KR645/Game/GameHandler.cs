using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Auth;
using Pangya.Domain.Game;
using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Serviços do game server (um por processo).</summary>
public sealed class GameContext(GameWorld world, SessionService sessions, PlayerService players, IGameData data,
    Core.Config.LotteryConfig? lottery = null, Domain.Servers.IServerRegistry? registry = null, Domain.Guilds.IGuildStore? guilds = null)
{
    /// <summary>Guildas (null nos testes antigos: sem guilda).</summary>
    public Domain.Guilds.GuildService? Guilds { get; } = guilds == null ? null : new(guilds);
    readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (GameHandler From, GameHandler To, int Room)> invites = new();
    int nextInvite;

    /// <summary>Convite pendente (0xB2 -> 0x29): id único; os antigos somem ao serem usados ou com o servidor.</summary>
    public uint NewInvite(GameHandler from, GameHandler to, int room)
    {
        uint id = (uint)Interlocked.Increment(ref nextInvite);
        invites[id] = (from, to, room);
        return id;
    }

    public (GameHandler From, GameHandler To, int Room)? TakeInvite(uint id) => invites.TryRemove(id, out var v) ? v : null;

    /// <summary>Registro de servidores (lista de servidores no jogo, 0x43); null nos testes que não usam.</summary>
    public Domain.Servers.IServerRegistry? Registry { get; } = registry;
    public GameWorld World { get; } = world;
    public SessionService Sessions { get; } = sessions;
    public PlayerService Players { get; } = players;
    public IGameData Data { get; } = data;
    public Domain.Shop.ShopService Shop { get; } = new(players.Store, data);
    public Domain.Shop.CardService Cards { get; } = new(players.Store, data);
    public PlayerActions Actions { get; } = new(players.Store, data);
    Domain.Shop.LotteryService? lotteryService;
    public Domain.Shop.LotteryService Lottery => lotteryService ??= new(Shop, Data, lottery ?? new());
    Domain.Shop.MagicBoxService? magicBoxService;
    public Domain.Shop.MagicBoxService MagicBox => magicBoxService ??= new(Shop, Data);
    Domain.Shop.SpinCubeService? spinCubeService;
    public Domain.Shop.SpinCubeService SpinCube => spinCubeService ??= new(Shop, Data);
}

/// <summary>
/// Game server do cliente 645. Este arquivo: entrada até o lobby (docs/protocolo/SPEC-game.md, SPEC-player-shop.md):
/// hello -> 0x02 login -> 0x42 dados do jogador + 0x4B canais + inventário -> 0x04 entrar no canal -> 0x4C.
/// Salas: GameHandler.Room.cs. Partida: GameHandler.Play.cs.
/// </summary>
public sealed partial class GameHandler(Connection conn, GameContext ctx) : IConnectionHandler, IGameSession
{
    // ids C->S
    const ushort CLogin = 0x02, CEnterChannel = 0x04, CEnterChannelAlt = 0x83, CAfterChannel = 0x99, CHeartbeat = 0xF6, CUnknown55 = 0x55,
        CGhost = 0xBA;
    // ids S->C
    const ushort SStatsUpdate = 0x43;
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
            LobbyUser(LobbyUserRemove);                                         // sai da lista dos outros
        }
        ctx.World.Leave(this);
        FinishGameEnd();                                                  // grava a recompensa de quem fechou o jogo cedo
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
        if (await HandleRoomAsync(p) || HandlePlay(p) || await HandleShopAsync(p) || await HandleMyRoomAsync(p) || await HandleLotteryAsync(p) || HandleGm(p) || await HandleSocialAsync(p) || await HandleTradeAsync(p) || await HandleBoxesAsync(p) || await HandleGuildAsync(p)) return;
        switch (p.Id)
        {
            case CLogin: await LoginAsync(p); break;
            case CEnterChannel or CEnterChannelAlt: EnterChannel(p.U8()); break;
            case CAfterChannel or CHeartbeat or CUnknown55: break;
            case CGhost:                                                     // modo Ghost: código morto no 645 (SPEC-ghost.md)
                Log.Info($"{conn} pacote de Ghost 0xBA sub 0x{(p.Remaining > 0 ? p.U8() : 0):X2} ignorado (exe modificado?)");
                p.Skip(p.Remaining);
                break;
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
        conn.IdleTimeoutSeconds = conn.Limits.SessionIdleTimeoutSeconds;
        ctx.World.Enter(this);
        Log.Info($"{conn} entrou: {player.Login} ({player.Nickname}) uid={uid} versão={version}");
        await LoadGuildAsync();
        SendPlayerInfo();
        SendChannels();
        SendInventory();
    }

    void SendPlayerInfo()
    {
        var w = new PacketWriter(SPlayerInfo, 0xD00).U8(0).Str(Kr645.ClientVersion).Str("")
            .Struct(PlayerStructs.UserInfo(player!))
            .Struct(PlayerStructs.SystemTime(DateTime.Now))       // hora do servidor (loja, validade de itens)
            .U8(0).U8(0).U16(0xFFFF).U16(0xFFFF).U16(0)           // flag, ?, papel: jogadas (-1 = sem limite), bônus (-1), faltam
            .U32(0).U32(0).U32(0).U32(0)                          // flagBlock, controlServerService, ?, serverProperty
            .Struct(myGuildInfo);                                 // GUILD_USER_INFO (o RSS do guildId != 0 é inofensivo)
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

    /// <summary>Itens por pacote 0x71 no login: o cliente vai somando os pacotes (o GB manda no máximo 50).</summary>
    const int ItemsPerPacket = 50;

    static bool InItemList(ItemGroup g) => g is ItemGroup.Part or ItemGroup.Club or ItemGroup.ClubSet or ItemGroup.Ball
        or ItemGroup.Usable or ItemGroup.Skin or ItemGroup.SetItem;

    /// <summary>
    /// Listas que o cliente guarda (o 0x42 limpa o inventário, então vêm depois dele). Em cada pacote total == n
    /// (senão o cliente espera mais); a lista de itens vai em vários pacotes, que o cliente acumula.
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
            if (InItemList(it.Group) && it.Location == ItemLocation.Inventory) items.Add(it);
        for (int start = 0; start == 0 || start < items.Count; start += ItemsPerPacket)   // páginas com total = n, como o GB
        {
            int n = Math.Min(ItemsPerPacket, items.Count - start);
            w = new PacketWriter(SItems, 8 + n * 0xA8).U16((ushort)n).U16((ushort)n);
            for (int i = start; i < start + n; i++) w.Struct(PlayerStructs.ItemInfo(items[i]));
            conn.Send(w);
        }

        conn.Send(new PacketWriter(SEquip).Struct(PlayerStructs.Equip(p)));

        var mascots = p.OfGroup(ItemGroup.Mascot);
        int nm = Math.Min(mascots.Count, 255);
        w = new PacketWriter(SMascots).U8((byte)nm);
        for (int i = 0; i < nm; i++) w.Struct(PlayerStructs.Mascot(mascots[i]));
        conn.Send(w);

        conn.Send(new PacketWriter(SGiftBox).U8(1).U16(1).U16(0).U16(0));   // caixa de presentes vazia (modo 1)
        SendCards();
        conn.Send(TutorialPacket(p.Tutorial));                             // missões do tutorial feitas
        conn.Send(new PacketWriter(SCookie).U64((ulong)p.Cookie));
    }

    /// <summary>0x12D limpa + 0x130 pilhas de cards (sCards) + 0x12E limpa + 0x12F cards ativos/encaixados.</summary>
    void SendCards()
    {
        var p = player!;
        var stacks = p.OfGroup(ItemGroup.Card);
        conn.Send(new PacketWriter(SCardsClear));
        var w = new PacketWriter(SCards, 8 + stacks.Count * 0x3A).U32(0).U16((ushort)stacks.Count);
        foreach (var c in stacks) w.Struct(new sCards { uid = (uint)c.Id, typeId = (uint)c.TypeId, count = c.Quantity, type = 1 });
        conn.Send(w);
        var active = PlayerStructs.ActiveCards(p, ctx.Data.Cards);
        conn.Send(new PacketWriter(SCardPeriodsClear));
        w = new PacketWriter(SCardPeriods, 8 + active.Count * 0x41).U16((ushort)active.Count);
        foreach (var a in active) w.Struct(a);
        conn.Send(w);
    }

    /// <summary>Fim de partida: credita pang (limitado) e EXP e atualiza o pang mostrado. Chamado sob o lock da sala.</summary>
    void EnterChannel(byte id)
    {
        var r = ctx.World.JoinChannel(id, channel, out var joined);
        if (r == ChannelJoinResult.Ok) channel = joined;
        Log.Info($"{conn} canal {id}: {r}");
        conn.Send(new PacketWriter(SEnterChannel).U8((byte)r));
    }
}
