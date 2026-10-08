using Pangya.Core.Config;
using Pangya.Domain.Game;

namespace Pangya.Tests;

/// <summary>Treasure Hunter (SPEC-treasure-hunter.md): pontos por buraco, caixas pelos pontos e sorteio.</summary>
public class TreasureHunterTests
{
    [Fact]
    public void HolePointsFollowTheTable()
    {
        Assert.Equal(100, TreasureHunter.HolePoints(1, 4));                 // HIO
        Assert.Equal(100, TreasureHunter.HolePoints(2, 5));                 // albatroz
        Assert.Equal(50, TreasureHunter.HolePoints(2, 4));
        Assert.Equal(30, TreasureHunter.HolePoints(3, 4));
        Assert.Equal(15, TreasureHunter.HolePoints(4, 4));
        Assert.Equal(10, TreasureHunter.HolePoints(5, 4));
        Assert.Equal(7, TreasureHunter.HolePoints(6, 4));
        Assert.Equal(4, TreasureHunter.HolePoints(7, 4));
        Assert.Equal(1, TreasureHunter.HolePoints(8, 4));
        Assert.Equal(0, TreasureHunter.HolePoints(9, 4));
        Assert.Equal(0, TreasureHunter.HolePoints(0, 4));                   // não jogou
    }

    [Fact]
    public void BoxesGrowWithPoints()
    {
        var rng = new Random(1);
        Assert.Equal(0, TreasureHunter.BoxCount(0, 100, rng));
        for (int i = 0; i < 50; i++)
        {
            Assert.InRange(TreasureHunter.BoxCount(60, 100, rng), 1, 2);
            Assert.InRange(TreasureHunter.BoxCount(450, 100, rng), 4, 8);
            Assert.InRange(TreasureHunter.BoxCount(1000, 100, rng), 12, 24);
            Assert.InRange(TreasureHunter.BoxCount(5000, 300, rng), 1, TreasureHunter.MaxBoxes);
            Assert.Equal(1, TreasureHunter.BoxCount(60, 1, rng));            // taxa baixa: pelo menos 1
        }
    }

    [Fact]
    public void DrawUsesOnlyAllowedPrizesAndTheirRanges()
    {
        var prizes = new[]
        {
            new TreasurePrize { TypeId = TreasureHunter.PangTid, Min = 50, Max = 60, Weight = 1 },
            new TreasurePrize { TypeId = 0x18000004, Min = 1, Max = 2, Weight = 1 },
            new TreasurePrize { TypeId = 0x08000800, Min = 1, Max = 1, Weight = 100 },   // peça: o cliente não soma
        };
        var got = TreasureHunter.Draw(200, prizes, tid => ((uint)tid >> 26) == 6, new Random(2));
        Assert.Equal(200, got.Count);
        bool pang = false, ps = false;
        foreach (var (tid, n) in got)
        {
            Assert.NotEqual(0x08000800, tid);
            if (tid == TreasureHunter.PangTid) { pang = true; Assert.InRange(n, 50, 60); }
            else { ps = true; Assert.InRange(n, 1, 2); }
        }
        Assert.True(pang && ps);
        Assert.Empty(TreasureHunter.Draw(5, [], _ => true, new Random(3)));
    }
}
