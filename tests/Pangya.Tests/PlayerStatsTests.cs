using Pangya.Core.Config;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

public class PlayerStatsRuleTests
{
    static GameStats G(long holeIn = 1, long hio = 0, long alba = 0, long putt = 5, float longest = 250, float chip = 12) =>
        new(Drive: 3, Putt: putt, ShotTime: 100, Longest: longest, Pangya: 2, TimeOut: 0, OB: 1, Distance: 700, Hole: 3,
            HoleInOne: hio, Bunker: 1, Fairway: 2, Albatross: alba, HoleIn: holeIn, PuttIn: 2, LongestPuttIn: 8, LongestChipIn: chip);

    [Fact]
    public void AccumulatesCountersAndKeepsLongest()
    {
        var t = PlayerStats.After(null, G(), 3, -1);
        t = PlayerStats.After(t, G(holeIn: 0, hio: 1, alba: 1, longest: 200, chip: 30), 3, 2);
        Assert.Equal((2L, 1L, 1L, 1L, 10L, 4L), (t.Games, t.HoleIn, t.HoleInOne, t.Albatross, t.Putt, t.PuttIn));
        Assert.Equal((250f, 30f, 1L), (t.Longest, t.LongestChipIn, t.TotalScore));
    }

    [Fact]
    public void ClientNumbersAreCappedByHolesPlayed()
    {
        var t = PlayerStats.After(null, G(holeIn: 99, hio: 50, alba: 9, putt: 1_000_000, longest: float.NaN), 3, null);
        Assert.Equal((3L, 3L, 3L, 60L, 0f), (t.HoleIn, t.HoleInOne, t.Albatross, t.Putt, t.Longest));
        var none = PlayerStats.After(null, null, 3, null);                 // sem 0x31/0x06: só conta a partida
        Assert.Equal((1L, 0L), (none.Games, none.Putt));
    }
}

[Collection("db")]
public class PlayerStatsTests(DbFixture fx)
{
    [Fact]
    public async Task TotalsAreSavedAndShownInLoginAndProfile()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var g = new GameStats(3, 5, 100, 250.5f, 2, 0, 1, 700, 3, 1, 1, 2, 0, 1, 2, 8.25f, 12.5f);
        await Rewards.ApplyAsync(env.Players.Store, p, 100, 0, 3, true, new RewardConfig(), (4, -2), stats: g);
        await Rewards.ApplyAsync(env.Players.Store, p, 100, 0, 3, false, new RewardConfig(), (4, 0), stats: g);   // saiu: não conta
        var saved = (await env.Players.LoadAsync(acc.Id))!;
        Assert.Equal((1L, 1L, 1L, 5L, -2L), (saved.Stats.Games, saved.Stats.HoleInOne, saved.Stats.HoleIn, saved.Stats.Putt, saved.Stats.TotalScore));
        Assert.Single(saved.Courses);                                       // o registro do curso continua gravado junto

        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        var login = await c.ExpectAsync(0x42);
        login.U8(); login.Str(); login.Str();
        var st = login.Struct<sUserInfo>().stat;
        Assert.Equal((1u, (ushort)1, 1u, 5u, 250.5f, 12.5f), (st.dwGameCount, st.wHoleInOne, st.dwHoleIn, st.dwPutt, st.fLongest, st.fLongestChipIn));
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x2F).U32((uint)acc.Id).U8(5));
        var prof = await c.ExpectAsync(0x150);
        prof.U8(); prof.U32();
        var ps = prof.Struct<sPangYaUserStatistics>();
        Assert.Equal((1u, (ushort)1, 2u), (ps.dwHoleIn, ps.wHoleInOne, ps.dwPuttIn));
    }
}
