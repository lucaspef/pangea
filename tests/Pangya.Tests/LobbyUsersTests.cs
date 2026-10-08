using Pangya.Core.Net;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Lista de usuários do lobby (0x44): entrar, ir para uma sala e voltar atualizam a entrada para todos.</summary>
[Collection("db")]
public class LobbyUsersTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> EnterLobbyAsync(GameEnv env)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x04).U8(0));
        await c.ExpectAsync(0x4C);
        await c.SendAsync(new PacketWriter(0x81));
        return (c, acc.Id);
    }

    static List<(uint Uid, ushort Room)> Users(PacketReader r, out byte sub)
    {
        sub = r.U8();
        var list = new List<(uint, ushort)>();
        for (int n = r.U8(), i = 0; i < n; i++)
        {
            var b = r.Struct<sBriefUserInfo>();
            list.Add((b.dwUid, b.roomIndex));
        }
        return list;
    }

    [Fact]
    public async Task RoomMovesAreShownToEveryoneAndLeavingClearsTheRoom()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, aId) = await EnterLobbyAsync(env);
        await using var _a = a;
        Users(await a.ExpectAsync(0x44), out _);                            // a lista inicial (só A)
        var (b, bId) = await EnterLobbyAsync(env);
        await using var _b = b;
        var added = Users(await a.ExpectAsync(0x44), out var sub);          // A vê B entrar
        Assert.Equal((1, (uint)bId, (ushort)0xFFFF), (sub, added[0].Uid, added[0].Room));
        var full = Users(await b.ExpectAsync(0x44), out _);                 // B recebe a lista com os dois
        Assert.Contains(((uint)aId, (ushort)0xFFFF), full);
        Assert.Contains(((uint)bId, (ushort)0xFFFF), full);

        await a.SendAsync(new PacketWriter(0x08).U8(0).U32(60000).U32(0).U8(4).U8(0).U8(3).U8(0).U8(0).Str("t").Str(""));
        var inRoom = Users(await b.ExpectAsync(0x44), out sub);             // B vê A numa sala
        Assert.Equal((3, (uint)aId), (sub, inRoom[0].Uid));
        Assert.NotEqual(0xFFFF, inRoom[0].Room);

        await a.SendAsync(new PacketWriter(0x0F));
        var back = Users(await b.ExpectAsync(0x44), out sub);               // e de volta ao lobby
        Assert.Equal((3, (uint)aId, (ushort)0xFFFF), (sub, back[0].Uid, back[0].Room));
        var self = Users(await a.ExpectAsync(0x44), out sub);               // o próprio A também é atualizado
        Assert.Equal((3, (uint)aId, (ushort)0xFFFF), (sub, self[0].Uid, self[0].Room));

        await a.DisposeAsync();                                             // A desconecta: sai da lista de B
        var gone = Users(await b.ExpectAsync(0x44), out sub);
        Assert.Equal((2, (uint)aId), (sub, gone[0].Uid));
    }
}
