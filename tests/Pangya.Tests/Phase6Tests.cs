using Pangya.Domain.Players;
using Pangya.Domain.Rooms;

namespace Pangya.Tests;

static class ModeSetup
{
    public static (Room Room, StrokeGame Game, FakeOutput Out) Turn(GameMode mode, int humans, byte holes, params byte[] teams)
    {
        var mgr = new RoomManager();
        var room = mgr.Create(new RoomSettings { Holes = holes, Mode = mode }, 1);
        for (int i = 0; i < humans; i++)
        {
            var s = new FakeSession(100 + i);
            var rp = RoomManager.Join(room, new RoomPlayer { Guid = (uint)(100 + i), Player = s.Player, Session = s });
            if (teams.Length > i) rp.Team = teams[i];
        }
        RoomManager.PrepareStart(room, new Random(1));
        room.HoleOrder = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        var o = new FakeOutput();
        var g = StrokeGame.For(room, o, mgr.Sync, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        o.Game = g;
        room.Game = g;
        for (byte h = 1; h <= holes; h++) g.HoleData(h, new HoleInfo(4, 0, 0, 0, 320));
        foreach (var p in g.Players) g.Loaded(p);
        return (room, g, o);
    }

    /// <summary>Tacada do jogador da vez até a posição z (estado 4 = acertou).</summary>
    public static GamePlayer Play(StrokeGame g, float z, byte state = 5)
    {
        var p = g.Turn!;
        Assert.True(g.Shoot(p));
        Assert.True(g.Result(new ShotResult(p.Guid, 0, 0, z, state, 10, 0)));
        foreach (var h in g.Players) g.ShotFinished(h);
        return p;
    }
}

public class TeamMatchSkinsTests
{
    [Fact]
    public void TeamMembersAlternateShotsOnOneBall()
    {
        // 2 x 2: slots 100/101 no time 0, 102/103 no time 1 (como a sala define)
        var (_, g, o) = ModeSetup.Turn(GameMode.Team, 4, 1, 0, 0, 1, 1);
        var side = (SideGame)g;
        Assert.Equal(0, side.SideOf(g.Players[0]));
        Assert.Equal(100u, g.Turn!.Guid);                      // lado do slot 1 sai do tee
        ModeSetup.Play(g, 100);                                // lado 0 a 220 da bandeira
        Assert.Equal(102u, g.Turn!.Guid);                      // lado 1 ainda no tee
        ModeSetup.Play(g, 50);                                 // lado 1 a 270: mais longe, joga de novo
        Assert.Equal(103u, g.Turn!.Guid);                      // o OUTRO membro do lado 1 (tacada alternada)
        ModeSetup.Play(g, 320, ShotResult.StateHoled);         // lado 1 acertou em 2
        Assert.Equal(101u, g.Turn!.Guid);
        ModeSetup.Play(g, 300);                                // lado 0 com 2 tacadas sem acertar: buraco decidido
        Assert.StartsWith("end", o.Last);
        Assert.Equal(GameEndKind.Team, o.End!.Kind);
        Assert.Equal([0, 1], o.End.SideWins);
        Assert.Equal(1, o.End.Winner);
        Assert.Equal(2, o.End.Results.Find(r => r.Guid == 102).Rank == 1 ? 2 : 0);
    }

    [Fact]
    public void MatchHalvedHoleAndEarlyWin()
    {
        var (_, g, o) = ModeSetup.Turn(GameMode.Match, 2, 3);
        // buraco 1: os dois acertam em 1 = dividido
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        Assert.Equal("hole", o.Last);
        // buraco 2: 100 acerta em 1, 101 erra -> 100 ganha
        foreach (var p in g.Players) g.Loaded(p);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        ModeSetup.Play(g, 10);
        Assert.Equal("hole", o.Last);
        // buraco 3: 101 acerta em 1, 100 erra -> empate em buracos (1 x 1)
        foreach (var p in g.Players) g.Loaded(p);
        Assert.Equal(100u, g.Turn!.Guid);                      // quem fez menos tacadas no buraco anterior sai primeiro
        ModeSetup.Play(g, 10);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);         // 101 acerta em 1 e 100 já tem 1: decidido
        Assert.Equal(GameEndKind.Match, o.End!.Kind);
        Assert.All(o.End.Results, r => Assert.Equal(1, r.Score));
        Assert.All(o.End.Results, r => Assert.Equal(1, r.Rank));   // empate
    }

    [Fact]
    public void PangBattleCarriesOverAndPaysTheWinner()
    {
        var (_, g, o) = ModeSetup.Turn(GameMode.PangBattle, 2, 3);
        var skins = (SkinsGame)g;
        Assert.Equal(20, skins.HolePang(1));
        Assert.Equal(40, skins.HolePang(3));                   // último buraco vale o dobro
        // buraco 1: empate em 1 -> acumula
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        // buraco 2: só 100 acerta -> leva 20 x 2 (acumulado)
        foreach (var p in g.Players) g.Loaded(p);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        ModeSetup.Play(g, 10);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);
        Assert.Equal(40, skins.Net(g.Players[0]));
        Assert.Equal(-40, skins.Net(g.Players[1]));
        // buraco 3: 101 acerta em 1, 100 em 2 -> 101 leva 60
        foreach (var p in g.Players) g.Loaded(p);
        Assert.Equal(100u, g.Turn!.Guid);                      // 100 fez menos no buraco 2: sai primeiro
        ModeSetup.Play(g, 10);
        ModeSetup.Play(g, 320, ShotResult.StateHoled);         // 101 acerta em 1
        ModeSetup.Play(g, 320, ShotResult.StateHoled);         // 100 acerta em 2: 101 leva 40
        Assert.True(g.Over);
        Assert.Equal(GameEndKind.PangBattle, o.End!.Kind);
        Assert.Equal(0, o.End.Results.Find(r => r.Guid == 100).Net);
        Assert.Equal(0, o.End.Results.Find(r => r.Guid == 101).Net);
        Assert.Equal(101u, o.End.LastHoleWinner);
    }
}

sealed class MassRecorder : IMassOutput
{
    public List<string> Events { get; } = [];
    public List<ApproachEntry>? Totals { get; private set; }
    public List<ApproachEntry>? LastHole { get; private set; }
    public void HoleStart(MassPlayer to, byte wind, byte direction, bool approach) => Events.Add($"start {to.Guid}");
    public void TeeReady(MassPlayer? to) => Events.Add($"tee {(to == null ? "all" : to.Guid)}");
    public void RivalPos(MassPlayer p, byte hole, float x, float z, bool approach) => Events.Add($"pos {p.Guid}");
    public void RivalHole(MassPlayer p, byte hole) => Events.Add($"hole {p.Guid} {hole} {p.Total} {p.Score}");
    public void RivalState(MassPlayer p, byte state) => Events.Add($"state {p.Guid} {state}");
    public void NextHole(MassPlayer? to) => Events.Add($"next {(to == null ? "all" : to.Guid)}");
    public void ApproachHole(List<ApproachEntry> entries) { LastHole = entries; Events.Add("ahole"); }
    public void ApproachEnd(List<ApproachEntry> totals) { Totals = totals; Events.Add("aend"); }
    public void GameOver(List<MassPlayer> players) => Events.Add("over");
}

public class MassModeTests
{
    static (Room, MassGame, MassRecorder, object) Setup(GameMode mode, int humans, bool bot, byte holes)
    {
        var mgr = new RoomManager();
        var room = mgr.Create(new RoomSettings { Holes = holes, Mode = mode, MaxPlayers = 30 }, 1);
        Assert.Equal(30, room.Settings.MaxPlayers);
        for (int i = 0; i < humans; i++)
        {
            var s = new FakeSession(100 + i);
            RoomManager.Join(room, new RoomPlayer { Guid = (uint)(100 + i), Player = s.Player, Session = s });
        }
        if (bot) RoomManager.Join(room, new RoomPlayer { Guid = 0x7F000001, Player = new Player { AccountId = 0x7F000001 } });
        RoomManager.PrepareStart(room, new Random(1));
        room.HoleOrder = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        var o = new MassRecorder();
        var g = MassGame.For(room, o, mgr.Sync, TimeSpan.FromMilliseconds(20));
        room.Game = g;
        for (byte h = 1; h <= holes; h++) g.HoleData(h, new HoleInfo(4, 0, 0, 0, 320));
        return (room, g, o, mgr.Sync);
    }

    [Fact]
    public void TourneyPlayersAdvanceIndependently()
    {
        var (room, g, o, _) = Setup(GameMode.Tournament, 2, false, 2);
        var (a, b) = (g.Players[0], g.Players[1]);
        g.Loaded(a);
        Assert.Contains("start 100", o.Events);
        Assert.DoesNotContain("start 101", o.Events);          // cada um começa sozinho
        g.Shoot(a, 0);
        g.Result(a, new ShotResult(a.Guid, 0, 0, 300, 5, 5, 0));
        g.Result(a, new ShotResult(a.Guid, 0, 0, 300, 5, 5, 0));  // o cliente manda o 0x1B duas vezes
        Assert.Single(o.Events.FindAll(e => e == "pos 100"));
        g.ShotFinished(a);
        Assert.DoesNotContain(o.Events, e => e.StartsWith("hole"));   // bola em jogo: nada (sem 0x61)
        g.Shoot(a, 0);
        g.Result(a, new ShotResult(a.Guid, 0, 0, 320, ShotResult.StateHoled, 9, 1));
        g.ShotFinished(a);
        Assert.Contains("hole 100 1 2 -2", o.Events);
        Assert.Equal("next 100", o.Events[^1]);
        // b nem começou: a termina o buraco 2 e espera b
        g.Loaded(a);
        g.Shoot(a, 0);
        g.Result(a, new ShotResult(a.Guid, 0, 0, 320, ShotResult.StateHoled, 9, 1));
        g.ShotFinished(a);
        Assert.Contains("state 100 2", o.Events);
        Assert.False(g.Over);
        g.PlayerLeft(b.RoomPlayer);                            // b sai: fim
        Assert.Contains("state 101 3", o.Events);
        Assert.True(g.Over);
        Assert.Contains("over", o.Events);
        Assert.Equal(RoomState.Waiting, room.State);
    }

    [Fact]
    public async Task TourneyBotFinishesHolesAfterTheHuman()
    {
        var (_, g, o, sync) = Setup(GameMode.Tournament, 1, true, 1);
        var a = g.Players[0];
        lock (sync)
        {
            g.Loaded(a);
            g.Shoot(a, 0);
            g.Result(a, new ShotResult(a.Guid, 0, 0, 320, ShotResult.StateHoled, 9, 1));
            g.ShotFinished(a);
        }
        for (int i = 0; i < 200 && !g.Over; i++) await Task.Delay(10);
        Assert.True(g.Over);
        Assert.Contains(o.Events, e => e.StartsWith($"hole {0x7F000001u}"));
        Assert.Contains($"state {0x7F000001u} 2", o.Events);
        // o resultado (0x77) sai antes do último 0x6A: é ele que abre a tela de resultado do torneio
        Assert.True(o.Events.IndexOf("over") < o.Events.IndexOf($"state {0x7F000001u} 2"));
        Assert.True(o.Events.IndexOf("state 100 2") < o.Events.IndexOf("over"));
    }

    [Fact]
    public void LastHumanToFinishGetsResultBeforeClosingState()
    {
        var (_, g, o, _) = Setup(GameMode.Tournament, 1, false, 1);
        var a = g.Players[0];
        g.Loaded(a);
        g.Shoot(a, 0);
        g.Result(a, new ShotResult(a.Guid, 0, 0, 320, ShotResult.StateHoled, 9, 1));
        g.ShotFinished(a);
        Assert.True(g.Over);
        Assert.Single(o.Events.FindAll(e => e == "state 100 2"));
        Assert.True(o.Events.IndexOf("over") < o.Events.IndexOf("state 100 2"));
    }

    [Fact]
    public void ApproachRanksByDistanceAndEndsWithTotals()
    {
        var (_, g, o, _) = Setup(GameMode.NewApproach, 2, false, 1);
        var (a, b) = (g.Players[0], g.Players[1]);
        g.Loaded(a); g.Loaded(b);
        g.TeeShotReady(a);
        Assert.DoesNotContain("tee all", o.Events);
        g.TeeShotReady(b);
        Assert.Contains("tee all", o.Events);
        g.Shoot(a, 15000);
        g.Result(a, new ShotResult(a.Guid, 0, 0, 310, 5, 0, 0));    // 10 unidades = 31,2 décimos de jarda
        g.ShotFinished(a);
        g.Shoot(b, 12000);
        g.Result(b, new ShotResult(b.Guid, 0, 0, 300, ShotResult.StateWaterOrOut, 0, 0));   // água: "Out"
        g.ShotFinished(b);
        Assert.Equal("aend", o.Events[^2]);
        var ea = o.LastHole!.Find(e => e.Guid == 100);
        var eb = o.LastHole.Find(e => e.Guid == 101);
        Assert.Equal((1, 31u, 3u), (ea.Rank, ea.Dist, ea.Prize));
        Assert.Equal((255, MassGame.Out), (eb.Rank, eb.Dist));
        Assert.Equal(1, o.Totals!.Find(e => e.Guid == 100).Rank);
        Assert.True(g.Over);
    }
}

public class FieldItemTests
{
    [Fact]
    public void OnlyWizCityHasItemsAndEachIsTakenOnce()
    {
        Assert.Null(FieldItems.For(5, new Random(1)));
        var f = FieldItems.For(FieldItems.WizCity, new Random(1))!;
        Assert.Equal(60, f.PerHole[3].Length);
        Assert.Equal(5, f.PerHole[3].Count(t => t == FieldItems.Box));
        Assert.Equal(20, f.PerHole[1].Length);
        var rng = new Random(2);
        var coin = f.Take(100, 1, FieldItems.Coin, 0, 1, rng);
        Assert.InRange(coin!.Value.Pang, 1, 50);                  // borda do green: até 50
        Assert.Null(f.Take(100, 1, FieldItems.Coin, 0, 1, rng));  // a mesma moeda de novo
        Assert.NotNull(f.Take(101, 1, FieldItems.Coin, 0, 2, rng)); // outro jogador pode pegar a dele
        Assert.Null(f.Take(100, 1, FieldItems.Box, 1, 1, rng));   // tipo errado para o índice
        Assert.Null(f.Take(100, 1, FieldItems.Coin, 999, 1, rng)); // índice inexistente
        var box = f.Take(100, 3, FieldItems.Box, 0, 2, rng);
        Assert.Equal(FieldItems.BoxPrize, box!.Value.ItemTypeId);                 // caixa = 1 Spin Cube
    }
}
