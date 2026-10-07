using Pangya.Core.Config;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

public class CourseRecordRuleTests
{
    [Fact]
    public void OnlyFullCourseCountsAsRecord()
    {
        var r = new CourseResult(3, 3, -1, 500, 0x04000000).ApplyTo(null);   // 3 buracos: soma, sem recorde
        Assert.Equal((1, 3, -1, CourseRecord.NoRecord, 500L), (r.Games, r.Holes, r.TotalScore, r.BestScore, r.MaxPang));
        var r2 = new CourseResult(3, 18, -5, 300, 0x04000001).ApplyTo(r);
        Assert.Equal((2, 21, -6, -5, 500L, 0x04000001), (r2.Games, r2.Holes, r2.TotalScore, r2.BestScore, r2.MaxPang, r2.CharacterTypeId));
        var r3 = new CourseResult(3, 18, 2, 900, 0x04000002).ApplyTo(r2);     // pior: mantém o recorde e o personagem
        Assert.Equal((-5, 0x04000001, 900L), (r3.BestScore, r3.CharacterTypeId, r3.MaxPang));
        Assert.Equal(1, r.Games);                                           // o original não muda
    }
}

/// <summary>Estatística por curso no banco e no perfil (docs/protocolo/SPEC-perfil-mapas.md).</summary>
[Collection("db")]
public class CourseRecordTests(DbFixture fx)
{
    [Fact]
    public async Task GameEndSavesCourseAndProfileSendsAllCourses()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var cfg = new RewardConfig();
        await Rewards.ApplyAsync(env.Players.Store, p, 400, 0, 18, true, cfg, (5, -3));
        await Rewards.ApplyAsync(env.Players.Store, p, 100, 0, 3, true, cfg, (5, 1));
        await Rewards.ApplyAsync(env.Players.Store, p, 100, 0, 3, false, cfg, (7, 0));     // saiu antes: não conta
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Single(saved.Courses);
        var rec = saved.Courses[5];
        Assert.Equal((2, 21, -2, -3, 400L), (rec.Games, rec.Holes, rec.TotalScore, rec.BestScore, rec.MaxPang));
        Assert.Equal(p.Character!.TypeId, rec.CharacterTypeId);

        var c = await env.ConnectAsync();
        await using var _c = c;
        await GameEnv.SendLoginAsync(c, acc, key);
        var login = await c.ExpectAsync(0x42);
        login.U8(); login.Str(); login.Str();
        var ui = login.Struct<sUserInfo>();                                 // HUD: recordes do 0x42 por índice de curso
        Assert.Equal((5, (sbyte)-3), (ui.mapStat[5].bMap, ui.mapStat[5].cBestScore));
        Assert.Equal((19, (sbyte)127), (ui.mapStat[19].bMap, ui.mapStat[19].cBestScore));
        await c.ExpectAsync(0x94);

        await c.SendAsync(new PacketWriter(0x2F).U32((uint)acc.Id).U8(5));
        var normal = await c.ExpectAsync(0x154);
        Assert.Equal((5, (uint)acc.Id, 20), (normal.U8(), normal.U32(), (int)normal.U16()));
        for (int i = 0; i < 20; i++)
        {
            var s = normal.Struct<sMapStatistics>();
            Assert.Equal(i, s.bMap);
            if (i == 5) Assert.Equal((21u, -2, (sbyte)-3, 400L), (s.dwHole, s.iTotalScore, s.cBestScore, s.i64MaxPang));
            else Assert.Equal((sbyte)127, s.cBestScore);
        }
        var classic = await c.ExpectAsync(0x154);                           // clássico: os 20 cursos, sem dados
        Assert.Equal(0x33, classic.U8());
        classic.U32();
        Assert.Equal(20, classic.U16());
        for (int i = 0; i < 20; i++)
        {
            var s = classic.Struct<sMapStatistics>();
            Assert.Equal((i, (sbyte)127), (s.bMap, s.cBestScore));
        }

        await c.SendAsync(new PacketWriter(0x2F).U32((uint)acc.Id).U8(0));   // temporada anterior: vazia
        var prev = await c.ExpectAsync(0x154);
        Assert.Equal(0, prev.U8());
        prev.U32();
        Assert.Equal(20, prev.U16());
        for (int i = 0; i < 20; i++) Assert.Equal((sbyte)127, prev.Struct<sMapStatistics>().cBestScore);   // nada na anterior
        Assert.Equal(0x0A, (await c.ExpectAsync(0x154)).U8());
    }
}
