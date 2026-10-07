using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Pangya.Core.Net;
using Pangya.Data;
using Pangya.Domain.Accounts;
using Pangya.Domain.Players;
using Pangya.Domain.Servers;
using Pangya.Login;
using Pangya.Web;

namespace Pangya.Tests;

public class PasswordTests
{
    [Fact]
    public void HashVerifiesOnlyTheRightPassword()
    {
        var h = PasswordHasher.Hash("segredo123", 1000);
        Assert.True(PasswordHasher.Verify("segredo123", h));
        Assert.False(PasswordHasher.Verify("segredo124", h));
        Assert.True(PasswordHasher.NeedsRehash(h));                       // 1000 < Iterations
        Assert.False(PasswordHasher.NeedsRehash(PasswordHasher.Hash("x")));
        Assert.NotEqual(PasswordHasher.Hash("x"), PasswordHasher.Hash("x"));  // sal aleatório
        Assert.False(PasswordHasher.Verify("x", "lixo"));
    }
}

/// <summary>Ambiente da Fase 2: serviços no banco de teste, Web e Login em portas livres.</summary>
public sealed class Phase2Env : IAsyncDisposable
{
    public ServerServices S { get; }
    public WebApplication Web { get; }
    public TcpServer Login { get; }
    public int WebPort { get; }
    readonly CancellationTokenSource cts = new();

    Phase2Env(ServerServices s, WebApplication web, TcpServer login, int webPort) => (S, Web, Login, WebPort) = (s, web, login, webPort);

    public static async Task<Phase2Env> StartAsync(bool autoRegister = false)
    {
        var cfg = TestEnv.Config;
        cfg.Web.AutoRegister = autoRegister;
        var s = new ServerServices(cfg);
        var web = WebServer.Build(s, 0);
        await web.StartAsync();
        var addr = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var login = LoginServer.Create(s, [0])[0];
        var env = new Phase2Env(s, web, login, new Uri(addr).Port);
        _ = login.StartAsync(env.cts.Token);
        return env;
    }

    /// <summary>POST como o cliente real faz (multipart, mesmo boundary e cabeçalhos), por socket cru.</summary>
    public async Task<string> HttpLoginAsync(string id, string pwd)
    {
        const string b = "--MULTI-PARTS-FORM-DATA-BOUNDARY";
        var body = $"--{b}\r\nContent-Disposition: form-data; name=\"id\"\r\n\r\n{id}\r\n--{b}\r\nContent-Disposition: form-data; name=\"pwd\"\r\n\r\n{pwd}\r\n" +
                   $"--{b}\r\nContent-Disposition: form-data; name=\"gamecode\"\r\n\r\n9\r\n--{b}--\r\n";
        var req = $"POST {WebServer.LoginPath} HTTP/1.1\r\nAccept: */*\r\nContent-Type: multipart/form-data; boundary={b}\r\n" +
                  $"Content-Length: {Encoding.ASCII.GetByteCount(body)}\r\nUser-Agent: MERONG(0.9/;p)\r\nHost: 127.0.0.1\r\nConnection: close\r\nCache-Control: no-cache\r\n\r\n{body}";
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, WebPort);
        var st = tcp.GetStream();
        await st.WriteAsync(Encoding.ASCII.GetBytes(req));
        var resp = await new StreamReader(st).ReadToEndAsync();
        Assert.StartsWith("HTTP/1.1 200", resp);
        return resp[(resp.IndexOf("\r\n\r\n") + 4)..];
    }

    public static (string Key, uint Member) ParseArg(string xml)
    {
        Assert.Contains("<result>true</result>", xml);
        var arg = xml.Split("<arg>")[1].Split("</arg>")[0];
        var f = arg.Split('|').Select(kv => kv.Split('=')).ToDictionary(kv => kv[0], kv => kv[1]);
        Assert.Equal("0", f["PCBangNo"]);
        return (f["AuthKey"], uint.Parse(f["MemberNo"]));
    }

    public async Task<TestClient> ConnectLoginAsync()
    {
        var c = await TestClient.ConnectAsync(Login.Port);
        var (id, r) = await c.ReceiveAsync();
        Assert.Equal(0, id);
        c.Key = (int)r.U32();
        Assert.Equal(10101u, r.U32());
        return c;
    }

    public static Task SendLoginAsync(TestClient c, string id, string key, uint member) =>
        c.SendAsync(new PacketWriter(0x01).Str(id).Str(key).U32(2).U8(0).U32(member).U64(0x7fffffffffffffff).U8(0));

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        await Web.StopAsync();
        await Web.DisposeAsync();
        await S.DisposeAsync();
    }
}

[Collection("db")]
public class AccountTests(DbFixture fx)
{
    static string NewLogin() => "u" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task RegisterAndAuthenticate()
    {
        var svc = new AccountService(new AccountRepository(fx.Db), 100);
        var login = NewLogin();
        Assert.Equal(RegisterStatus.Ok, (await svc.RegisterAsync(login, "senha123", "1.1.1.1")).Status);
        Assert.Equal(RegisterStatus.LoginTaken, (await svc.RegisterAsync(login.ToUpperInvariant(), "senha123", "1.1.1.1")).Status);
        Assert.Equal(RegisterStatus.InvalidLogin, (await svc.RegisterAsync("a b", "senha123", "1.1.1.1")).Status);
        Assert.Equal(RegisterStatus.InvalidPassword, (await svc.RegisterAsync(NewLogin(), "123", "1.1.1.1")).Status);

        var (ok, acc) = await svc.AuthenticateAsync(login.ToUpperInvariant(), "senha123", "1.1.1.1");
        Assert.Equal(AuthStatus.Ok, ok);
        Assert.True(acc!.Id > 100000);
        Assert.Equal(AuthStatus.WrongPassword, (await svc.AuthenticateAsync(login, "errada!!", "1.1.1.1")).Status);
        Assert.Equal(AuthStatus.UnknownLogin, (await svc.AuthenticateAsync(NewLogin(), "senha123", "1.1.1.1")).Status);
    }

    [Fact]
    public async Task AutoRegisterCreatesAccountOnlyWhenEnabled()
    {
        var login = NewLogin();
        var off = new AccountService(new AccountRepository(fx.Db), 100, autoRegister: false);
        Assert.Equal(AuthStatus.UnknownLogin, (await off.AuthenticateAsync(login, "senha123", "ip")).Status);
        var on = new AccountService(new AccountRepository(fx.Db), 100, autoRegister: true);
        Assert.Equal(AuthStatus.Ok, (await on.AuthenticateAsync(login, "senha123", "ip")).Status);
        Assert.Equal(AuthStatus.WrongPassword, (await on.AuthenticateAsync(login, "outra123", "ip")).Status);
    }

    [Fact]
    public async Task TooManyAttemptsAreRefused()
    {
        var svc = new AccountService(new AccountRepository(fx.Db), 3);
        for (int i = 0; i < 3; i++) await svc.AuthenticateAsync(NewLogin(), "senha123", "9.9.9.9");
        Assert.Equal(AuthStatus.TooManyAttempts, (await svc.AuthenticateAsync(NewLogin(), "senha123", "9.9.9.9")).Status);
    }

    [Fact]
    public async Task BlockedAccountIsReported()
    {
        var svc = new AccountService(new AccountRepository(fx.Db), 100);
        var login = NewLogin();
        var (_, acc) = await svc.RegisterAsync(login, "senha123", "ip");
        await using (var c = await fx.Db.OpenAsync())
            await Dapper.SqlMapper.ExecuteAsync(c, "update accounts set blocked_until = now() + interval '1 day', block_reason = 'teste' where id = @id", new { id = acc!.Id });
        var (st, a) = await svc.AuthenticateAsync(login, "senha123", "ip");
        Assert.Equal(AuthStatus.Blocked, st);
        Assert.Equal("teste", a!.BlockReason);
    }

    [Fact]
    public async Task SessionKeysAreSingleUseAndReplaced()
    {
        var acc = await new AccountRepository(fx.Db).CreateAsync(NewLogin(), "h");
        var sessions = new Domain.Auth.SessionService(new SessionRepository(fx.Db));
        var web = await sessions.IssueWebAuthAsync(acc!.Id);
        Assert.Equal(acc.Id, await sessions.ConsumeWebAuthAsync(web));
        Assert.Null(await sessions.ConsumeWebAuthAsync(web));              // uso único
        var g1 = await sessions.IssueGameLoginAsync(acc.Id);
        Assert.Equal(acc.Id, await sessions.ValidateGameLoginAsync(g1));
        Assert.Equal(acc.Id, await sessions.ValidateGameLoginAsync(g1));   // vale para trocar de servidor
        var g2 = await sessions.IssueGameLoginAsync(acc.Id);
        Assert.Null(await sessions.ValidateGameLoginAsync(g1));            // substituída
        Assert.Equal(acc.Id, await sessions.ValidateGameLoginAsync(g2));
        Assert.Null(await sessions.ValidateGameLoginAsync("'; drop table x"));
    }

    [Fact]
    public async Task ServerRegistryListsOnlyLiveServers()
    {
        var reg = new ServerRegistry(fx.Db);
        await reg.HeartbeatAsync(new ServerInfo(901, "game", "Vivo", "127.0.0.1", 1, 100, 5, 0), TimeSpan.FromMinutes(1));
        await reg.HeartbeatAsync(new ServerInfo(902, "game", "Morto", "127.0.0.1", 2, 100, 0, 0), TimeSpan.FromSeconds(-1));
        var list = await reg.ListAsync("game");
        Assert.Contains(list, s => s.Id == 901 && s.CurUsers == 5);
        Assert.DoesNotContain(list, s => s.Id == 902);
        await reg.RemoveAsync(901);
        await reg.RemoveAsync(902);
    }
}

[Collection("db")]
public class LoginFlowTests(DbFixture fx)
{
    static string NewLogin() => "u" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task WebLoginAnswersLikeTheClientExpects()
    {
        _ = fx;
        await using var env = await Phase2Env.StartAsync();
        var login = NewLogin();
        await env.S.AccountService.RegisterAsync(login, "senha123", "t");
        var (key, member) = Phase2Env.ParseArg(await env.HttpLoginAsync(login, "senha123"));
        Assert.Equal(16, key.Length);
        Assert.True(member > 100000);
        var fail = await env.HttpLoginAsync(login, "errada!!");
        Assert.Contains("<result>false</result>", fail);
        Assert.All(fail, ch => Assert.True(ch < 128));       // o cliente só mostra ASCII em qualquer Windows
        Assert.Contains("<result>false</result>", await env.HttpLoginAsync("<script>", "x"));
    }

    [Fact]
    public async Task FullLoginReachesServerList()
    {
        await using var env = await Phase2Env.StartAsync();
        var login = NewLogin();
        var (_, acc) = await env.S.AccountService.RegisterAsync(login, "senha123", "t");
        Assert.True(await env.S.Accounts.SetNicknameAsync(acc!.Id, "Nick" + login[1..6]));
        await env.S.Players.CreateAsync(acc.Id, new NewPlayer(0, 0, []));
        await env.S.Registry.HeartbeatAsync(new ServerInfo(20201, "game", "Servidor Teste", "127.0.0.1", 20201, 3000, 7, 0), TimeSpan.FromMinutes(1));
        var (key, member) = Phase2Env.ParseArg(await env.HttpLoginAsync(login, "senha123"));

        await using var c = await env.ConnectLoginAsync();
        await Phase2Env.SendLoginAsync(c, login, key, member);
        var gameKey = (await c.ExpectAsync(0x10)).Str();
        Assert.Equal(acc.Id, await env.S.Sessions.ValidateGameLoginAsync(gameKey));
        var r = await c.ExpectAsync(0x01);
        Assert.Equal(0, r.U8());
        Assert.Equal(login, r.Str());
        Assert.Equal((uint)acc.Id, r.U32());
        r.U32(); r.U8(); r.U8(); r.U32(); r.U32();
        Assert.StartsWith("Nick", r.Str());
        Assert.Equal(0xDE, (await c.ExpectAsync(0x01)).U8());              // portão da 2ª senha antes da lista
        Assert.Equal(0, (await c.ExpectAsync(0x09)).U8());
        var list = await c.ExpectAsync(0x02);
        int n = list.U8();
        var entries = Enumerable.Range(0, n).Select(_ => list.Struct<Protocol.KR645.sGameServerInfo>()).ToList();
        var e = entries.Single(x => x.id == 20201);
        Assert.Equal("Servidor Teste", Core.Text.Cp949.Read(e.name));
        Assert.Equal("127.0.0.1", Core.Text.Cp949.Read(e.addr));
        Assert.Equal(20201, e.port);
        Assert.True(e.curUser < e.maxUser - 200);
        await env.S.Registry.RemoveAsync(20201);
    }

    [Fact]
    public async Task ReusedOrForgedKeyIsRejectedWithoutDisconnect()
    {
        await using var env = await Phase2Env.StartAsync();
        var login = NewLogin();
        var (_, acc) = await env.S.AccountService.RegisterAsync(login, "senha123", "t");
        await env.S.Accounts.SetNicknameAsync(acc!.Id, "N" + login[1..8]);
        await env.S.Players.CreateAsync(acc.Id, new NewPlayer(0, 0, []));
        var (key, member) = Phase2Env.ParseArg(await env.HttpLoginAsync(login, "senha123"));

        await using var c = await env.ConnectLoginAsync();
        await Phase2Env.SendLoginAsync(c, login, "0123456789ABCDEF", member);    // chave forjada
        var r = await c.ExpectAsync(0x01);
        Assert.Equal(6, r.U8());
        await Phase2Env.SendLoginAsync(c, "outro", key, member);                 // chave de outra conta
        Assert.Equal(6, (await c.ExpectAsync(0x01)).U8());
        // a chave foi gasta na tentativa acima (uso único): nem a conta certa consegue reutilizar
        await Phase2Env.SendLoginAsync(c, login, key, member);
        Assert.Equal(6, (await c.ExpectAsync(0x01)).U8());
    }


    [Fact]
    public async Task NewAccountCreatesNicknameAndCharacterThenGetsServerList()
    {
        await using var env = await Phase2Env.StartAsync();
        var login = NewLogin();
        await env.S.AccountService.RegisterAsync(login, "senha123", "t");
        var (key, member) = Phase2Env.ParseArg(await env.HttpLoginAsync(login, "senha123"));
        await using var c = await env.ConnectLoginAsync();
        await Phase2Env.SendLoginAsync(c, login, key, member);

        var r = await c.ExpectAsync(0x01);
        Assert.Equal(0xD8, r.U8());                                    // abre o diálogo de nickname
        Assert.Equal(0xFFFFFFFF, r.U32());

        var nick = "Nk" + login[1..9];
        await c.SendAsync(new PacketWriter(0x07).Str("ab"));             // curto demais
        Assert.Equal(3u, (await c.ExpectAsync(0x0E)).U32());
        await c.SendAsync(new PacketWriter(0x07).Str(nick));
        r = await c.ExpectAsync(0x0E);
        Assert.Equal(0u, r.U32());
        Assert.Equal(nick, r.Str());
        await c.SendAsync(new PacketWriter(0x06).Str(nick + "x"));      // diferente do conferido
        Assert.Equal(10u, (await c.ExpectAsync(0x0D)).U32());
        await c.SendAsync(new PacketWriter(0x06).Str(nick));
        r = await c.ExpectAsync(0x0D);
        Assert.Equal(0u, r.U32());
        Assert.Equal(nick, r.Str());
        Assert.Equal(0xD9, (await c.ExpectAsync(0x01)).U8());           // abre a criação de personagem

        await c.SendAsync(new PacketWriter(0x08).U32(0x04000005).U8(0)); // personagem não oferecido
        Assert.Equal(1, (await c.ExpectAsync(0x11)).U8());
        await c.SendAsync(new PacketWriter(0x08).U32(0x04000000).U8(0x21)); // cabelo 1, camisa 2
        Assert.Equal(0, (await c.ExpectAsync(0x11)).U8());
        var gameKey = (await c.ExpectAsync(0x10)).Str();
        r = await c.ExpectAsync(0x01);
        Assert.Equal(0, r.U8());
        r.Str(); r.U32(); r.U32(); r.U8(); r.U8(); r.U32(); r.U32();
        Assert.Equal(nick, r.Str());
        Assert.Equal(0xDE, (await c.ExpectAsync(0x01)).U8());
        await c.ExpectAsync(0x02);
        Assert.NotNull(await env.S.Sessions.ValidateGameLoginAsync(gameKey));

        var p = (await env.S.Players.LoadAsync(member))!;
        Assert.Equal(100_000, p.Pang);
        var ch = p.Character!;
        Assert.Equal(0x04000000, ch.TypeId);
        Assert.Equal(1, ch.Int("hair"));
        Assert.Equal(2, ch.Int("shirt"));
        Assert.Equal([0x08000400, 0, 0x08004400, 0x08006400, 0x08008400, 0x0800A400, 0, 0x0800E400, 0x08010400,
                      0, 0, 0, 0, 0, 0, 0, 0, 0, 0x08024400, 0, 0, 0, 0, 0], ch.IntArray("parts", 24));
        Assert.Equal(ch.Id, p.Equip.CharacterId);
        Assert.Equal(0x10000000, p.Find(p.Equip.ClubSetId)!.TypeId);
        Assert.Equal(0x14000000, p.Equip.BallTypeId);
        Assert.Equal(100, p.OfGroup(ItemGroup.Ball).Single().Quantity);
        Assert.All(p.Items.Values, i => Assert.True(i.Id >= 1_000_000));
        Assert.Equal(p.Items.Values.Count, p.Items.Values.Select(i => i.Id).Distinct().Count());

        // segunda conta não pode usar o mesmo nickname
        var login2 = NewLogin();
        await env.S.AccountService.RegisterAsync(login2, "senha123", "t");
        var (key2, member2) = Phase2Env.ParseArg(await env.HttpLoginAsync(login2, "senha123"));
        await using var c2 = await env.ConnectLoginAsync();
        await Phase2Env.SendLoginAsync(c2, login2, key2, member2);
        await c2.ExpectAsync(0x01);
        await c2.SendAsync(new PacketWriter(0x07).Str(nick.ToUpperInvariant()));
        Assert.Equal(2u, (await c2.ExpectAsync(0x0E)).U32());
    }
}

public class NicknameTests
{
    [Theory]
    [InlineData("Jogador1", true)]
    [InlineData("한글닉네임", true)]
    [InlineData("abc", false)]                   // menos de 4 bytes
    [InlineData("12345678901234567", false)]     // mais de 16 bytes
    [InlineData("tem espaco", false)]
    [InlineData("aspa'x", false)]
    [InlineData("Ação", false)]                  // fora de ASCII/Hangul
    [InlineData("meulogin", false)]              // igual ao login
    public void ClientRulesAreEnforced(string nick, bool ok) => Assert.Equal(ok, NicknameRules.IsValid(nick, "MeuLogin"));
}

public class BallTests
{
    [Fact]
    public void BasicBallIsNeverConsumed()
    {
        Assert.False(new Item { TypeId = Item.BasicBall }.IsConsumable);
        Assert.True(new Item { TypeId = 0x14000001 }.IsConsumable);       // outras bolas gastam
        Assert.False(new Item { TypeId = 0x10000000 }.IsConsumable);      // club set não
    }
}
