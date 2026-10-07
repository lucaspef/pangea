using System.Buffers.Binary;
using Pangya.Domain.Rooms;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Tests;

public class BotGolferTests
{
    const float Y = ShotModel.UnitsPerYard;
    static BotGolfer Perfect() => new(new Random(1), accuracy: 1);

    /// <summary>Onde o modelo do cliente diz que a bola cai (sem efeito; vento = 3 × W fora do putt).</summary>
    static (float X, float Z) Landing(BotShot s, float x, float z, float yardsToPin, byte wind, byte dir, float distFactor = 1, float aimOffset = 0)
    {
        float d = ShotModel.Distance(ShotModel.RangeYards(s.Club, 0, yardsToPin), s.Bar) * distFactor;
        var (ux, uz) = ShotModel.Direction(s.Aim + aimOffset);
        var (wx, wz) = s.Club >= ShotModel.Putter1 ? (0f, 0f) : ShotModel.Wind(wind, dir);
        return (x + ux * d + 3 * wx, z + uz * d + 3 * wz);
    }

    [Fact]
    public void AimFollowsClientFormula()
    {
        // a = atan2(-dx, dz); direção = (-sen a, cos a): alvo em +X dá a = -π/2, alvo em -X dá +π/2
        Assert.Equal(-MathF.PI / 2, ShotModel.AimTo(100, 0), 4);
        Assert.Equal(MathF.PI / 2, ShotModel.AimTo(-100, 0), 4);
        var (x, z) = ShotModel.Direction(ShotModel.AimTo(30, 40));
        Assert.Equal(0.6f, x, 4);
        Assert.Equal(0.8f, z, 4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ShotGoesToTheSideOfThePin(int side)
    {
        var s = Perfect().Plan(0, 0, side * 150 * Y, 0, 0, 0);
        Assert.Equal(side, MathF.Sign(ShotModel.Direction(s.Aim).X));
        Assert.True(MathF.Abs(ShotModel.Direction(s.Aim).X) > 0.99f);
    }

    [Fact]
    public void BarIsLinearInDistance()
    {
        Assert.Equal(320f, ShotModel.BarOf(0.5f));
        Assert.Equal(110 * Y * 0.5f, ShotModel.Distance(110, 320), 3);
        Assert.Equal(2 * ShotModel.Distance(110, 230), ShotModel.Distance(110, 320), 3);
        // planejado: o modelo leva a bola exatamente à bandeira (100 jardas, 9I a 100/110 da barra, corrigido pelo vento)
        var s = Perfect().Plan(0, 0, 0, 100 * Y, 0, 0);
        Assert.Equal(ShotModel.Iron9, s.Club);
        var (lx, lz) = Landing(s, 0, 0, 100, 0, 0);
        Assert.Equal(0, lx, 0.5);
        Assert.Equal(100 * Y, lz, 0.5);
    }

    [Theory]
    [InlineData(300, ShotModel.Driver)]
    [InlineData(230, ShotModel.Driver)]
    [InlineData(200, 1)]        // 2W 210
    [InlineData(145, 6)]        // 5I 150
    [InlineData(100, ShotModel.Iron9)]
    [InlineData(40, ShotModel.Iron9)]    // PW/SW nunca
    [InlineData(18, ShotModel.Putter1)]
    [InlineData(3, ShotModel.Putter1)]
    public void ClubByDistance(float yards, int club)
    {
        var s = Perfect().Plan(0, 0, 0, yards * Y, 4, 10);
        Assert.Equal(club, s.Club);
    }

    [Fact]
    public void LongHoleIsPlayedInSteps()
    {
        var s = Perfect().Plan(0, 0, 0, 400 * Y, 0, 0);
        Assert.Equal(ShotModel.Driver, s.Club);
        var (lx, lz) = Landing(s, 0, 0, 400, 0, 0);
        Assert.Equal(0, lx, 0.5);
        Assert.Equal(230 * Y, lz, 0.5);              // até o alcance do driver, na linha da bandeira
    }

    [Fact]
    public void WindIsCompensatedExceptOnPutts()
    {
        var (wx, wz) = ShotModel.Wind(8, 191);       // vento de 9 para +X
        Assert.True(wx > 8.9f && MathF.Abs(wz) < 0.5f);
        var s = Perfect().Plan(0, 0, 0, 150 * Y, 8, 191);
        Assert.True(ShotModel.Direction(s.Aim).X < -0.04f, "mira contra o vento (para -X)");
        var (lx, lz) = Landing(s, 0, 0, 150, 8, 191);
        Assert.Equal(0, lx, 0.5);
        Assert.Equal(150 * Y, lz, 0.5);
        var putt = Perfect().Plan(0, 0, 0, 10 * Y, 8, 191);
        Assert.Equal(0, putt.Aim, 5);                // putt: direto na bandeira
        Assert.Equal((10 + 1) / 20f, putt.Power, 4); // 1PT 20 jardas (+1 jarda para a bola chegar)
        Assert.Equal((16 + 1) / 40f, Perfect().Plan(0, 0, 0, 16 * Y, 0, 0).Power, 4);   // 15+ jardas: putt longo (40)
    }

    [Fact]
    public void AfterWaterTheBotLaysUp()
    {
        var g = Perfect();
        Assert.Equal(ShotModel.Driver, g.Plan(0, 0, 0, 300 * Y, 0, 0).Club);
        Assert.Equal(2, g.Plan(0, 0, 0, 300 * Y, 0, 0, cautious: true).Club);
        Assert.Equal(ShotModel.Iron9, g.Plan(0, 0, 0, 100 * Y, 0, 0, cautious: true).Club);
    }

    [Fact]
    public void AccuracyAddsBoundedNoise()
    {
        var ideal = Perfect().Plan(0, 0, 0, 150 * Y, 0, 0);
        var g = new BotGolfer(new Random(5), accuracy: 0.5f);
        var aims = new List<float>();
        for (int i = 0; i < 300; i++)
        {
            var s = g.Plan(0, 0, 0, 150 * Y, 0, 0);
            Assert.InRange(s.Aim - ideal.Aim, -0.5f * 0.15f - 1e-4f, 0.5f * 0.15f + 1e-4f);
            Assert.InRange(s.Power / ideal.Power, 1 - 0.5f * 0.3f - 1e-4f, 1 + 0.5f * 0.3f + 1e-4f);
            aims.Add(s.Aim);
        }
        Assert.True(aims.Distinct().Count() > 100);
        Assert.Equal(ideal.Aim, aims.Average(), 2);            // em média, na bandeira
    }

    [Fact]
    public void CalibrationLearnsAConsistentError()
    {
        // "mundo" em que a bola anda 10% menos e sai 0,05 rad para a esquerda do que o modelo diz
        var g = Perfect();
        var rng = new Random(3);
        for (int i = 0; i < 20; i++)
        {
            float px = rng.Next(-300, 300), pz = 200 + rng.Next(300);
            byte wind = (byte)rng.Next(9), dir = (byte)rng.Next(256);
            var s = g.Plan(0, 0, px, pz, wind, dir);
            var (lx, lz) = Landing(s, 0, 0, 999, wind, dir, 0.9f, 0.05f);
            Assert.True(g.Calibration.Observe(s.Club, s.Bar, s.Aim, 0, 0, lx, lz, wind, dir, 2, learnDistance: true));
        }
        Assert.Equal(0.9f, g.Calibration.DistanceFactor, 2);
        Assert.Equal(0.05f, g.Calibration.AimOffset, 2);
        var shot = g.Plan(0, 0, 100, 400, 3, 77);
        var (x, z) = Landing(shot, 0, 0, 999, 3, 77, 0.9f, 0.05f);
        Assert.InRange(MathF.Sqrt((x - 100) * (x - 100) + (z - 400) * (z - 400)), 0, 3);   // calibrado: na bandeira
    }

    [Fact]
    public void CalibrationIgnoresPuttsHazardsAndOutliers()
    {
        var c = new ShotCalibration();
        Assert.False(c.Observe(ShotModel.Putter1, 300, 0, 0, 0, 0, 30, 0, 0, 2, true));        // putt
        Assert.False(c.Observe(5, 400, 0, 0, 0, 0, 300, 0, 0, ShotResult.StateWaterOrOut, true));
        Assert.False(c.Observe(5, 400, 0, 0, 0, 0, 300, 0, 0, ShotResult.StateHoled, true));
        Assert.False(c.Observe(5, 400, 0, 0, 0, 300, 300, 0, 0, 2, true));                    // 45° fora: obstáculo/curva
        Assert.False(c.Observe(5, 400, 0, 0, 0, 0, 50, 0, 0, 2, true));                       // parou bem antes (árvore)
        Assert.Equal(0, c.Samples);
        Assert.Equal(1f, c.DistanceFactor);
        // madeira de humano: aprende só a mira (o alcance depende do stat de força dele)
        float d = ShotModel.Distance(230, 500);
        Assert.True(c.Observe(0, 500, 0, 0, 0, 0, d * 0.8f + 3, 0, 0, 2, learnDistance: false));
        Assert.Equal(1f, c.DistanceFactor);
        Assert.Equal(1, c.Samples);
    }
}

public class BotBlockTests
{
    // tacada real do cliente (a mesma do ShotSplitTests): fase 3, curva 0,133, +0x15 = 574, +0x1D = 14057, +0x21 = 140f
    static readonly byte[] Real = Convert.FromHexString("00000080f64300000f434341c8b18988083e03000000003e020000009282bce936000000000c43000000000000000080")[2..];

    static float F(byte[] b, int off) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(off));
    static uint U(byte[] b, int off) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off));

    static void AssertStraight(byte[] b, BotShot s)
    {
        Assert.Equal(InGameOutput.ShotLength, b.Length);
        Assert.Equal(s.Bar, F(b, 0x00));
        Assert.Equal(F(b, 0x21), F(b, 0x04));         // impacto no centro
        Assert.Equal(0f, F(b, 0x08));                 // sem efeito
        Assert.Equal(0f, F(b, 0x0C));
        Assert.Equal(4, b[0x10]);                     // fase 4
        Assert.Equal(0u, U(b, 0x11));                 // sem tacada especial
        Assert.Equal(s.Aim, F(b, 0x19));
        Assert.Equal(s.Club, b[0x25]);
        Assert.Equal(0u, U(b, 0x26));
        Assert.Equal(0u, U(b, 0x2A));                 // +0x2A soma na mira: zero (nem -0)
    }

    [Fact]
    public void BlockFromHumanTemplate()
    {
        var s = new BotShot(7, 0.75f, -0.3f);
        var b = InGameOutput.BotBlock(Real, s);
        AssertStraight(b, s);
        Assert.Equal(140f, F(b, 0x21));
        Assert.Equal(574u, U(b, 0x15));
        Assert.Equal(14057u, U(b, 0x1D));
        Assert.Equal(3, Real[0x10]);                   // o modelo não foi alterado
    }

    [Fact]
    public void BlockWithoutTemplate()
    {
        var s = new BotShot(ShotModel.Putter1, 0.2f, 1.2f);
        var b = InGameOutput.BotBlock(null, s);
        AssertStraight(b, s);
        Assert.Equal(140f, F(b, 0x21));
        Assert.Equal(3000, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x1D)));
    }
}
