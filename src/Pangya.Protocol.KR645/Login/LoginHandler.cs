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
/// Login server do cliente 645 (docs/protocolo/SPEC-login.md). Nunca fecha a conexão por iniciativa própria
/// em caso de erro de login: o cliente mostraria "servidor de login desconectado".
/// </summary>
public sealed class LoginHandler(Connection conn, LoginContext ctx) : IConnectionHandler
{
    // ids C->S
    const ushort CLogin = 0x01, CKickPrevious = 0x04;
    // ids S->C
    const ushort SHello = 0x00, SLoginResult = 0x01, SServerList = 0x02, SMessengerList = 0x09, SGameKey = 0x10;
    // sub-códigos de SLoginResult (lobbytask.c / logindlg.cpp)
    const byte Ok = 0x00, Blocked = 0x05, WrongPassword = 0x06, CreateCharacter = 0xD9, SecondPasswordOk = 0xDE;

    Account? account;
    string typedId = "";

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
        if (account != null) return;                      // já logado nesta conexão
        if (!ctx.Attempts.TryAttempt(conn.Remote.Address.ToString()))
        {
            conn.Send(new PacketWriter(SLoginResult).U8(WrongPassword));
            return;
        }
        var r = await ctx.Login.LoginAsync(typedId, authKey, memberNo);
        Log.Info($"{conn} login id={typedId}: {r.Outcome}");
        await SendResultAsync(r);
    }

    async Task SendResultAsync(LoginResult r)
    {
        switch (r.Outcome)
        {
            case LoginOutcome.Ok:
                account = r.Account!;
                conn.Send(new PacketWriter(SGameKey).Str(r.GameKey!));
                conn.Send(new PacketWriter(SLoginResult).U8(Ok).Str(typedId).U32((uint)account.Id).U32((uint)account.IdentityFlags)
                    .U8(0).U8(1).U32(0).U32(0).Str(account.Nickname!));
                conn.Send(new PacketWriter(SLoginResult).U8(SecondPasswordOk));   // sem isto o cliente descarta a lista
                conn.Send(ServerList(SMessengerList, await ctx.Registry.ListAsync("messenger")));
                conn.Send(ServerList(SServerList, await ctx.Registry.ListAsync("game")));
                break;
            case LoginOutcome.Blocked:
                conn.Send(new PacketWriter(SLoginResult).U8(Blocked).Str(r.Account?.BlockReason ?? ""));
                break;
            case LoginOutcome.NeedsNickname:
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
