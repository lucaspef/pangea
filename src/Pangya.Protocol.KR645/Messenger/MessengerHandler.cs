using System.Collections.Concurrent;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Game;
using Pangya.Domain.Messenger;

namespace Pangya.Protocol.KR645.Messenger;

/// <summary>
/// Serviços do mensageiro (um por processo): amizades no banco e quem está conectado, em memória. Roda junto do game
/// server: findGame confirma que o uid do login está jogando (o 0x12 não tem chave nenhuma) e diz onde ele está.
/// </summary>
public sealed class MessengerContext(FriendService friends, Func<long, IGameSession?> findGame, int gameServerId)
{
    public const ushort SSub = 0x2E, SubList = 0x102;
    const int PageSize = 30, FriendSize = 0x8D;
    /// <summary>Status do 0x115/0x1D: 0 jogando, 1 ausente, 3 ocupado, 4 online.</summary>
    public const byte StatusPlaying = 0, StatusAway = 1, StatusBusy = 3, StatusOnline = 4;

    readonly ConcurrentDictionary<long, MessengerHandler> online = new();

    public FriendService Friends { get; } = friends;
    public int GameServerId { get; } = gameServerId;
    public int OnlineCount => online.Count;

    public IGameSession? FindGame(long uid) => findGame(uid);

    /// <summary>Sessão do mensageiro que já recebeu a lista (online para os amigos).</summary>
    public MessengerHandler? Find(long uid) => online.TryGetValue(uid, out var h) && h.Listed ? h : null;

    /// <summary>Registra a sessão; devolve a anterior da mesma conta (para derrubar).</summary>
    internal MessengerHandler? Enter(MessengerHandler h)
    {
        MessengerHandler? old = null;
        online.AddOrUpdate(h.AccountId, h, (_, prev) => { old = prev; return h; });
        return old == h ? null : old;
    }

    internal bool Leave(MessengerHandler h) => online.TryRemove(new KeyValuePair<long, MessengerHandler>(h.AccountId, h));

    public static PacketWriter Sub(ushort sub, int capacity = 16) => new PacketWriter(SSub, capacity + 2).U16(sub);

    static sUserPosition OfflinePosition => new() { iRoomIdx = 0xFFFF, iRoomType = -1, iServerGUID = -1, iChannelUid = 0xFF };

    /// <summary>sUserPosition montado pelo servidor (canal e sala da sessão de jogo), nunca o do cliente.</summary>
    public sUserPosition PositionOf(long uid)
    {
        var game = FindGame(uid);
        if (game == null) return OfflinePosition;
        var where = game.Where;
        var pos = new sUserPosition
        {
            iRoomIdx = where.Room >= 0 ? (ushort)where.Room : (ushort)0xFFFF, iRoomType = where.RoomType,
            iServerGUID = GameServerId, iChannelUid = where.Channel >= 0 ? (byte)where.Channel : (byte)0xFF,
        };
        Cp949.Write(pos.csChannelName, where.ChannelName);
        return pos;
    }

    /// <summary>
    /// Como o dono da lista vê o amigo f (sFriend, 0x8D): pedido/aceite/bloqueio e, se ele está online, aceito e não me
    /// bloqueou, posição e status. forceOnline: o 0x106 manda quem pediu como online.
    /// </summary>
    public sFriend Entry(Friend f, bool forceOnline = false)
    {
        bool accepted = f.State == FriendState.Accepted;
        var other = Find(f.AccountId);
        var e = new sFriend
        {
            Uid = (uint)f.AccountId, Guid = (uint)f.AccountId, userPosition = OfflinePosition, State = 5, Channel = 0xFF,
            GameLevel = (byte)Math.Clamp(f.Level, 0, 255),
        };
        Cp949.Write(e.NickName, f.Nickname);
        Cp949.Write(e.szAlias, f.Alias.Length > 0 ? f.Alias : accepted ? "Friend" : "");
        e.PangyaFriend = 1;
        e.IsAccept = accepted ? 1u : 0;
        e.IsAgree = f.State != FriendState.Pending ? 1u : 0;
        e.IsBlock = f.Blocked ? 1u : 0;
        e.IsBlocked = f.BlockedMe ? 1u : 0;
        if (other != null && accepted && !f.BlockedMe)
        {
            e.IsLogOn = 1;
            e.State = other.Status;
            e.userPosition = PositionOf(f.AccountId);
            e.IsPlay = other.Status == StatusPlaying ? 1u : 0;
            e.IsDive = other.Status == StatusAway ? 1u : 0;
            e.IsBusy = other.Status == StatusBusy ? 1u : 0;
        }
        else if (forceOnline) e.IsLogOn = 1;
        return e;
    }

    /// <summary>0x2E/0x102 em páginas de 30: u8 página (1 limpa a lista), u16 total, u16 n, n × sFriend.</summary>
    public async Task<List<PacketWriter>> ListPagesAsync(long owner)
    {
        var list = await Friends.Store.ListAsync(owner);
        int pages = Math.Max(1, (list.Count + PageSize - 1) / PageSize);
        var result = new List<PacketWriter>(pages);
        for (int page = 0; page < pages; page++)
        {
            int from = page * PageSize, n = Math.Min(PageSize, list.Count - from);
            var w = Sub(SubList, 8 + n * FriendSize).U8((byte)(page + 1)).U16((ushort)list.Count).U16((ushort)n);
            for (int i = 0; i < n; i++) w.Struct(Entry(list[from + i]));
            result.Add(w);
        }
        return result;
    }
}

/// <summary>
/// Mensageiro do cliente 645 (docs/protocolo/SPEC-messenger.md): hello 0x2C, login 0x12 -> 0x2D, lista 0x14 -> 0x2E/0x102,
/// amizade (0x17..0x1C, 0x1F), presença (0x1D, 0x23 -> 0x10E/0x10F/0x115/0x123) e conversa (0x1E -> 0x113/0x114).
/// Respostas no 0x2E com sub-id. Tudo conferido no servidor: o cliente só manda uid e nick.
/// </summary>
public sealed class MessengerHandler(Connection conn, MessengerContext ctx) : IConnectionHandler
{
    // C->S
    const ushort CLogin = 0x12, CListFor = 0x13, CList = 0x14, CLogout = 0x16, CLookup = 0x17, CRequest = 0x18, CAccept = 0x19,
        CBlock = 0x1A, CUnblock = 0x1B, CRemove = 0x1C, CStatus = 0x1D, CChat = 0x1E, CAlias = 0x1F, CPosition = 0x23;
    // S->C
    const ushort SHello = 0x2C, SLogin = 0x2D;
    // sub-ids do 0x2E
    const ushort SubRequested = 0x104, SubAskedMe = 0x106, SubAccepted = 0x109, SubTheyAccepted = 0x10A,
        SubRemoved = 0x10B, SubBlocked = 0x10C, SubUnblocked = 0x10D, SubLogOn = 0x10E, SubLogOff = 0x10F, SubChat = 0x113,
        SubChatFailed = 0x114, SubStatus = 0x115, SubLookup = 0x117, SubDuplicate = 0x118, SubAlias = 0x119, SubPosition = 0x123;
    const int MaxChat = 200, LoginTimeoutSeconds = 30;
    /// <summary>Resposta automática do cliente com as janelas de conversa cheias ("o outro não pode responder").</summary>
    public const string AutoReply = "상대방이 응답할 수 없습니다.";
    static readonly TimeSpan AutoReplyWindow = TimeSpan.FromSeconds(10);

    public long AccountId { get; private set; }
    public string Nickname { get; private set; } = "";
    /// <summary>Recebeu a lista (estado 4 no cliente): aparece online para os amigos.</summary>
    public bool Listed { get; private set; }
    public byte Status { get; private set; } = MessengerContext.StatusOnline;
    public Connection Connection => conn;
    bool loggedIn, replaced;
    (long To, DateTime At) lastAutoReply;

    static PacketWriter Sub(ushort sub, int capacity = 16) => MessengerContext.Sub(sub, capacity);

    public ValueTask OnConnectedAsync()
    {
        conn.ParseKey = Random.Shared.Next(16);
        conn.IdleTimeoutSeconds = LoginTimeoutSeconds;             // depois do login: 0 (o cliente não manda heartbeat ao MSN)
        conn.SendRaw(new PacketWriter(SHello).U8(0).U8(0).U32((uint)conn.ParseKey));
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnDisconnectedAsync()
    {
        if (!loggedIn || replaced || !ctx.Leave(this)) return;
        Log.Info($"{conn} MSN: {Nickname} saiu");
        if (Listed) await NotifyFriendsAsync(() => Sub(SubLogOff).U32((uint)AccountId));
    }

    public async ValueTask OnPacketAsync(PacketReader p)
    {
        if (!loggedIn && p.Id != CLogin)
        {
            Log.Debug($"{conn} MSN: pacote 0x{p.Id:X4} antes do login: ignorado");
            p.Skip(p.Remaining);
            return;
        }
        switch (p.Id)
        {
            case CLogin: Login(p.U32(), p.Str(22)); break;
            case CListFor: p.Skip(p.Remaining); await SendListAsync(); break;
            case CList: await ListAsync(); break;
            case CLogout: conn.Close("saiu do mensageiro"); break;
            case CLookup: await LookupAsync(p.Str(22)); break;
            case CRequest: await RequestAsync(p.U32(), p.Str(22)); break;
            case CAccept: await AcceptAsync(p.U32()); break;
            case CBlock: await BlockAsync(p.U32(), true); break;
            case CUnblock: await BlockAsync(p.U32(), false); break;
            case CRemove: { uint uid = p.U32(); p.Str(22); await RemoveAsync(uid); break; }
            case CStatus: await StatusAsync(p.U8()); break;
            case CChat: await ChatAsync(p.U32(), p.Str(256)); break;
            case CAlias: await AliasAsync(p.U32(), p.Str(32)); break;
            case CPosition: p.Skip(p.Remaining); await PositionAsync(); break;   // a posição vem da sessão de jogo
            default:
                Log.Info($"{conn} MSN: pacote 0x{p.Id:X4} sem tratamento ({p.Remaining} bytes)");
                p.Skip(p.Remaining);
                break;
        }
    }

    // ------------------------------------------------------------------ login e lista

    /// <summary>0x12 u32 uid, str nick: só entra quem está jogando neste processo, com o mesmo nick e IP.</summary>
    void Login(uint uid, string nick)
    {
        if (loggedIn) throw new PacketException("login repetido no mensageiro");
        var game = ctx.FindGame(uid);
        if (game == null || !string.Equals(game.Player.Nickname, nick, StringComparison.OrdinalIgnoreCase)
            || (game.RemoteAddress is { } ip && !ip.Equals(conn.Remote.Address)))
        {
            Log.Info($"{conn} MSN: login recusado uid={uid} nick={nick} (não está jogando, nick ou IP diferente)");
            conn.Send(new PacketWriter(SLogin).U8(2));
            conn.Close("login do mensageiro inválido", afterSend: true);
            return;
        }
        AccountId = uid;
        Nickname = game.Player.Nickname;
        loggedIn = true;
        conn.IdleTimeoutSeconds = 0;
        if (ctx.Enter(this) is { } old)
        {
            old.replaced = true;
            old.Connection.Send(Sub(SubDuplicate));
            old.Connection.Close("mensageiro aberto de novo", afterSend: true);
        }
        Log.Info($"{conn} MSN: {Nickname} ({uid}) entrou");
        conn.Send(new PacketWriter(SLogin).U8(0).U32(uid));
    }

    /// <summary>0x14: lista (estado 4 no cliente) e aviso aos amigos online; recebe a posição/status de cada um.</summary>
    async Task ListAsync()
    {
        await SendListAsync();
        if (Listed) return;
        Listed = true;
        await NotifyFriendsAsync(() => Sub(SubLogOn).U32((uint)AccountId));
        await NotifyFriendsAsync(PositionPacket);
        foreach (var f in await ctx.Friends.Store.ListAsync(AccountId))
            if (f.State == FriendState.Accepted && !f.BlockedMe && ctx.Find(f.AccountId) is { } h && h != this)
            {
                conn.Send(h.PositionPacket());
                if (h.Status != MessengerContext.StatusOnline) conn.Send(Sub(SubStatus).U32(h.Status).U32((uint)h.AccountId));
            }
    }

    async Task SendListAsync()
    {
        foreach (var page in await ctx.ListPagesAsync(AccountId)) conn.Send(page);
    }

    /// <summary>0x2E/0x123: u32 0, u32 uid, sUserPosition.</summary>
    PacketWriter PositionPacket() => Sub(SubPosition, 8 + 0x4B).U32(0).U32((uint)AccountId).Struct(ctx.PositionOf(AccountId));

    /// <summary>Manda a cada amigo aceito e online (que eu não bloqueei) um pacote novo de <paramref name="build"/>.</summary>
    async Task NotifyFriendsAsync(Func<PacketWriter> build)
    {
        foreach (var f in await ctx.Friends.Store.ListAsync(AccountId))
            if (f.State == FriendState.Accepted && !f.Blocked && ctx.Find(f.AccountId) is { } h && h != this)
                h.Connection.Send(build());
    }

    // ------------------------------------------------------------------ amizade

    /// <summary>0x17 str nick -> 0x117 u32 código [, str nick, u32 uid].</summary>
    async Task LookupAsync(string nick)
    {
        var (code, id, exact) = await ctx.Friends.LookupAsync(AccountId, nick);
        conn.Send(code == FriendCode.Ok ? Sub(SubLookup, 40).U32(0).Str(exact).U32((uint)id) : Sub(SubLookup).U32((uint)code));
    }

    /// <summary>0x18 u32 uid, str nick -> 0x104 ao pedinte (com o sFriend) e 0x106 ao alvo online.</summary>
    async Task RequestAsync(uint target, string nick)
    {
        var code = await ctx.Friends.RequestAsync(AccountId, target, nick);
        Log.Info($"{conn} MSN: {Nickname} pede amizade a {nick} ({target}): {code}");
        if (code != FriendCode.Ok) { conn.Send(Sub(SubRequested).U32((uint)code)); return; }
        if (await ctx.Friends.Store.GetAsync(AccountId, target) is { } mine)
            conn.Send(Sub(SubRequested, 4 + 0x8D).U32(0).Struct(ctx.Entry(mine)));
        if (ctx.Find(target) is { } h && await ctx.Friends.Store.GetAsync(target, AccountId) is { } theirs)
            h.Connection.Send(Sub(SubAskedMe, 0x8D).Struct(ctx.Entry(theirs, forceOnline: true)));
    }

    /// <summary>0x19 u32 uid -> 0x109 a quem aceitou; 0x10A ao outro; os dois trocam status e posição.</summary>
    async Task AcceptAsync(uint other)
    {
        var code = await ctx.Friends.AcceptAsync(AccountId, other);
        Log.Info($"{conn} MSN: {Nickname} aceita {other}: {code}");
        conn.Send(code == FriendCode.Ok ? Sub(SubAccepted).U32(0).U32(other) : Sub(SubAccepted).U32((uint)code));
        if (code != FriendCode.Ok || ctx.Find(other) is not { } h) return;
        h.Connection.Send(Sub(SubTheyAccepted).U32(0).U32((uint)AccountId));
        h.Connection.Send(PositionPacket());
        conn.Send(h.PositionPacket());
        if (h.Status != MessengerContext.StatusOnline) conn.Send(Sub(SubStatus).U32(h.Status).U32(other));
        if (Status != MessengerContext.StatusOnline) h.Connection.Send(Sub(SubStatus).U32(Status).U32((uint)AccountId));
    }

    /// <summary>0x1C u32 uid, str nick -> 0x10B aos dois (também recusa um pedido).</summary>
    async Task RemoveAsync(uint other)
    {
        var code = await ctx.Friends.RemoveAsync(AccountId, other);
        Log.Info($"{conn} MSN: {Nickname} apaga {other}: {code}");
        conn.Send(code == FriendCode.Ok ? Sub(SubRemoved).U32(0).U32(other) : Sub(SubRemoved).U32((uint)code));
        if (code == FriendCode.Ok && ctx.Find(other) is { } h) h.Connection.Send(Sub(SubRemoved).U32(0).U32((uint)AccountId));
    }

    /// <summary>0x1A/0x1B u32 uid -> 0x10C/0x10D. Bloquear: eu sumo para ele (0x10F); desbloquear: volto (0x10E).</summary>
    async Task BlockAsync(uint other, bool block)
    {
        var code = await ctx.Friends.BlockAsync(AccountId, other, block);
        ushort sub = block ? SubBlocked : SubUnblocked;
        conn.Send(code == FriendCode.Ok ? Sub(sub).U32(0).U32(other) : Sub(sub).U32((uint)code));
        if (code != FriendCode.Ok || ctx.Find(other) is not { } h) return;
        if (await ctx.Friends.Store.GetAsync(AccountId, other) is not { State: FriendState.Accepted }) return;
        if (block) h.Connection.Send(Sub(SubLogOff).U32((uint)AccountId));
        else
        {
            h.Connection.Send(Sub(SubLogOn).U32((uint)AccountId));
            h.Connection.Send(PositionPacket());
        }
    }

    /// <summary>0x1F u32 uid, str apelido -> 0x119 u32 código [, u32 uid, str apelido].</summary>
    async Task AliasAsync(uint other, string alias)
    {
        var (code, saved) = await ctx.Friends.AliasAsync(AccountId, other, alias);
        conn.Send(code == FriendCode.Ok ? Sub(SubAlias, 32).U32(0).U32(other).Str(saved) : Sub(SubAlias).U32((uint)code));
    }

    // ------------------------------------------------------------------ presença e conversa

    /// <summary>0x1D u8 status (4 online, 3 ocupado, 1 ausente) -> 0x115 aos amigos.</summary>
    async Task StatusAsync(byte status)
    {
        if (status is not (MessengerContext.StatusOnline or MessengerContext.StatusBusy or MessengerContext.StatusAway)) return;
        Status = status;
        await NotifyFriendsAsync(() => Sub(SubStatus).U32(Status).U32((uint)AccountId));
    }

    /// <summary>Status "jogando", avisado pelo game server (o cliente nunca manda 0x1D 0).</summary>
    public Task SetPlayingAsync(bool playing)
    {
        if (playing == (Status == MessengerContext.StatusPlaying)) return Task.CompletedTask;
        Status = playing ? MessengerContext.StatusPlaying : MessengerContext.StatusOnline;
        return NotifyFriendsAsync(() => Sub(SubStatus).U32(Status).U32((uint)AccountId));
    }

    /// <summary>0x23: mudou de canal/sala -> 0x123 aos amigos (posição da sessão de jogo).</summary>
    Task PositionAsync() => Listed ? NotifyFriendsAsync(PositionPacket) : Task.CompletedTask;

    /// <summary>
    /// 0x1E u32 uid, str msg -> 0x113 (u32 uid, str nick, str msg, u8 0) ao amigo; 0x114 u8 3, u32 uid se não der.
    /// Só entre amigos aceitos, sem bloqueio dos dois lados. A resposta automática do cliente não volta para quem
    /// também respondeu automaticamente (evita pingue-pongue entre dois clientes com as janelas cheias).
    /// </summary>
    async Task ChatAsync(uint to, string msg)
    {
        if (msg.Length > MaxChat) msg = msg[..MaxChat];
        var f = await ctx.Friends.Store.GetAsync(AccountId, to);
        var h = ctx.Find(to);
        if (f is not { State: FriendState.Accepted, Blocked: false, BlockedMe: false } || h == null || msg.Length == 0)
        {
            conn.Send(Sub(SubChatFailed).U8(3).U32(to));
            return;
        }
        if (msg == AutoReply)
        {
            var (lastTo, at) = h.lastAutoReply;
            if (lastTo == AccountId && DateTime.UtcNow - at < AutoReplyWindow) return;
            lastAutoReply = (to, DateTime.UtcNow);
        }
        h.Connection.Send(Sub(SubChat, 16 + msg.Length * 2).U32((uint)AccountId).Str(Nickname).Str(msg).U8(0));
    }
}
