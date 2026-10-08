using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Protocol.KR645;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Tests;

public class GameEndPacketTests
{
    [Fact]
    public void LevelGiftsFollowClientTable()
    {
        Assert.Equal([(0x18000008, 1), (0x18000007, 1)], Levels.Gifts(1));
        Assert.Equal([(Levels.PangPouch, 3000)], Levels.Gifts(6));
        Assert.Equal([(0x1A000033, 1)], Levels.Gifts(40));
        Assert.Empty(Levels.Gifts(0));
    }

    [Fact]
    public void ItemsWonLayout()
    {
        using var w = GameHandler.ItemsWon([(100u, [0x1A00015B, 0x1A00015B]), (101u, [])]);
        var body = w.Body.ToArray();
        var r = new PacketReader();
        r.Reset(body, 0, body.Length);
        Assert.Equal(0xF8, r.Id);
        Assert.Equal(2, r.U16());
        Assert.Equal((100u, 0, 2, 0x1A00015Bu, 0x1A00015Bu), (r.U32(), r.U8(), (int)r.U16(), r.U32(), r.U32()));
        Assert.Equal((101u, 0, 0), (r.U32(), r.U8(), (int)r.U16()));
        Assert.Equal(0, r.Remaining);
    }
}

/// <summary>Fim de partida em duas fases: EXP antes do placar; 0x43/0xC6 (e presentes de nível) só depois do 0x06.</summary>
[Collection("db")]
public class GameEndFlowTests(DbFixture fx)
{
    static async Task<(TestClient C, long Id, GameHandler H)> EnterAsync(GameEnv env, int level, int exp)
    {
        var (acc, key) = await env.NewPlayerAsync();
        await using (var db = await env.S.Db.OpenAsync())
            await Dapper.SqlMapper.ExecuteAsync(db, "update players set level = @level, exp = @exp where account_id = @id",
                new { level = (short)level, exp, id = acc.Id });
        var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        return (c, acc.Id, (GameHandler)env.Game.World.Find(acc.Id)!);
    }

    static PacketWriter FinalStats(uint holeIn) =>
        new PacketWriter(0x06).Struct(new sPangYaUserStatistics { dwHole = 3, dwHoleIn = holeIn, dwPutt = 4 });

    [Fact]
    public async Task ExpIsPreviewedAndSavedAfterFinalStats()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, id, h) = await EnterAsync(env, 5, 0);
        await using var _c = c;
        int exp = h.BeginGameEnd(300, 0, 3, true, (2, -1));
        Assert.Equal(6, exp);                                              // 2 por buraco
        Assert.Equal(0, (await env.Players.LoadAsync(id))!.Exp);           // ainda não gravou
        await c.SendAsync(FinalStats(1));
        var st = await c.ExpectAsync(0x43);
        var s = st.Struct<sPangYaUserStatistics>();
        Assert.Equal((6u, (byte)5, 1u), (s.dwExp, s.Level, s.dwHoleIn));   // EXP novo e a estatística do 0x06
        await c.ExpectAsync(0xC6);
        var saved = (await env.Players.LoadAsync(id))!;
        Assert.Equal((6, 1L), (saved.Exp, saved.Stats.HoleIn));
    }

    [Fact]
    public async Task LevelUpSendsGiftsAndLevelUpDialog()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, id, h) = await EnterAsync(env, 0, 25);                    // faltam 5 para o nível 1
        await using var _c = c;
        h.BeginGameEnd(100, 0, 3, true);
        await c.SendAsync(FinalStats(0));
        var seen = new List<ushort>();
        while (true)
        {
            var (pid, r) = await c.ReceiveAsync();
            seen.Add(pid);
            if (pid == 0x10D) Assert.Equal((1, 1, 0), (r.U8(), r.U8(), r.U8()));   // feito, nível 1, tipo 0
            if (pid == 0xC6) break;
        }
        Assert.True(seen.IndexOf(0x10D) < seen.IndexOf(0x43));
        var p = (await env.Players.LoadAsync(id))!;
        Assert.Equal(1, p.Level);
        Assert.NotNull(p.FindType(0x18000008));                            // presentes do nível 1
        Assert.NotNull(p.FindType(0x18000007));
    }

    [Fact]
    public async Task PlayerWhoLeftGetsNothing()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (c, id, h) = await EnterAsync(env, 5, 0);
        await using var _c = c;
        Assert.Equal(0, h.BeginGameEnd(300, 0, 3, false));
        await c.SendAsync(FinalStats(1));
        await Task.Delay(200);
        Assert.Equal((0, 0L), ((await env.Players.LoadAsync(id))!.Exp, (await env.Players.LoadAsync(id))!.Stats.Games));
    }
}
