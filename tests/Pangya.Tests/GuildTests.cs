using Pangya.Core.Net;
using Pangya.Domain.Guilds;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

public class GuildRuleTests
{
    [Theory]
    [InlineData("Guild", true)] [InlineData("abc", false)] [InlineData("com espaco", false)]
    [InlineData("123456789012345678901", false)] [InlineData("길드이름", true)]
    public void NameRules(string name, bool ok) => Assert.Equal(ok, GuildService.ValidName(name));
}

/// <summary>Guilda de ponta a ponta (docs/protocolo/SPEC-guilda.md).</summary>
[Collection("db")]
public class GuildTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id, string Key)> EnterAsync(GameEnv env, bool kit)
    {
        var (acc, key) = await env.NewPlayerAsync();
        if (kit)
        {
            var p = (await env.Players.LoadAsync(acc.Id))!;
            Assert.Equal(ShopCode.Ok, (await new ShopService(env.Players.Store, env.Data).GiveAsync(p, GuildService.CreateKit, 1)).Code);
        }
        return (await LoginAsync(env, acc, key), acc.Id, key);
    }

    static async Task<TestClient> LoginAsync(GameEnv env, Domain.Accounts.Account acc, string key)
    {
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return c;
    }

    /// <summary>0x1BD: u32 1, GUILD_USER_INFO -> (guilda, cargo).</summary>
    static async Task<(uint Guild, int Class)> StateAsync(TestClient c)
    {
        var r = await c.ExpectAsync(0x1BD);
        Assert.Equal(1u, r.U32());
        var s = r.Struct<GUILD_USER_INFO>();
        return (s.guildUID, s.classIdx);
    }

    [Fact]
    public async Task FullLifecycleWithPermissions()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, aId, _) = await EnterAsync(env, kit: false);
        var (b, bId, _) = await EnterAsync(env, kit: false);
        await using var _a = a;
        await using var _b = b;
        string name = "G" + Guid.NewGuid().ToString("N")[..10];

        await a.SendAsync(new PacketWriter(0xFE).Str(name).Str("intro"));          // sem kit
        Assert.Equal((uint)GuildCode.NoKit, (await a.ExpectAsync(0x1B3)).U32());
        var p = (await env.Players.LoadAsync(aId))!;
        await a.DisposeAsync();                                                     // dá o kit e entra de novo
        await new ShopService(env.Players.Store, env.Data).GiveAsync(p, GuildService.CreateKit, 1);
        var accA = (await env.S.Accounts.FindByLoginAsync(p.Login))!;
        await using var a2 = await LoginAsync(env, accA, await env.S.Sessions.IssueGameLoginAsync(aId));

        await a2.SendAsync(new PacketWriter(0xFF).Str(name));                       // nome livre
        var check = await a2.ExpectAsync(0x1B4);
        Assert.Equal((1u, name), (check.U32(), check.Str()));
        await a2.SendAsync(new PacketWriter(0xFE).Str(name).Str("a melhor guilda"));
        Assert.Equal(1u, (await a2.ExpectAsync(0x1B3)).U32());
        Assert.Equal(1, (await a2.ExpectAsync(0xA5)).U8());                         // kit gasto
        var (gid, cls) = await StateAsync(a2);
        Assert.Equal(GuildClass.Master, cls);
        Assert.Null((await env.Players.LoadAsync(aId))!.FindType(GuildService.CreateKit));

        await b.SendAsync(new PacketWriter(0xFF).Str(name.ToLowerInvariant()));     // nome em uso (sem diferenciar maiúsculas)
        Assert.Equal((uint)GuildCode.NameTaken, (await b.ExpectAsync(0x1B4)).U32());
        await b.SendAsync(new PacketWriter(0x106).U32(1).Str(name));                // busca
        var found = await b.ExpectAsync(0x1BB);
        Assert.Equal((1u, 1u, 1u, 1), (found.U32(), found.U32(), found.U32(), (int)found.U16()));
        Assert.Equal(gid, found.Struct<GUILD_LIST>().guildUID);

        await b.SendAsync(new PacketWriter(0x109).U32(gid).Str("me aceita"));       // pedido
        Assert.Equal(1u, (await b.ExpectAsync(0x1BE)).U32());
        Assert.Equal((gid, GuildClass.Waiting), await StateAsync(b));
        await a2.SendAsync(new PacketWriter(0x10F).U32(gid).U32(1));               // membros + pedido
        var list = await a2.ExpectAsync(0x1C4);
        Assert.Equal((1u, 1u, 2u, 2), (list.U32(), list.U32(), list.U32(), (int)list.U16()));
        var first = list.Struct<GUILD_USER_LIST>();
        var second = list.Struct<GUILD_USER_LIST>();
        Assert.Equal(((uint)aId, GuildClass.Master), (first.userUID, first.classIdx));
        Assert.Equal(((uint)bId, GuildClass.Waiting, (byte)1), (second.userUID, second.classIdx, second.online));

        await b.SendAsync(new PacketWriter(0x10B).U32(gid).U32((uint)bId));         // quem pediu não se aprova
        Assert.Equal((uint)GuildCode.NotInGuild, (await b.ExpectAsync(0x1C0)).U32());
        await a2.SendAsync(new PacketWriter(0x10B).U32(gid).U32((uint)bId));        // aprovar
        Assert.Equal(1u, (await a2.ExpectAsync(0x1C0)).U32());
        Assert.Equal((gid, GuildClass.Member), await StateAsync(b));
        await b.SendAsync(new PacketWriter(0x111).U32(gid).U32((uint)aId));         // membro não expulsa
        Assert.Equal((uint)GuildCode.NotManager, (await b.ExpectAsync(0x1C6)).U32());
        await b.SendAsync(new PacketWriter(0x102).U32(gid).U32((uint)bId).Str("aviso"));   // membro não muda notícia
        Assert.Equal((uint)GuildCode.NotManager, (await b.ExpectAsync(0x1B7)).U32());
        await a2.SendAsync(new PacketWriter(0x102).U32(gid).U32((uint)aId).Str("treino `hoje`"));
        Assert.Equal(1u, (await a2.ExpectAsync(0x1B7)).U32());

        await a2.SendAsync(new PacketWriter(0x10D).U32(gid).U32((uint)bId).U32(2)); // promover
        var promo = await a2.ExpectAsync(0x1C2);
        Assert.Equal((1u, 2u), (promo.U32(), promo.U32()));
        Assert.Equal((gid, GuildClass.SubMaster), await StateAsync(b));
        await a2.SendAsync(new PacketWriter(0x10D).U32(gid).U32((uint)bId).U32(1)); // passar o cargo de mestre
        Assert.Equal(1u, (await a2.ExpectAsync(0x1C2)).U32());
        Assert.Equal((gid, GuildClass.Master), await StateAsync(b));
        Assert.Equal((gid, GuildClass.Member), await StateAsync(a2));             // o antigo mestre vira membro

        await b.SendAsync(new PacketWriter(0x104).U32(gid));                        // encerrar com membro: não
        Assert.Equal((uint)GuildCode.HasMembers, (await b.ExpectAsync(0x1B9)).U32());
        await a2.SendAsync(new PacketWriter(0x110).U32(gid));                       // A sai
        Assert.Equal(1u, (await a2.ExpectAsync(0x1C5)).U32());
        Assert.Equal((0u, 0), await StateAsync(a2));
        await a2.SendAsync(new PacketWriter(0x109).U32(gid).Str("volta"));          // 24 h de espera
        Assert.Equal((uint)GuildCode.Cooldown, (await a2.ExpectAsync(0x1BE)).U32());

        var gNow = await env.S.Guilds.GetAsync((int)gid);
        Assert.Equal(("treino 'hoje'", bId), (gNow!.Notice, gNow.MasterId));
        await b.SendAsync(new PacketWriter(0x104).U32(gid));                        // agora encerra
        Assert.Equal(1u, (await b.ExpectAsync(0x1B9)).U32());
        Assert.Equal((0u, 0), await StateAsync(b));
        Assert.Null(await env.S.Guilds.GetAsync((int)gid));

        await b.SendAsync(new PacketWriter(0x107));                                 // histórico de B
        var hist = await b.ExpectAsync(0x1BC);
        Assert.Equal(1u, hist.U32());
        var states = new List<int>();
        for (int n = hist.U16(), i = 0; i < n; i++) states.Add(hist.Struct<GUILD_HISTORY>().state);
        Assert.Equal([GuildState.Closed, GuildState.BecameMaster, GuildState.BecameSubMaster, GuildState.Approved, GuildState.Requested], states);
    }

    [Fact]
    public async Task LoginCarriesTheGuild()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, id, _) = await EnterAsync(env, kit: true);
        string name = "L" + Guid.NewGuid().ToString("N")[..10];
        await c.SendAsync(new PacketWriter(0xFE).Str(name).Str("intro"));
        Assert.Equal(1u, (await c.ExpectAsync(0x1B3)).U32());
        await c.DisposeAsync();
        var p = (await env.Players.LoadAsync(id))!;
        var acc = (await env.S.Accounts.FindByLoginAsync(p.Login))!;
        await using var c2 = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c2, acc, await env.S.Sessions.IssueGameLoginAsync(id));
        var info = await c2.ExpectAsync(0x42);
        info.U8(); info.Str(); info.Str();
        var ui = info.Struct<sUserInfo>();
        Assert.NotEqual(0u, ui.info.dwGuildId);
        info.Struct<SYSTEMTIME>();
        info.Bytes(24);                                                           // flags/papel (8) + flagBlock, controlServerService... (16)
        var gi = info.Struct<GUILD_USER_INFO>();
        Assert.Equal((ui.info.dwGuildId, GuildClass.Master), (gi.guildUID, gi.classIdx));
    }
}
