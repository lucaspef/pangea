using Pangya.Core.Config;
using Pangya.Core.Limits;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Accounts;
using Pangya.Domain.Servers;

namespace Pangya.Protocol.KR645.Login;

/// <summary>Serviços que o login server usa (um por processo).</summary>
public sealed class LoginContext(LoginService login, IServerRegistry registry, LoginConfig config, int maxAttemptsPerMinute)
{
    public LoginService Login { get; } = login;
    public IServerRegistry Registry { get; } = registry;
    public LoginConfig Config { get; } = config;
    public AttemptLimiter Attempts { get; } = new(maxAttemptsPerMinute, TimeSpan.FromMinutes(1));
}

/// <summary>
/// Login server do cliente 645 (docs/protocolo/SPEC-login.md e SPEC-login-novaconta.md). Nunca fecha a conexão por
/// iniciativa própria em erro de login: o cliente mostraria "servidor de login desconectado".
/// </summary>
public sealed class LoginHandler(Connection conn, LoginContext ctx) : IConnectionHandler
{
    // ids C->S
    const ushort CLogin = 0x01, CKickPrevious = 0x04, CCreateNick = 0x06, CCheckNick = 0x07, CCreateCharacter = 0x08;
    // ids S->C
    const ushort SHello = 0x00, SLoginResult = 0x01, SServerList = 0x02, SMessengerList = 0x09,
        SCreateNickResult = 0x0D, SCheckNickResult = 0x0E, SGameKey = 0x10, SCreateCharacterResult = 0x11;
    // sub-códigos de SLoginResult (lobbytask.c / logindlg.cpp)
    const byte Ok = 0x00, Blocked = 0x05, WrongPassword = 0x06, CreateCharacter = 0xD9, CreateNick = 0xD8, SecondPasswordOk = 0xDE;

    Account? pending;          // conta autenticada, ainda criando nickname/personagem
    bool loggedIn;
    string typedId = "", checkedNick = "";

    public ValueTask OnConnectedAsync()
    {
        conn.ParseKey = Random.Shared.Next(16);
        conn.SendRaw(new PacketWriter(SHello).U32((uint)conn.ParseKey).U32(ctx.Config.ServerUid));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDisconnectedAsync() => ValueTask.CompletedTask;

    public async ValueTask OnPacketAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CLogin: await LoginAsync(p); break;
            case CCheckNick: await CheckNickAsync(p.Str(32)); break;
            case CCreateNick: await CreateNickAsync(p.Str(32)); break;
            case CCreateCharacter: await CreateCharacterAsync(p.I32(), p.U8()); break;
            case CKickPrevious: break;                    // sessão anterior: tratado quando houver controle de online
            default: Log.Debug($"{conn} pacote de login não tratado 0x{p.Id:X4}"); break;
        }
    }

    async Task LoginAsync(PacketReader p)
    {
        typedId = p.Str(22);
        var authKey = p.Str(64);
        p.U32();                                          // provType (2)
        p.U8();                                           // web login
        var memberNo = p.U32();
        if (loggedIn || pending != null) return;          // já autenticado nesta conexão
        if (!ctx.Attempts.TryAttempt(conn.Remote.Address.ToString()))
        {
            conn.Send(new PacketWriter(SLoginResult).U8(WrongPassword));
            return;
        }
        var r = await ctx.Login.LoginAsync(typedId, authKey, memberNo);
        Log.Info($"{conn} login id={typedId}: {r.Outcome}");
        await SendResultAsync(r);
    }

    async Task CheckNickAsync(string nick)
    {
        if (pending == null) return;
        var st = await ctx.Login.CheckNicknameAsync(pending, nick);
        if (st == Domain.Players.NicknameStatus.Ok) checkedNick = nick;
        var w = new PacketWriter(SCheckNickResult).U32((uint)st);
        conn.Send(st == Domain.Players.NicknameStatus.Ok ? w.Str(nick) : w);
    }

    async Task CreateNickAsync(string nick)
    {
        if (pending == null) return;
        if (!string.Equals(nick, checkedNick, StringComparison.OrdinalIgnoreCase))
        {
            conn.Send(new PacketWriter(SCreateNickResult).U32(10));   // difere do nickname conferido
            return;
        }
        var st = await ctx.Login.CreateNicknameAsync(pending, nick);
        Log.Info($"{conn} criar nickname {nick}: {st}");
        if (st != Domain.Players.NicknameStatus.Ok)
        {
            conn.Send(new PacketWriter(SCreateNickResult).U32((uint)st));
            return;
        }
        conn.Send(new PacketWriter(SCreateNickResult).U32(0).Str(nick));
        await SendResultAsync(await ctx.Login.ContinueAsync(pending));   // próximo passo: personagem
    }

    async Task CreateCharacterAsync(int characterTypeId, byte colors)
    {
        if (pending == null) return;
        bool ok = await ctx.Login.CreateCharacterAsync(pending, characterTypeId, colors & 0x0F, colors >> 4);
        Log.Info($"{conn} criar personagem 0x{characterTypeId:X8} cores {colors:X2}: {(ok ? "ok" : "recusado")}");
        conn.Send(new PacketWriter(SCreateCharacterResult).U8(ok ? (byte)0 : (byte)1));
        if (ok) await SendResultAsync(await ctx.Login.ContinueAsync(pending));
    }

    async Task SendResultAsync(LoginResult r)
    {
        switch (r.Outcome)
        {
            case LoginOutcome.Ok:
                var acc = r.Account!;
                pending = null;
                loggedIn = true;
                conn.Send(new PacketWriter(SGameKey).Str(r.GameKey!));
                conn.Send(new PacketWriter(SLoginResult).U8(Ok).Str(typedId).U32((uint)acc.Id).U32((uint)acc.IdentityFlags)
                    .U8(0).U8(1).U32(0).U32(0).Str(acc.Nickname!));
                conn.Send(new PacketWriter(SLoginResult).U8(SecondPasswordOk));   // sem isto o cliente descarta a lista
                conn.Send(ServerList(SMessengerList, await ctx.Registry.ListAsync("messenger")));
                conn.Send(ServerList(SServerList, await ctx.Registry.ListAsync("game")));
                break;
            case LoginOutcome.Blocked:
                conn.Send(new PacketWriter(SLoginResult).U8(Blocked).Str(r.Account?.BlockReason ?? ""));
                break;
            case LoginOutcome.NeedsNickname:
                pending = r.Account;
                conn.Send(new PacketWriter(SLoginResult).U8(CreateNick).U32(0xFFFFFFFF));
                break;
            case LoginOutcome.NeedsCharacter:
                pending = r.Account;
                conn.Send(new PacketWriter(SLoginResult).U8(CreateCharacter));
                break;
            default:
                conn.Send(new PacketWriter(SLoginResult).U8(WrongPassword));
                break;
        }
    }

    /// <summary>u8 quantidade + n × sGameServerInfo (92 bytes).</summary>
    static PacketWriter ServerList(ushort id, IReadOnlyList<ServerInfo> servers)
    {
        var w = new PacketWriter(id).U8((byte)Math.Min(servers.Count, 255));
        foreach (var s in servers.Take(255))
        {
            var e = new sGameServerInfo { id = (uint)s.Id, maxUser = s.MaxUsers, curUser = s.CurUsers, port = s.Port, eventFlags = (uint)s.Flags };
            Cp949.Write(e.name, s.Name);
            Cp949.Write(e.addr, s.Address);
            w.Struct(e);
        }
        return w;
    }
}
