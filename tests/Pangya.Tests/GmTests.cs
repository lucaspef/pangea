using Pangya.Core.Net;

namespace Pangya.Tests;

/// <summary>Chat e comandos de GM (docs/protocolo/SPEC-chat-gm.md).</summary>
[Collection("db")]
public class GmTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> EnterAsync(GameEnv env, int identity)
    {
        var (acc, key) = await env.NewPlayerAsync();
        if (identity != 0) await env.S.Accounts.SetIdentityFlagsAsync(acc.Id, identity);
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id);
    }

    [Fact]
    public async Task GmChatIsBlueNoticeReachesEveryoneAndKickNeedsGm()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (gm, gmId) = await EnterAsync(env, 0x14);
        var (user, userId) = await EnterAsync(env, 0);
        await using var _g = gm;

        await gm.SendAsync(new PacketWriter(0x03).Str("x").Str("oi"));
        Assert.Equal(0x80, (await gm.ExpectAsync(0x3E)).U8());              // azul
        await user.SendAsync(new PacketWriter(0x03).Str("x").Str("oi"));
        Assert.Equal(0, (await user.ExpectAsync(0x3E)).U8());

        await user.SendAsync(new PacketWriter(0x57).Str("falso"));           // não é GM: ignorado
        await gm.SendAsync(new PacketWriter(0x57).Str("manutencao as 22h"));
        Assert.Equal("manutencao as 22h", (await user.ExpectAsync(0x40)).Str());
        var chat = await user.ExpectAsync(0x3E);
        Assert.Equal(7, chat.U8());
        chat.Str();
        Assert.Equal("manutencao as 22h", chat.Str());
        Assert.Equal("manutencao as 22h", (await gm.ExpectAsync(0x40)).Str());

        await user.SendAsync(new PacketWriter(0x8C).U16(10).U32((uint)gmId).U8(0));   // usuário comum não expulsa
        await gm.SendAsync(new PacketWriter(0x8C).U16(10).U32((uint)userId).U8(0));
        await using (user)
            await Assert.ThrowsAnyAsync<Exception>(async () => { while (true) await user.ReceiveAsync(); });
        for (int i = 0; i < 50 && env.Game.World.Find(userId) != null; i++) await Task.Delay(20);
        Assert.Null(env.Game.World.Find(userId));
        Assert.NotNull(env.Game.World.Find(gmId));
    }

    [Fact]
    public async Task IdentityCommandOnlyChangesTheGmsOwnView()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (gm, gmId) = await EnterAsync(env, 0x14);
        var (user, _) = await EnterAsync(env, 0);
        await using var _g = gm;
        await using var _u = user;
        await user.SendAsync(new PacketWriter(0x41).U32(0x14).Str(""));           // comum: ignorado
        await user.SendAsync(new PacketWriter(0x03).Str("x").Str("ping"));
        Assert.Equal(0, (await user.ExpectAsync(0x3E)).U8());                    // chegou o chat, não um 0x98
        await gm.SendAsync(new PacketWriter(0x41).U32(0x80).Str(""));             // GM se vê como comum
        Assert.Equal(0x80u, (await gm.ExpectAsync(0x98)).U32());
        var acc = await env.S.Accounts.FindByLoginAsync((await env.Players.LoadAsync(gmId))!.Login);
        Assert.Equal(0x14, acc!.IdentityFlags);                                  // a conta continua GM
    }
}
