using Pangya.Core.Net;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Lounge (sala de avatar, modo 2): docs/protocolo/SPEC-lounge-loja.md.</summary>
[Collection("db")]
public class LoungeTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> EnterAsync(GameEnv env)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id);
    }

    /// <summary>0x46 sub 0: u16 tem de ser 0xFFFF; devolve (guid, x, z, ângulo) de cada avatar.</summary>
    static List<(uint Guid, float X, float Z, float A)> Avatars(PacketReader r)
    {
        Assert.Equal(0, r.U8());
        Assert.Equal(0xFFFF, r.U16());
        var list = new List<(uint, float, float, float)>();
        for (int n = r.U8(), i = 0; i < n; i++)
        {
            var s = r.Struct<sSlotInfo>();
            r.Struct<sCharacterInfo>();
            list.Add((s.dwGuid, s.location[0], s.location[1], s.location[2]));
        }
        return list;
    }

    [Fact]
    public async Task AvatarsAppearMoveAndAreResentUntilConfirmed()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, aId) = await EnterAsync(env);
        var (b, bId) = await EnterAsync(env);
        await using var _a = a;
        await using var _b = b;

        await a.SendAsync(new PacketWriter(0x08).U8(0).U32(0).U32(0).U8(30).U8(2).U8(5).U8(0).U8(0).Str("lounge").Str(""));
        var enter = await a.ExpectAsync(0x47);
        enter.U8(); enter.U8();
        var info = enter.Struct<sRoomInfo>();
        Assert.Equal((2, (byte)30), (info.gameType, info.nUserLimit));
        Avatars(await a.ExpectAsync(0x46));
        Assert.Single(Avatars(await a.ExpectAsync(0x46, 3000)));            // reenviado (a task do lounge ainda não existia)
        await a.SendAsync(new PacketWriter(0x63).U8(4).F32(10).F32(20).F32(1.5f));   // apareci: para o reenvio
        var lounge = env.Game.World.Rooms.Get(info.roomGuid)!;
        for (int i = 0; i < 100 && lounge.Find((uint)aId)!.X != 10; i++) await Task.Delay(10);

        await b.SendAsync(new PacketWriter(0x09).U16(info.roomGuid).Str(""));
        await b.ExpectAsync(0x47);
        var seen = Avatars(await b.ExpectAsync(0x46));
        Assert.Contains(((uint)aId, 10f, 20f, 1.5f), seen);                 // B vê A onde A está
        var added = await a.ExpectAsync(0x46);
        Assert.Equal((1, (ushort)0xFFFF), (added.U8(), added.U16()));      // A vê B entrar
        await b.SendAsync(new PacketWriter(0x63).U8(4).F32(0).F32(0).F32(0));
        var appear = await a.ExpectAsync(0xC2);
        Assert.Equal(((uint)bId, 4), (appear.U32(), appear.U8()));

        await a.SendAsync(new PacketWriter(0x63).U8(6).F32(3).F32(-4).F32(2f));      // anda
        var move = await b.ExpectAsync(0xC2);
        Assert.Equal(((uint)aId, 6, 3f, -4f, 2f), (move.U32(), move.U8(), move.F32(), move.F32(), move.F32()));
        await a.SendAsync(new PacketWriter(0x63).U8(6).F32(9999).F32(0).F32(0));     // salto impossível: ignorado
        await a.SendAsync(new PacketWriter(0x63).U8(7).Str("dance"));                 // emote
        var mot = await b.ExpectAsync(0xC2);
        Assert.Equal(((uint)aId, 7, "dance"), (mot.U32(), mot.U8(), mot.Str()));
        var me = lounge.Find((uint)aId)!;
        Assert.Equal((13f, 16f), (me.X, me.Z));

        await a.SendAsync(new PacketWriter(0x0E).U32((uint)aId));                     // lounge não tem partida
        Assert.Equal(1, (await a.ExpectAsync(0x7D)).U8());
    }
}
