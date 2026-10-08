using Pangya.Domain.Players;
using Pangya.Domain.Rooms;

namespace Pangya.Tests;

/// <summary>Troféus do torneio (SPEC-resultado-fim-de-jogo.md §3.4).</summary>
public class TrophyRuleTests
{
    [Fact]
    public void RoomTrophyFollowsAverageLevel()
    {
        Assert.Equal(0x2C000000, Trophy.RoomTid([1, 2, 3]));                // amador 6º
        Assert.Equal(0x2C060000, Trophy.RoomTid([30, 30, 34]));             // pro 1º
        Assert.Equal(0x2C0C0000, Trophy.RoomTid([70, 70]));                 // pro 7º (máximo)
        Assert.Equal(6, Trophy.RankOf(0x2C060000));
        Assert.Equal(-1, Trophy.RankOf(0x2D0A0100));                        // troféu especial: não conta
    }

    [Fact]
    public void TrophiesByPlayersAndHoles()
    {
        Assert.Empty(Trophy.ByPosition(9, 18));
        Assert.Equal([Trophy.Bronze], Trophy.ByPosition(10, 18));
        Assert.Equal([Trophy.Silver, Trophy.Bronze], Trophy.ByPosition(15, 18));
        Assert.Equal([Trophy.Gold, Trophy.Silver, Trophy.Bronze, Trophy.Bronze], Trophy.ByPosition(23, 18));
        Assert.Equal(6, Trophy.ByPosition(30, 18).Length);
        Assert.Empty(Trophy.ByPosition(14, 9));
        Assert.Equal([Trophy.Bronze], Trophy.ByPosition(15, 9));
        Assert.Equal([Trophy.Gold, Trophy.Silver, Trophy.Bronze], Trophy.ByPosition(27, 9));
        Assert.Empty(Trophy.ByPosition(30, 3));
    }

    [Fact]
    public void CountsAreKeptPerRankAndCloneIsDeep()
    {
        var s = new PlayerStats();
        Trophy.Add(s.Trophies, 0x2C060000, Trophy.Gold);
        Trophy.Add(s.Trophies, 0x2C060000, Trophy.Gold);
        Trophy.Add(s.Trophies, 0x2C000000, Trophy.Bronze);
        Trophy.Add(s.Trophies, 0x2D0A0100, Trophy.Gold);                    // ignorado
        Trophy.Add(s.Trophies, 0x2C060000, 4);                              // ignorado
        Assert.Equal(2, s.Trophies[6 * 3]);
        Assert.Equal(1, s.Trophies[2]);
        var c = s.Clone();
        Trophy.Add(c.Trophies, 0x2C060000, Trophy.Gold);
        Assert.Equal(2, s.Trophies[6 * 3]);                                 // o original não muda
        Assert.Equal(3, c.Trophies[6 * 3]);
        var old = new PlayerStats { Trophies = [1, 2] };                    // gravado antes de existir a tabela toda
        Assert.Equal(Trophy.Count, old.Clone().Trophies.Length);
    }

    [Fact]
    public void StructHasThirteenRanksOfThree()
    {
        var p = new Player();
        Trophy.Add(p.Stats.Trophies, 0x2C0C0000, Trophy.Silver);
        var t = Pangya.Protocol.KR645.Game.PlayerStructs.Trophies(p);
        Assert.Equal(1, t.Trophy[12][1]);
        Assert.Equal(0, t.Trophy[12][0]);
    }
}

/// <summary>A contagem de troféus é gravada com a recompensa e volta no perfil.</summary>
[Collection("db")]
public class TrophySaveTests(DbFixture fx)
{
    [Fact]
    public async Task TrophyIsSavedWithTheReward()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, _) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var cfg = new Pangya.Core.Config.RewardConfig();
        await Rewards.ApplyAsync(env.Players.Store, p, 100, 0, 18, true, cfg, trophy: (0x2C030000, Trophy.Silver));
        await Rewards.ApplyAsync(env.Players.Store, p, 100, 0, 18, false, cfg, trophy: (0x2C030000, Trophy.Gold));   // saiu: nada
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Equal(1, saved.Stats.Trophies[3 * 3 + 1]);
        Assert.Equal(0, saved.Stats.Trophies[3 * 3]);
        Assert.Equal(1, Pangya.Protocol.KR645.Game.PlayerStructs.UserInfo(saved).trophy.Trophy[3][1]);
    }
}

/// <summary>Fim do torneio: classificação, troféus e itens.</summary>
public class TourneyTrophyTests
{
    static (TourneyGame Game, MassRecorder Out, object Sync) Setup(int humans, byte holes, int level, bool bot = false, bool countBots = false)
    {
        var mgr = new RoomManager();
        var room = mgr.Create(new RoomSettings { Holes = holes, Mode = GameMode.Tournament, MaxPlayers = 30 }, 1);
        for (int i = 0; i < humans; i++)
        {
            var s = new FakeSession(100 + i);
            s.Player.Level = level;
            RoomManager.Join(room, new RoomPlayer { Guid = (uint)(100 + i), Player = s.Player, Session = s });
        }
        if (bot) RoomManager.Join(room, new RoomPlayer { Guid = 0x7F000001, Player = new Player { AccountId = 0x7F000001, Level = level } });
        RoomManager.PrepareStart(room, new Random(1));
        room.HoleOrder = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        var o = new MassRecorder();
        var g = (TourneyGame)MassGame.For(room, o, mgr.Sync, TimeSpan.FromMilliseconds(5), countBots);
        room.Game = g;
        for (byte h = 1; h <= holes; h++) g.HoleData(h, new HoleInfo(4, 0, 0, 0, 320));
        return (g, o, mgr.Sync);
    }

    /// <summary>Joga o buraco atual de p em n tacadas (a última acerta).</summary>
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

    static void PlayAll(TourneyGame g, byte holes, Func<int, int> strokesOf)
    {
        for (int h = 0; h < holes; h++)
            for (int i = 0; i < g.Players.Count; i++)
                if (!g.Players[i].IsBot && !g.Over) Hole(g, g.Players[i], strokesOf(i));
    }

    [Fact]
    public void TenPlayersOn18HolesGiveBronzeToTheWinner()
    {
        var (g, o, _) = Setup(10, 18, 30);
        Assert.Equal(0x2C060000, g.MatchTid);
        PlayAll(g, 18, i => i == 3 ? 3 : 4);                                // o 4º da lista joga melhor
        Assert.True(g.Over);
        var r = o.Result!;
        Assert.Equal(0x2C060000, r.MatchTid);
        var a = Assert.Single(r.Awards);
        Assert.Equal((103u, 0, Trophy.Bronze), (a.Player.Guid, a.Position, a.Trophy));
        Assert.InRange(a.ItemTid, TourneyGame.AwardItemBase, TourneyGame.AwardItemBase + TourneyGame.AwardItemCount - 1);
        Assert.Equal(Trophy.Bronze, r.TrophyOf(a.Player));
        Assert.Equal(0, r.TrophyOf(g.Players[0]));
    }

    [Fact]
    public void FewPlayersOrShortGameGiveNoTrophy()
    {
        var (g, o, _) = Setup(9, 18, 10);
        PlayAll(g, 18, _ => 4);
        Assert.Equal(0x2C020000, o.Result!.MatchTid);
        Assert.Empty(o.Result.Awards);

        (g, o, _) = Setup(10, 9, 10);                                           // 9 buracos: só com 15+
        PlayAll(g, 9, _ => 4);
        Assert.Empty(o.Result!.Awards);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task BotsCountOnlyWhenConfigured(bool countBots, int awards)
    {
        var (g, o, sync) = Setup(9, 18, 10, bot: true, countBots: countBots);
        Assert.Equal(10, g.Players.Count);
        lock (sync) PlayAll(g, 18, _ => 1);                                  // humanos com hole-in-one: o bot fica atrás
        TourneyResult? result = null;
        for (int i = 0; i < 500 && result == null; i++)                      // o bot termina os buracos sozinho
        {
            await Task.Delay(10);
            lock (sync) result = o.Result;                                    // o fim é gravado sob o lock da sala
        }
        Assert.NotNull(result);
        Assert.Equal(awards, result.Awards.Count);
        if (awards > 0) Assert.False(result.Awards[0].Player.IsBot);
    }

    [Fact]
    public void OtherMassModesHaveNoRoomTrophy()
    {
        var mgr = new RoomManager();
        var room = mgr.Create(new RoomSettings { Holes = 3, Mode = GameMode.Team30s, MaxPlayers = 30 }, 1);
        var s = new FakeSession(100);
        RoomManager.Join(room, new RoomPlayer { Guid = 100, Player = s.Player, Session = s });
        RoomManager.PrepareStart(room, new Random(1));
        var g = (TourneyGame)MassGame.For(room, new MassRecorder(), mgr.Sync, TimeSpan.FromHours(1));
        Assert.Equal(0, g.MatchTid);
        Assert.Equal(0, Pangya.Protocol.KR645.Game.RoomPackets.MatchTid(room));
    }
}
