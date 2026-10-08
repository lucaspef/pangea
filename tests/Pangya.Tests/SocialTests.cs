using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Tests;

/// <summary>Pacotes pequenos de sala e de itens (GameHandler.Social.cs).</summary>
[Collection("db")]
public class SocialTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id)> EnterAsync(GameEnv env)
    {
        var (acc, key) = await env.NewPlayerAsync();
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id);
    }

    static PacketWriter MakeRoom(byte mode = 0) =>
        new PacketWriter(0x08).U8(0).U32(60000).U32(0).U8(4).U8(mode).U8(3).U8(0).U8(0).Str("t").Str("");

    [Fact]
    public async Task HeadIconTeamChatDetailAndBanish()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, aId) = await EnterAsync(env);
        var (b, bId) = await EnterAsync(env);
        await using var _a = a;
        await using var _b = b;
        await a.SendAsync(MakeRoom());
        var enter = await a.ExpectAsync(0x47);
        enter.U8(); enter.U8();
        ushort index = enter.Struct<Pangya.Protocol.KR645.sRoomInfo>().roomGuid;
        await b.SendAsync(new PacketWriter(0x09).U16(index).Str(""));
        await b.ExpectAsync(0x47);
        await a.ExpectAsync(0x46);                                          // B entrou (vaga para A)

        await a.SendAsync(new PacketWriter(0x18).U16(3));                    // ícone: só os outros recebem
        var icon = await b.ExpectAsync(0x5B);
        Assert.Equal(((uint)aId, (ushort)3), (icon.U32(), icon.U16()));

        await a.SendAsync(new PacketWriter(0x54).Str("vamos"));              // equipe de A (inclui o próprio A)
        var tc = await a.ExpectAsync(0xAE);
        tc.Str();
        Assert.Equal("vamos", tc.Str());

        await a.SendAsync(new PacketWriter(0x2D).U16(index));               // detalhe da sala
        var det = await a.ExpectAsync(0x84);
        Assert.Equal(2, det.U8());
        await a.SendAsync(new PacketWriter(0x2D).U16(999));
        Assert.Equal(0, (await a.ExpectAsync(0x84)).U8());                  // não existe

        await b.SendAsync(new PacketWriter(0x26).U32((uint)aId));           // quem não é dono não expulsa
        await a.SendAsync(new PacketWriter(0x26).U32((uint)bId));           // o dono expulsa B
        await b.ExpectAsync(0x4A);
        var gone = await a.ExpectAsync(0x46);
        Assert.Equal(2, gone.U8());                                          // vaga removida
        Assert.Single(env.Game.World.Rooms.Get(index)!.Players);
    }

    [Fact]
    public async Task DeleteItemRulesAndSmallReplies()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        await shop.GiveAsync(p, 0x18000004, 5);
        await shop.GiveAsync(p, 0x1C000010, 1);                              // caddie (para recontratar)
        int caddie = p.FindType(0x1C000010)!.Id;
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);

        await c.SendAsync(new PacketWriter(0x64).U32(0x18000004).U32(2));     // 5 -> 3
        var cnt = await c.ExpectAsync(0xA5);
        Assert.Equal((1, 0x18000004u), (cnt.U8(), cnt.U32()));
        cnt.U32();
        Assert.Equal(3, cnt.U16());
        Assert.Equal(0, (await c.ExpectAsync(0xA8)).U16());
        await c.SendAsync(new PacketWriter(0x64).U32(0x18000004).U32(9));     // tudo: apaga
        Assert.Equal(1, (await c.ExpectAsync(0xA5)).U8());
        await c.ExpectAsync(0xA8);
        await c.SendAsync(new PacketWriter(0x64).U32(Item.BasicBall).U32(1)); // bola básica: recusado, só fecha a espera
        Assert.Equal(0, (await c.ExpectAsync(0xA8)).U16());
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Null(saved.FindType(0x18000004));
        Assert.NotNull(saved.FindType(Item.BasicBall));

        await c.SendAsync(new PacketWriter(0x38).Str("novonick"));            // troca de nick: suspensa
        Assert.Equal(7u, (await c.ExpectAsync(0x4E)).U32());
        await c.SendAsync(new PacketWriter(0x39).U32((uint)caddie));          // recontratar: grátis
        var re = await c.ExpectAsync(0x91);
        Assert.Equal((2, (uint)caddie), (re.U8(), re.U32()));
    }

    [Fact]
    public async Task GiveUpSoloEndsTheGameWithoutReward()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, id) = await EnterAsync(env);
        await using var _c = c;
        long pang = (await env.Players.LoadAsync(id))!.Pang;
        await c.SendAsync(MakeRoom());
        await c.ExpectAsync(0x46);
        await c.SendAsync(new PacketWriter(0x0E).U32((uint)id));
        await c.ExpectAsync(0x50);
        await c.SendAsync(new PacketWriter(0x37));
        var end = await c.ExpectAsync(0x64);                                 // placar
        Assert.Equal(1, end.U8());
        end.U32(); end.U8(); end.U8(); end.U8();
        Assert.Equal(0, end.I16());                                          // sem EXP
        await Task.Delay(300);
        Assert.Equal(pang, (await env.Players.LoadAsync(id))!.Pang);
    }

    [Fact]
    public async Task WhisperInviteAndFollow()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, aId) = await EnterAsync(env);
        var (b, bId) = await EnterAsync(env);
        await using var _a = a;
        await using var _b = b;
        string bNick = (await env.Players.LoadAsync(bId))!.Nickname, aNick = (await env.Players.LoadAsync(aId))!.Nickname;

        await a.SendAsync(new PacketWriter(0x2A).Str(bNick).Str("oi b"));          // sussurro
        var echo = await a.ExpectAsync(0x82);
        Assert.Equal((0, bNick, "oi b"), (echo.U8(), echo.Str(), echo.Str()));
        var got = await b.ExpectAsync(0x82);
        Assert.Equal((1, aNick, "oi b"), (got.U8(), got.Str(), got.Str()));
        await a.SendAsync(new PacketWriter(0x2A).Str("ninguem").Str("oi"));        // não está online
        Assert.Equal(6, (await a.ExpectAsync(0x3E)).U8());

        await a.SendAsync(MakeRoom());
        var enter = await a.ExpectAsync(0x47);
        enter.U8(); enter.U8();
        ushort index = enter.Struct<Pangya.Protocol.KR645.sRoomInfo>().roomGuid;
        await a.SendAsync(new PacketWriter(0xB2).Str(bNick).U32((uint)bId));        // convite
        var inv = await a.ExpectAsync(0x127);
        Assert.Equal(0, inv.U16());
        inv.U32(); inv.U8();
        Assert.Equal((index, (uint)bId), (inv.U16(), inv.U32()));
        inv.Str();
        uint id = inv.U32();
        await a.SendAsync(new PacketWriter(0x29).U32(id));
        var invited = await b.ExpectAsync(0x81);
        Assert.Equal(0, invited.U16());
        invited.U32(); invited.U8();
        Assert.Equal((index, (uint)aId, aNick, id), (invited.U16(), invited.U32(), invited.Str(), invited.U32()));

        await b.SendAsync(new PacketWriter(0xAC).U8(0).U16(index));                // segue até a sala
        Assert.Equal(0, (await b.ExpectAsync(0x47)).U8());
        await a.SendAsync(new PacketWriter(0xB2).Str(bNick).U32((uint)bId));        // já está numa sala
        Assert.Equal(6, (await a.ExpectAsync(0x127)).U16());
        await b.SendAsync(new PacketWriter(0xB7).U8(0));                           // partida rápida: sem alvo
        Assert.Equal(1, (await b.ExpectAsync(0x133)).U8());
    }
}
