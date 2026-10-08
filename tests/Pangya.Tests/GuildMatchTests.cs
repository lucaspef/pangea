using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Guilds;
using Pangya.Domain.Players;
using Pangya.Domain.Rooms;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Tests;

/// <summary>GuildMatch (modo 6, SPEC-guildmatch.md): sala por guilda, pares, pontos por buraco e resultado.</summary>
public class GuildMatchTests
{
    static FakeSession Member(long id, int guild, int cls = GuildClass.Member)
    {
        var s = new FakeSession(id);
        s.Player.Guild = new GuildTag(guild, "G" + guild, "g" + guild, cls, 0);
        return s;
    }

    static RoomPlayer JoinAs(Room room, FakeSession s) =>
        RoomManager.Join(room, new RoomPlayer { Guid = (uint)s.Player.AccountId, Player = s.Player, Session = s });

    [Fact]
    public void RoomSidesFollowTheGuilds()
    {
        var mgr = new RoomManager();
        var room = mgr.Create(new RoomSettings { Mode = GameMode.GuildMatch, MaxPlayers = 7, Holes = 3, Course = 2 }, 1);
        Assert.Equal((RoomManager.RandomCourse, (byte)9, (byte)10), (room.Settings.Course, room.Settings.Holes, room.Settings.MaxPlayers));
        Assert.Equal(JoinResult.GuildRequired, RoomManager.CanJoin(room, "", new FakeSession(9).Player));
        Assert.Equal(JoinResult.GuildRequired, RoomManager.CanJoin(room, "", Member(9, 50, GuildClass.Waiting).Player));   // pedido pendente
        var a1 = JoinAs(room, Member(1, 10));
        var b1 = JoinAs(room, Member(2, 20));
        var a2 = JoinAs(room, Member(3, 10));
        Assert.Equal((0, 1, 0), (a1.Team, b1.Team, a2.Team));
        Assert.Equal((10, 20), (room.GuildSides[0]!.Id, room.GuildSides[1]!.Id));
        Assert.Equal(JoinResult.Full, RoomManager.CanJoin(room, "", Member(4, 30).Player));   // terceira guilda
        mgr.Leave(room, b1);
        Assert.Null(room.GuildSides[1]);                                                // lado azul livre de novo
        Assert.Equal(JoinResult.Ok, RoomManager.CanJoin(room, "", Member(4, 30).Player));

        var info = RoomPackets.RoomInfo(room);
        Assert.Equal(10u, info.GuildInfo.nGuildID[0]);
        Assert.Equal("G10", Cp949.Read(info.GuildInfo.szName[0]));
        Assert.Equal("g10", Cp949.Read(info.GuildInfo.szEmblemName[0]));
    }

    static (TourneyGame Game, MassRecorder Out, Room Room, RoomManager Mgr) Setup(int perSide, byte holes)
    {
        var mgr = new RoomManager();
        var room = mgr.Create(new RoomSettings { Mode = GameMode.GuildMatch, MaxPlayers = 30, Holes = holes }, 1);
        for (int i = 0; i < perSide; i++)
        {
            JoinAs(room, Member(100 + i, 10));
            JoinAs(room, Member(200 + i, 20));
        }
        RoomManager.PrepareStart(room, new Random(1));
        room.HoleOrder = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        var o = new MassRecorder();
        var g = (TourneyGame)MassGame.For(room, o, mgr.Sync, TimeSpan.FromHours(1));
        room.Game = g;
        for (byte h = 1; h <= 18; h++) g.HoleData(h, new HoleInfo(4, 0, 0, 0, 320));
        return (g, o, room, mgr);
    }

    static void Hole(TourneyGame g, MassPlayer p, int n)
    {
        g.Loaded(p);
        for (int k = 1; k <= n; k++)
        {
            g.Shoot(p, 0);
            g.Result(p, new ShotResult(p.Guid, 0, 0, 320, k == n ? ShotResult.StateHoled : (byte)5, 5, 0));
            g.ShotFinished(p);
        }
    }

    MassPlayer P(TourneyGame g, uint guid) => g.Find(guid)!;

    [Fact]
    public void PairsScoreHolesAndDecideTheWinner()
    {
        var (g, o, _, _) = Setup(2, 9);
        Assert.Equal(9, g.HoleCount);                                        // modo 6: só 9 ou 18 buracos
        Assert.Equal([(1, 100u, 200u), (2, 101u, 201u)], g.Pairs.ConvertAll(x => ((int)x.Group, x.Red.Guid, x.Blue.Guid)));
        Hole(g, P(g, 100), 3);                                               // par 1, buraco 1: 100 = 3
        Assert.Empty(o.Scores);                                              // ainda falta o adversário
        Hole(g, P(g, 200), 4);                                               // 200 = 4: 100 ganha 2
        Assert.Equal((200u, (short)2, (short)0, 0, 2), o.Scores[^1]);
        Hole(g, P(g, 101), 4);
        Hole(g, P(g, 201), 4);                                               // empate: 1/1
        Assert.Equal(((short)3, (short)1), (g.SidePoints(0), g.SidePoints(1)));
        Hole(g, P(g, 100), 5);
        Hole(g, P(g, 200), 3);                                               // buraco 2: 200 ganha
        Hole(g, P(g, 101), 4);
        Hole(g, P(g, 201), 5);                                               // 101 ganha: vermelha 5 x 3
        Assert.Equal(((short)5, (short)3), (g.SidePoints(0), g.SidePoints(1)));
        for (int h = 2; h < 9; h++)                                          // o resto empatado: +2 para cada lado por buraco
            foreach (uint guid in new uint[] { 100, 200, 101, 201 }) Hole(g, P(g, guid), 4);
        Assert.True(g.Over);
        var r = o.Result!.Guild!;
        Assert.Equal((0, 19, 17), (r.Winner, r.Points[0], r.Points[1]));
        Assert.Equal(100, r.PangWin[P(g, 100)]);                             // (2 pares + 0 saídas) × 50
        Assert.Equal(50, r.PangWin[P(g, 200)]);                              // 0 × 50 + 50
        Assert.Equal(0, o.Result.MatchTid);
        Assert.Empty(o.Result.Medals);
        Assert.True(o.Events.LastIndexOf("gscore 201") < o.Events.IndexOf("over"));   // último 0xC0 antes do 0x77
    }

    [Fact]
    public void LeavingGivesTheOpponentTheHoles()
    {
        var (g, o, room, mgr) = Setup(1, 9);
        Hole(g, P(g, 200), 5);                                               // 200 fecha o buraco 1
        Hole(g, P(g, 200), 5);                                               // e o 2
        lock (mgr.Sync) mgr.Leave(room, room.Find(100u)!);                   // 100 sai sem ter fechado nenhum
        Assert.Equal((200u, (short)0, (short)4, 4, 0), o.Scores[^1]);         // 2 + 2 para o 200
        for (int h = 2; h < 9; h++) Hole(g, P(g, 200), 6);                   // adversário fora: 2 por buraco
        Assert.True(g.Over);
        Assert.Equal((1, 18), (o.Result!.Guild!.Winner, o.Result.Guild.Points[1]));
    }

    [Fact]
    public void PairsPacketLayout()
    {
        var (g, _, _, _) = Setup(2, 9);
        var w = MassOutput.GuildPairs(g.Pairs);
        var r = new PacketReader();
        r.Reset(w.Body.ToArray(), 0, w.Body.Length);
        Assert.Equal(0xBD, r.Id);
        Assert.Equal(2, r.U8());
        Assert.Equal((1, 100u, 200u), (r.U8(), r.U32(), r.U32()));
        Assert.Equal(9, r.Remaining);
    }
}

/// <summary>Criar sala de guilda sem guilda: 0x47 código 13 ("길드에 가입해야 합니다.").</summary>
[Collection("db")]
public class GuildMatchRoomTests(DbFixture fx)
{
    [Fact]
    public async Task CreatingWithoutAGuildIsRefused()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x08).U8(0).U32(0).U32(0).U8(10).U8(6).U8(18).U8(0x7F).U8(0).Str("guerra").Str(""));
        Assert.Equal(13, (await c.ExpectAsync(0x47)).U8());
        Assert.Empty(env.Game.World.Rooms.Rooms);
    }

    [Fact]
    public async Task MatchIsRecordedForGuildsAndMembers()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, _) = await env.NewPlayerAsync();
        var (b, _) = await env.NewPlayerAsync();
        var store = env.S.Guilds;
        string n1 = "R" + Guid.NewGuid().ToString("N")[..8], n2 = "B" + Guid.NewGuid().ToString("N")[..8];
        int red = await store.CreateAsync(n1, n1.ToLowerInvariant(), "", a.Id, new GuildChange());
        int blue = await store.CreateAsync(n2, n2.ToLowerInvariant(), "", b.Id, new GuildChange());
        await store.RecordMatchAsync(new GuildMatchRecord(red, blue, [19, 17], [100, 50], 0,
            [(a.Id, red, 19, 100), (b.Id, blue, 17, 50)]));
        var gr = (await store.GetAsync(red))!;
        var gb = (await store.GetAsync(blue))!;
        Assert.Equal((19, 100, 17, 50), (gr.Point, gr.Pang, gb.Point, gb.Pang));
        await using var db = await env.S.Db.OpenAsync();
        var (wins, losses) = await Dapper.SqlMapper.QuerySingleAsync<(int, int)>(db,
            "select (select wins from guilds where id = @red), (select losses from guilds where id = @blue)", new { red, blue });
        Assert.Equal((1, 1), (wins, losses));
        Assert.Equal(19, await Dapper.SqlMapper.ExecuteScalarAsync<int>(db, "select point from guild_members where account_id = @id", new { id = a.Id }));

        // perfil: 0x14F termina com os pontos do membro; 0x155 traz a guilda (pang/pontos)
        var (c, key) = (await env.ConnectAsync(), await env.S.Sessions.IssueGameLoginAsync(b.Id));
        await using var _c = c;
        await GameEnv.SendLoginAsync(c, b, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x2F).U32((uint)a.Id).U8(5));
        var head = await c.ExpectAsync(0x14F);
        head.U8(); head.U32(); head.U16();
        head.Struct<Pangya.Protocol.KR645.sPangYaUserInfo>();
        Assert.Equal(19u, head.U32());
        var gi = await c.ExpectAsync(0x155);
        Assert.Equal((uint)a.Id, gi.U32());
        var info = gi.Struct<Pangya.Protocol.KR645.GUILD_INFO>();
        Assert.Equal(((uint)red, 19, 100), (info.guildUID, info.guildPoint, info.guildPang));
    }
}
