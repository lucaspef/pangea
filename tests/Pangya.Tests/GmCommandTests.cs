using Pangya.Core.Net;
using Pangya.Domain.Rooms;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Comandos de GM do toolkit e de sala (docs/protocolo/SPEC-gm-comandos.md).</summary>
[Collection("db")]
public class GmCommandTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> LobbyAsync(GameEnv env, int identity)
    {
        var (acc, key) = await env.NewPlayerAsync();
        if (identity != 0) await env.S.Accounts.SetIdentityFlagsAsync(acc.Id, identity);
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x04).U8(0));
        await c.ExpectAsync(0x4C);
        await c.SendAsync(new PacketWriter(0x81));                              // entra na lista do lobby
        return (c, acc.Id);
    }

    static PacketWriter CreateRoom(byte mode) =>
        new PacketWriter(0x08).U8(0).U32(0).U32(0).U8(4).U8(mode).U8(1).U8(0).U8(0).Str("sala").Str("");

    static async Task<ushort> EnteredAsync(TestClient c)
    {
        var r = await c.ExpectAsync(0x47);
        r.U8(); r.U8();
        return r.Struct<sRoomInfo>().roomGuid;
    }

    [Fact]
    public void WindIsCappedAndSentToTheGame()
    {
        var (_, g, o) = ModeSetup.Turn(GameMode.Stroke, 2, 2);
        g.SetWind(20, 77);
        Assert.Equal(((byte)8, (byte)77), (g.WindStrength, g.WindDirection));
        Assert.Equal(((byte)8, (byte)77), o.LastWind);
    }

    [Fact]
    public async Task WeatherVisibleAndGiveItem()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (gm, gmId) = await LobbyAsync(env, 0x04);
        var (user, userId) = await LobbyAsync(env, 0);
        await using var _g = gm;
        await using var _u = user;

        // /visible off: a entrada do GM no 0x44 sai com o bit de GM e state 0 (o cliente esconde de quem não é GM)
        await gm.SendAsync(new PacketWriter(0x8C).U16(3).U16(0));
        sBriefUserInfo b = default;
        while (b.dwUid != (uint)gmId)
        {
            var l = await user.ExpectAsync(0x44);
            if (l.U8() != 3 || l.U8() != 1) continue;                            // a lista inicial vem antes
            b = l.Struct<sBriefUserInfo>();
        }
        Assert.Equal((0x04u, (ushort)0), (b.dwIdentity, b.state));

        // usuário comum não manda clima nem item
        await user.SendAsync(new PacketWriter(0x8C).U16(18).U32((uint)userId).U32(SpinCubeService.LuckyKey).U32(3));
        // /giveitem: carta do sistema e aviso 0x31
        await gm.SendAsync(new PacketWriter(0x8C).U16(18).U32((uint)userId).U32(SpinCubeService.LuckyKey).U32(3));
        await user.ExpectAsync(0x31);
        var mail = await env.S.Mail.UnreadAsync(userId, 5);
        var letter = Assert.Single(mail);
        Assert.Equal((SpinCubeService.LuckyKey, 3), (letter.Items[0].TypeId, letter.Items[0].Quantity));

        // /weather no lounge: a sala toda recebe 0x9C; quem entra depois recebe ao confirmar o avatar
        await gm.SendAsync(new PacketWriter(0x08).U8(0).U32(0).U32(0).U8(30).U8(2).U8(5).U8(0).U8(0).Str("lounge").Str(""));
        ushort lounge = await EnteredAsync(gm);
        await gm.SendAsync(new PacketWriter(0x63).U8(4).F32(0).F32(0).F32(0));
        await user.SendAsync(new PacketWriter(0x8C).U16(15).U8(3));             // não é GM nem está na sala: nada
        await gm.SendAsync(new PacketWriter(0x8C).U16(15).U8(2));
        var w = await gm.ExpectAsync(0x9C);
        Assert.Equal((2, 0, 0), (w.U8(), w.U8(), w.U8()));
        Assert.Equal((byte)2, env.Game.World.Rooms.Get(lounge)!.Weather);
        await user.SendAsync(new PacketWriter(0x09).U16(lounge).Str(""));
        await EnteredAsync(user);
        await user.SendAsync(new PacketWriter(0x63).U8(4).F32(1).F32(1).F32(0));
        Assert.Equal(2, (await user.ExpectAsync(0x9C)).U8());

        var audit = new HashSet<string>();
        for (int i = 0; i < 50 && audit.Count < 3; i++)
        {
            audit.Clear();
            foreach (var e in await env.S.Audit.Store.RecentAsync(100)) if (e.ActorId == gmId) audit.Add(e.Action);
            await Task.Delay(20);
        }
        Assert.Superset(new HashSet<string> { "gm:status", "gm:giveitem", "gm:weather" }, audit);
        Assert.Single(await env.S.Mail.UnreadAsync(userId, 5));                 // o pedido do usuário comum não gerou carta
    }

    [Fact]
    public async Task F10RemovesFromRoomAndDestroyEmptiesIt()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (gm, gmId) = await LobbyAsync(env, 0x04);
        var (user, userId) = await LobbyAsync(env, 0);
        var (other, otherId) = await LobbyAsync(env, 0);
        await using var _g = gm;
        await using var _u = user;
        await using var _o = other;

        await gm.SendAsync(CreateRoom(0));
        ushort index = await EnteredAsync(gm);
        await user.SendAsync(new PacketWriter(0x09).U16(index).Str(""));
        await EnteredAsync(user);
        await gm.SendAsync(new PacketWriter(0x4C).U32((uint)userId));           // F10 no slot do usuário
        for (int i = 0; i < 50 && env.Game.World.Rooms.Get(index)?.Find((uint)userId) != null; i++) await Task.Delay(20);
        Assert.Null(env.Game.World.Rooms.Get(index)!.Find((uint)userId));
        Assert.NotNull(env.Game.World.Find(userId));                            // continua conectado

        await other.SendAsync(CreateRoom(0));
        ushort second = await EnteredAsync(other);
        await user.SendAsync(new PacketWriter(0x60).U16(second));              // usuário comum: nada
        await Task.Delay(100);
        Assert.NotNull(env.Game.World.Rooms.Get(second));
        await gm.SendAsync(new PacketWriter(0x60).U16(second));                // GM (dentro de outra sala também funciona)
        for (int i = 0; i < 50 && env.Game.World.Rooms.Get(second) != null; i++) await Task.Delay(20);
        Assert.Null(env.Game.World.Rooms.Get(second));
        Assert.NotNull(env.Game.World.Find(otherId));
        _ = gmId;
    }
}
