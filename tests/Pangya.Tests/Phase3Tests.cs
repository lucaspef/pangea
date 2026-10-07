using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Data;
using Pangya.Domain.Accounts;
using Pangya.Domain.Players;
using Pangya.Game;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Game server de teste numa porta livre + contas prontas para entrar.</summary>
public sealed class GameEnv : IAsyncDisposable
{
    public ServerServices S { get; }
    public GameServer Game { get; }
    public PlayerService Players { get; }
    readonly CancellationTokenSource cts = new();

    GameEnv(ServerServices s)
    {
        S = s;
        Game = new GameServer(s, 0);
        Players = new PlayerService(s.Players, Kr645GameData.Load(s.Config.Data.IffPath), s.Config.NewPlayer);
        _ = Game.Tcp.StartAsync(cts.Token);
    }

    public static async Task<GameEnv> StartAsync()
    {
        var env = new GameEnv(new ServerServices(TestEnv.Config));
        await Task.Yield();
        return env;
    }

    /// <summary>Cria conta + nickname + personagem e devolve (conta, chave do game server).</summary>
    public async Task<(Account Acc, string Key)> NewPlayerAsync()
    {
        var login = "g" + Guid.NewGuid().ToString("N")[..12];
        var acc = (await S.Accounts.CreateAsync(login, "h"))!;
        await S.Accounts.SetNicknameAsync(acc.Id, "N" + login[1..10]);
        await Players.CreateAsync(acc.Id, 0x04000000, 1, 2);
        return (acc with { Nickname = "N" + login[1..10] }, await S.Sessions.IssueGameLoginAsync(acc.Id));
    }

    public async Task<TestClient> ConnectAsync()
    {
        var c = await TestClient.ConnectAsync(Game.Tcp.Port);
        var (id, r) = await c.ReceiveAsync();
        Assert.Equal(0x3D, id);
        r.U8(); r.U8();
        c.Key = r.U8();
        return c;
    }

    /// <summary>C->S 0x02 como o cliente manda (gameunit.cpp).</summary>
    public static Task SendLoginAsync(TestClient c, Account acc, string key) =>
        c.SendAsync(new PacketWriter(0x02).Str(acc.Login).U32((uint)acc.Id).U32((uint)acc.Id).U16(0x6696).Str(key)
            .Str("645.00").U32(Kr645.PacketVersion).U32(0));

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        await S.DisposeAsync();
    }
}

[Collection("db")]
public class GameLobbyTests(DbFixture fx)
{
    [Fact]
    public async Task LoginSendsPlayerDataFromDatabaseAndEntersChannel()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);

        // 0x42: sub 0, versão, "", sUserInfo, SYSTEMTIME, 2+6+16 bytes, GUILD_USER_INFO
        var r = await c.ExpectAsync(0x42);
        Assert.Equal(0, r.U8());
        Assert.Equal("645.00", r.Str());
        Assert.Equal("", r.Str());
        var u = r.Struct<sUserInfo>();
        var time = r.Struct<SYSTEMTIME>();
        r.Skip(2 + 6 + 16 + 0x119);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(DateTime.Now.Year, time.wYear);
        Assert.Equal(acc.Login, Cp949.Read(u.info.sID));
        Assert.Equal(acc.Nickname, Cp949.Read(u.info.sNick));
        Assert.Equal((uint)acc.Id, u.info.dwUID);
        Assert.Equal((uint)acc.Id, u.info.dwGuid);
        Assert.Equal(0u, u.info.dwGuildId);
        Assert.Equal(100_000, u.stat.i64Pang);
        Assert.Equal(0xFFFF, u.roomIndex);
        var ch = p.Character!;
        Assert.Equal((uint)ch.Id, u.userEquip.guidChar);
        Assert.Equal((uint)ch.Id, u.charInfo.guid);
        Assert.Equal(0x04000000u, u.charInfo.tid);
        Assert.Equal(1, u.charInfo.hairClr);
        Assert.Equal(2u, u.charInfo.shirtsClr);
        Assert.Equal(0x08000400u, u.charInfo.tidParts[0]);
        Assert.Equal(0x10000000u, u.clubInfo.tid);
        Assert.Equal(u.userEquip.guidClubSet, u.clubInfo.guid);
        Assert.Equal(0x14000000u, u.userEquip.tidBall);
        Assert.Equal(0xFF, u.mapStat[19].bMap);

        var ch4b = await c.ExpectAsync(0x4B);
        Assert.Equal(1, ch4b.U8());
        var chan = ch4b.Struct<sChannelInfo>();
        Assert.Equal("Canal 1", Cp949.Read(chan.Name));

        var chars = await c.ExpectAsync(0x6E);
        Assert.Equal(1, chars.U16());
        Assert.Equal(1, chars.U16());
        Assert.Equal((uint)ch.Id, chars.Struct<sCharacterInfo>().guid);
        var items = await c.ExpectAsync(0x71);
        int n = items.U16();
        Assert.Equal(n, items.U16());
        var list = Enumerable.Range(0, n).Select(_ => items.Struct<sItemInfo>()).ToList();
        Assert.Contains(list, i => i.tid == 0x10000000 && i.IsValid == 1);
        Assert.Contains(list, i => i.tid == 0x14000000 && i.Common[0] == 100);
        var equip = (await c.ExpectAsync(0x70)).Struct<sUserEquip>();
        Assert.Equal(u.userEquip.guidChar, equip.guidChar);
        Assert.Equal(0, (await c.ExpectAsync(0xDF)).U8());
        Assert.Equal(0ul, (await c.ExpectAsync(0x94)).U64());

        await c.SendAsync(new PacketWriter(0x04).U8(5));                  // canal inexistente
        Assert.Equal(3, (await c.ExpectAsync(0x4C)).U8());
        await c.SendAsync(new PacketWriter(0x04).U8(0));
        Assert.Equal(1, (await c.ExpectAsync(0x4C)).U8());
        Assert.Equal(1, env.Game.World.Channels[0].Count);
    }

    [Fact]
    public async Task ChannelLimitIsEnforced()
    {
        await using var env = await GameEnv.StartAsync();                 // config de teste: canal com 2 vagas
        var clients = new List<TestClient>();
        var results = new List<byte>();
        for (int i = 0; i < 3; i++)
        {
            var (acc, key) = await env.NewPlayerAsync();
            var c = await env.ConnectAsync();
            clients.Add(c);
            await GameEnv.SendLoginAsync(c, acc, key);
            await c.ExpectAsync(0x94);
            await c.SendAsync(new PacketWriter(0x04).U8(0));
            results.Add((await c.ExpectAsync(0x4C)).U8());
        }
        Assert.Equal([1, 1, 2], results);
        await clients[0].DisposeAsync();                                  // saiu: libera a vaga
        for (int i = 0; i < 50 && env.Game.World.Channels[0].Count > 1; i++) await Task.Delay(20);
        Assert.Equal(1, env.Game.World.Channels[0].Count);
        foreach (var c in clients.Skip(1)) await c.DisposeAsync();
    }

    [Fact]
    public async Task InvalidKeyIsDisconnected()
    {
        await using var env = await GameEnv.StartAsync();
        var (acc, _) = await env.NewPlayerAsync();
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, "0123456789ABCDEF");
        Assert.True(await c.IsClosedByServerAsync());
    }

    [Fact]
    public async Task KeyOfAnotherAccountIsDisconnected()
    {
        await using var env = await GameEnv.StartAsync();
        var (a, _) = await env.NewPlayerAsync();
        var (_, keyB) = await env.NewPlayerAsync();
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, a, keyB);
        Assert.True(await c.IsClosedByServerAsync());
    }

    [Fact]
    public async Task SecondLoginKicksTheFirst()
    {
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        await using var first = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(first, acc, key);
        await first.ExpectAsync(0x94);
        await using var second = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(second, acc, key);
        await second.ExpectAsync(0x42);
        Assert.True(await first.IsClosedByServerAsync());
        Assert.Equal(1, env.Game.World.OnlineCount);
    }
}
