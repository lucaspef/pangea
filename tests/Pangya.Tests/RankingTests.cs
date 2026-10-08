using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Players;
using Pangya.Domain.Ranking;
using Pangya.Ranking;

namespace Pangya.Tests;

/// <summary>Regras do retrato do ranking (SPEC-ranking.md §6.2).</summary>
public class RankingRuleTests
{
    static Player P(long id, int level, long holes = 0, long total = 0, long albatross = 0, (int Course, int Best)? course = null)
    {
        var p = new Player { AccountId = id, Nickname = "n" + id, Level = level };
        p.Stats.Hole = holes;
        p.Stats.TotalScore = total;
        p.Stats.Albatross = albatross;
        if (course is { } c) p.Courses[c.Course] = new CourseRecord { BestScore = c.Best, Games = 1, Holes = 18 };
        return p;
    }

    [Fact]
    public void BoardsOrderFilterAndRememberThePreviousPosition()
    {
        var players = new List<Player>
        {
            P(1, 5, holes: 18, total: 18, albatross: 1),            // média +1/buraco -> 2000
            P(2, 30, holes: 18, total: 0, albatross: 3, course: (0, -4)),   // par -> 3000; Blue Lagoon 68
            P(3, 50, holes: 0, course: (0, 2)),                      // sem buracos: fora do placar; Blue Lagoon 74
        };
        var s1 = RankingService.Build(players, null, DateTime.UtcNow);
        var score = s1.Board(0, 1, 0)!;
        Assert.Equal([2L, 1L], [score.Entries[0].Uid, score.Entries[1].Uid]);
        Assert.Equal((3000, 2000), (score.Entries[0].Value, score.Entries[1].Value));
        Assert.All(score.Entries, e => Assert.Equal(0, e.Previous));          // primeiro retrato: tudo "new"
        var course = s1.Board(1, 0, 0)!;                                     // por curso: menos tacadas primeiro
        Assert.Equal([(2L, 68), (3L, 74)], [(course.Entries[0].Uid, course.Entries[0].Value), (course.Entries[1].Uid, course.Entries[1].Value)]);
        Assert.Equal(2L, s1.Board(2, 0, 0)!.Entries[0].Uid);                 // albatross
        Assert.Equal([3L, 2L, 1L], [s1.Board(2, 3, 0)!.Entries[0].Uid, s1.Board(2, 3, 0)!.Entries[1].Uid, s1.Board(2, 3, 0)!.Entries[2].Uid]);
        Assert.Single(s1.Board(2, 3, 1)!.Entries);                           // classe 1: até o nível 20
        Assert.Single(s1.Board(2, 3, 3)!.Entries);                           // classe 3: 41+
        Assert.Equal(16, RankingService.CourseBySub.Length - 1);
        Assert.Equal(19, RankingService.CourseBySub[16]);                    // Wiz City

        players[0].Stats.TotalScore = -18;                                   // jogador 1 melhora: passa o 2
        var s2 = RankingService.Build(players, s1, DateTime.UtcNow);
        var e1 = s2.Board(0, 1, 0)!.Entries[0];
        Assert.Equal((1L, 1, 2), (e1.Uid, e1.Position, e1.Previous));       // subiu da 2ª para a 1ª
    }
}

/// <summary>Ranking pela rede: 0x47 -> 0xA0, hello 0x1F4, página, ficha e busca.</summary>
[Collection("db")]
public class RankingTests(DbFixture fx)
{
    [Fact]
    public async Task ButtonAddressPageCardAndSearch()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, keyA) = await env.NewPlayerAsync();
        var (b, _) = await env.NewPlayerAsync();
        await env.S.Players.ApplyAsync(a.Id, new PlayerChanges { Stats = new PlayerStats { Hole = 36, TotalScore = 9, Albatross = 2 } });
        await env.S.Players.ApplyAsync(b.Id, new PlayerChanges { Stats = new PlayerStats { Hole = 18, TotalScore = -3 } });
        var rank = new RankingServer(env.S, env.Game.World, 0);
        using var cts = new CancellationTokenSource();
        _ = await rank.StartAsync(cts.Token);
        env.Game.Context.Ranking = rank.Context;

        var g = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(g, a, keyA);
        await g.ExpectAsync(0x94);
        await g.SendAsync(new PacketWriter(0x47));
        var addr = await g.ExpectAsync(0xA0);
        Assert.Equal(("127.0.0.1", (uint)rank.Tcp.Port), (addr.Str(), addr.U32()));

        var r = await TestClient.ConnectAsync(rank.Tcp.Port);
        var (id, hello) = await r.ReceiveAsync();
        Assert.Equal(0x1F4, id);
        r.Key = (int)hello.U32();
        Assert.Equal(5, hello.U8());

        // login errado: código 1 com o cabeçalho ecoado
        await r.SendAsync(new PacketWriter(0x00).U32((uint)a.Id).Str("outro").U8(0).U8(1).U8(0).U8(0).U32(0).U8(1));
        var bad = await r.ExpectAsync(0x1F5);
        Assert.Equal((1, 0, 1), (bad.U8(), bad.U8(), bad.U8()));

        // geral / placar: b (−3 em 18) antes de a (+9 em 36), e a minha linha (a)
        await r.SendAsync(new PacketWriter(0x00).U32((uint)a.Id).Str(a.Login).U8(0).U8(1).U8(0).U8(0).U32(0).U8(1));
        var page = await r.ExpectAsync(0x1F5);
        Assert.Equal((0, 0, 1, 0, 0), (page.U8(), page.U8(), page.U8(), page.U8(), page.U8()));
        Assert.Equal(0u, page.U32());
        page.U32();
        int n = page.U16();
        Assert.True(n >= 2);
        var rows = new List<(uint Uid, uint Pos, int Val, string Nick)>();
        for (int i = 0; i < n; i++)
        {
            uint uid = page.U32(), pos = page.U32(); page.U32();
            int val = page.I32(); page.U16(); page.U8();
            string idField = page.Str(), nick = page.Str();
            Assert.Equal(nick, idField);                                      // o login não sai do servidor
            rows.Add((uid, pos, val, nick));
        }
        int ia = rows.FindIndex(x => x.Uid == a.Id), ib = rows.FindIndex(x => x.Uid == b.Id);
        Assert.True(ib >= 0 && ia > ib);
        Assert.Equal(0, page.U8());                                          // minha linha vem
        Assert.Equal((uint)a.Id, page.U32());

        // página além do fim: código 2 com a última página válida
        await r.SendAsync(new PacketWriter(0x00).U32((uint)a.Id).Str(a.Login).U8(0).U8(1).U8(0).U8(0).U32(999).U8(0));
        var end = await r.ExpectAsync(0x1F5);
        Assert.Equal(2, end.U8());

        // ficha: 0x22C bytes depois do código
        await r.SendAsync(new PacketWriter(0x01).U32((uint)b.Id).Str(b.Nickname!).U8(0));
        var card = await r.ExpectAsync(0x1F6);
        Assert.Equal(0, card.U8());
        Assert.Equal(0x22C, card.Remaining);
        Assert.Equal((uint)b.Id, card.U32());
        Assert.Equal(b.Nickname, Cp949.Read(card.Bytes(22)));

        // busca por nick (sem diferenciar caixa) -> página e índice
        await r.SendAsync(new PacketWriter(0x02).U8(0).Str(b.Nickname!.ToUpperInvariant()).U8(0).U8(1).U8(0).U8(0).U32(0));
        var found = await r.ExpectAsync(0x1F8);
        Assert.Equal(0, found.U8());
        found.U8(); found.U8(); found.U8(); found.U8();
        uint fpage = found.U32(); found.U32();
        int fn = found.U16();
        for (int i = 0; i < fn; i++) { found.U32(); found.U32(); found.U32(); found.I32(); found.U16(); found.U8(); found.Str(); found.Str(); }
        Assert.Equal(rows[ib].Pos - 1, fpage * 12 + found.U16());
        await r.SendAsync(new PacketWriter(0x02).U8(1).U32(100000).U8(0).U8(1).U8(0).U8(0).U32(0));
        Assert.Equal(1, (await r.ExpectAsync(0x1F8)).U8());                   // posição que não existe
        cts.Cancel();
    }

    [Fact]
    public async Task WithoutRankingTheButtonShowsMaintenance()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, key) = await env.NewPlayerAsync();
        var g = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(g, a, key);
        var info = await g.ExpectAsync(0x42);
        info.U8(); info.Str(); info.Str();
        info.Struct<Pangya.Protocol.KR645.sUserInfo>();
        info.Bytes(16);                                                       // SYSTEMTIME
        info.U8(); info.U8(); info.U16(); info.U16(); info.U16();
        info.U32();                                                           // flagBlock
        Assert.Equal(0x10000u, info.U32());                                   // controlServerService: ranking em manutenção
    }
}
