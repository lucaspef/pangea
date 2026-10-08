using System.Buffers.Binary;
using Pangya.Domain.Rooms;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Tests;

public class BotHoleMemoryTests
{
    const float Y = ShotModel.UnitsPerYard;
    static BotGolfer Perfect() => new(new Random(1), accuracy: 1);

    /// <summary>Ponto onde o plano manda a bola (sem vento): mira × distância do modelo.</summary>
    static (float X, float Z) Target(BotShot s, float x, float z)
    {
        float d = ShotModel.Distance(ShotModel.RangeYards(s.Club), s.Bar);
        var (ux, uz) = ShotModel.Direction(s.Aim);
        return (x + ux * d, z + uz * d);
    }

    static float Dist((float X, float Z) a, (float X, float Z) b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void AfterWaterTheSameShotIsNotRepeated()
    {
        var g = Perfect();
        var first = g.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0);
        var t1 = Target(first, 0, 0);
        g.Observe(0, 0, 0, t1.X, t1.Z, 0, 0, ShotResult.StateWaterOrOut, putt: false);   // água: a bola volta ao início
        Assert.Single(g.Hazards);
        var second = g.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0);
        Assert.True(Dist(Target(second, 0, 0), t1) >= BotGolfer.HazardYards * Y);  // cai longe da água
        g.Observe(0, 0, 0, Target(second, 0, 0).X, Target(second, 0, 0).Z, 0, 0, ShotResult.StateWaterOrOut, putt: false);
        var third = g.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0);
        Assert.True(Dist(Target(third, 0, 0), t1) >= BotGolfer.HazardYards * Y);
        Assert.True(Dist(Target(third, 0, 0), Target(second, 0, 0)) >= BotGolfer.HazardYards * Y);
    }

    [Fact]
    public void FollowsTheSafeSpotOfAnotherPlayerAroundADogleg()
    {
        var g = Perfect();
        var direct = Target(g.Plan(0, 0, 0, 300 * Y, 0, 0, hole: 2), 0, 0);
        g.Observe(2, 0, 0, direct.X, direct.Z, 0, 0, ShotResult.StateWaterOrOut, putt: false);
        g.Observe(2, 0, 0, -80 * Y, 150 * Y, -80 * Y, 150 * Y, 2, putt: false);        // humano: bola boa à esquerda
        var t = Target(g.Plan(0, 0, 0, 300 * Y, 0, 0, hole: 2), 0, 0);
        Assert.True(Dist(t, (-80 * Y, 150 * Y)) < 3 * Y);
    }

    [Fact]
    public void ShortStopMeansObstacleAndNewHoleForgets()
    {
        var g = Perfect();
        g.Observe(0, 0, 0, 0, 200 * Y, 0, 30 * Y, 2, putt: false);                    // parou a 15%: árvore no caminho
        Assert.Single(g.Hazards);
        g.Observe(0, 0, 0, 0, 5 * Y, 0, 4 * Y, 2, putt: true);                        // putt não conta
        Assert.Single(g.Hazards);
        g.Plan(0, 0, 0, 100 * Y, 0, 0, hole: 1);
        Assert.Empty(g.Hazards);
        Assert.Empty(g.SafeSpots);
    }
}

public class BotPowerShotTests
{
    const float Y = ShotModel.UnitsPerYard;

    [Fact]
    public void GaugeFollowsTheClientRules()
    {
        var g = new BotGolfer(new Random(1), 1, maxPowerShot: 2);
        for (int i = 0; i < 3; i++) g.ShotDone(new BotShot(0, 1, 0));
        Assert.Equal(36, g.Gauge);                                          // 3 × PangYa (+12)
        g.ShotDone(new BotShot(0, 1, 0, PowerShot: 1));
        Assert.Equal(3, g.Gauge);                                           // −33
        g.ShotDone(default, timeOut: true);
        Assert.Equal(0, g.Gauge);                                           // −30, não fica negativo
        for (int i = 0; i < 20; i++) g.ShotDone(new BotShot(0, 1, 0));
        Assert.Equal(BotGolfer.GaugeMax, g.Gauge);
    }

    [Fact]
    public void UsesTheSmallestPowerShotThatReachesAndOnlyWithGauge()
    {
        var g = new BotGolfer(new Random(1), 1, maxPowerShot: 2);
        Assert.Equal(0, g.Plan(0, 0, 0, 238 * Y, 0, 0).PowerShot);          // sem gauge: sem power shot
        for (int i = 0; i < 6; i++) g.ShotDone(new BotShot(0, 1, 0));       // gauge 72
        var s1 = g.Plan(0, 0, 0, 238 * Y, 0, 0);
        Assert.Equal((ShotModel.Driver, (byte)1), (s1.Club, s1.PowerShot));  // 230 não chega; 240 (simples) chega
        Assert.Equal(2, g.Plan(0, 0, 0, 248 * Y, 0, 0).PowerShot);           // precisa do duplo (250)
        Assert.Equal(0, g.Plan(0, 0, 0, 200 * Y, 0, 0).PowerShot);           // o driver alcança: não gasta gauge
        Assert.Equal(240f, ShotModel.RangeYards(ShotModel.Driver, powerShot: 1));
        Assert.Equal(180f, ShotModel.RangeYards(4, powerShot: 1));             // ferros também ganham (3I: 170 + 10)
        var easy = BotGolfer.For(BotLevel.Easy, new Random(1));
        for (int i = 0; i < 9; i++) easy.ShotDone(new BotShot(0, 1, 0));
        Assert.Equal(0, easy.Plan(0, 0, 0, 238 * Y, 0, 0).PowerShot);        // easy nunca usa
        var normal = BotGolfer.For(BotLevel.Normal, new Random(1));
        for (int i = 0; i < 9; i++) normal.ShotDone(new BotShot(0, 1, 0));
        Assert.Equal(1, normal.Plan(0, 0, 0, 248 * Y, 0, 0).PowerShot);      // normal: só o simples
    }

    static BotGolfer Charged(BotLevel level)
    {
        var g = BotGolfer.For(level, new Random(1));
        for (int i = 0; i < 9; i++) g.ShotDone(new BotShot(0, 1, 0));       // gauge 99
        return g;
    }

    [Fact]
    public void TomahawkOnlyWhenTheBiggestPowerShotDoesNotReach()
    {
        var hard = Charged(BotLevel.Hard);
        Assert.Equal(Special.None, hard.Plan(0, 0, 0, 248 * Y, 0, 0).Special);   // o duplo alcança
        var s = hard.Plan(0, 0, 0, 290 * Y, 0, 0);
        Assert.Equal((ShotModel.Driver, (byte)2, Special.Tomahawk), (s.Club, s.PowerShot, s.Special));
        // alcance estimado 250 × 1,25 = 312,5 jd: 290 jd pede ~93 % da barra (precisão 0,93 → erro pequeno)
        Assert.InRange(s.Power, 0.85f, 1f);
        Assert.Equal(Special.None, Charged(BotLevel.Normal).Plan(0, 0, 0, 290 * Y, 0, 0).Special);
        Assert.Equal(Special.None, hard.Plan(0, 0, 0, 290 * Y, 0, 0, cautious: true).Special);   // depois de água: 2 tacos a menos
    }

    [Fact]
    public void ImpactErrorFollowsTheLevelAndTheAccuracyStat()
    {
        var impossible = BotGolfer.For(BotLevel.Impossible, new Random(1));
        for (int i = 0; i < 50; i++) Assert.Equal((4, 0f), impossible.DrawImpact());   // sempre PangYa

        var easy = BotGolfer.For(BotLevel.Easy, new Random(1));
        easy.AccuracyStat = 10;                                              // N = 10, W = 20 (raio 25)
        var phases = new int[5];
        for (int i = 0; i < 2000; i++)
        {
            var (ph, x) = easy.DrawImpact();
            float ax = MathF.Abs(x);
            Assert.Equal(ax <= 2 ? 4 : ax <= 10 ? 3 : ax <= 20 ? 2 : 1, ph);   // como o cliente: |d| < área + 0,5
            phases[ph]++;
        }
        Assert.True(phases[1] > 0 && phases[2] > 0 && phases[3] > 0 && phases[4] > 0);
        Assert.True(phases[4] < phases[3]);                                  // PangYa é a minoria no easy

        var s = easy.Plan(0, 0, 0, 150 * Y, 0, 0);                           // tacada comum: leva a fase sorteada
        var block = InGameOutput.BotBlock(null, s);
        Assert.Equal(s.Phase, block[0x10]);
        Assert.Equal(ShotModel.BarStart + s.Impact, BitConverter.ToSingle(block, 0x04));
        var putt = easy.Plan(0, 0, 0, 5 * Y, 0, 0);
        Assert.Equal((4, 0f), (putt.Phase, putt.Impact));                    // putt sai limpo

        var g = new BotGolfer(new Random(1), 1, maxPowerShot: 2);
        g.ShotDone(new BotShot(0, 1, 0, Phase: 3));
        Assert.Equal(BotGolfer.GaugePerGood, g.Gauge);                       // boa: +4
        g.ShotDone(new BotShot(0, 1, 0, Phase: 2));
        Assert.Equal(BotGolfer.GaugePerGood, g.Gauge);                       // normal: nada
        g.ShotDone(new BotShot(0, 1, 0));
        Assert.Equal(BotGolfer.GaugePerGood + BotGolfer.GaugePerPangya, g.Gauge);
    }

    [Fact]
    public void PowerShotItemsReachWhenTheGaugeIsEmpty()
    {
        var g = new BotGolfer(new Random(1), 1, maxPowerShot: 2, specials: Special.Tomahawk);
        float driver = ShotModel.RangeYards(ShotModel.Driver);
        var none = g.Plan(0, 0, 0, (driver + 8) * Y, 0, 0);
        Assert.Equal((0, 0), ((int)none.PowerShot, none.Item));             // sem gauge e sem item: sem PS

        g.Items.AddRange([BotItem.PowerAssist, BotItem.PowerEnhancer]);
        var pa = g.Plan(0, 0, 0, (driver + 8) * Y, 0, 0);
        Assert.Equal((1, BotItem.PowerAssist), ((int)pa.PowerShot, pa.Item));   // +10 resolve
        var pe = g.Plan(0, 0, 0, (driver + 14) * Y, 0, 0);
        Assert.Equal((3, BotItem.PowerEnhancer), ((int)pe.PowerShot, pe.Item)); // +15
        var far = g.Plan(0, 0, 0, (driver + 40) * Y, 0, 0);
        Assert.Equal((3, BotItem.PowerEnhancer, Special.Tomahawk), ((int)far.PowerShot, far.Item, far.Special));

        g.UseItem(BotItem.PowerEnhancer);
        g.ShotDone(pe);
        Assert.Equal(0, g.Gauge);                                            // PS por item não mexe no gauge
        Assert.Equal([BotItem.PowerAssist], g.Items);
    }

    [Fact]
    public void SilentWindOnLongShotsWithStrongWind()
    {
        var g = new BotGolfer(new Random(1), 1);
        g.Items.Add(BotItem.SilentWind);
        var calm = g.Plan(0, 0, 0, 200 * Y, 2, 64);                          // vento 3 m: não gasta
        Assert.Equal(0, calm.Item);
        var windy = g.Plan(0, 0, 0, 200 * Y, 8, 64);                         // 9 m de lado
        Assert.Equal(BotItem.SilentWind, windy.Item);
        var expected = g.Plan(0, 0, 0, 200 * Y, 0, 64);                      // planejada com 1 m
        Assert.Equal(expected.Aim, windy.Aim, 4);
        Assert.Equal(0, g.Plan(0, 0, 0, 100 * Y, 8, 64).Item);               // curta: não gasta
        Assert.Equal(0, g.Plan(0, 0, 0, 200 * Y, 8, 0).Item);                // a favor (direção 0 = +Z): ajuda, não corta
        Assert.Equal(BotItem.SilentWind, g.Plan(0, 0, 0, 200 * Y, 8, 128).Item);   // contra: corta
    }

    [Fact]
    public void CobraGoesUnderAnObstacleThatBlockedTheLine()
    {
        var vh = Charged(BotLevel.VeryHard);
        Assert.Equal(Special.None, vh.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0).Special);
        vh.Observe(0, 0, 0, 0, 200 * Y, 0, 50 * Y, 2, putt: false);            // barrada a 50 de 200 jardas: obstáculo
        var s = vh.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0);
        Assert.Equal((ShotModel.Driver, (byte)1, Special.Cobra), (s.Club, s.PowerShot, s.Special));
        Assert.Equal(0f, s.Aim, 2);                                          // mesma linha
        vh.Observe(0, 0, 0, 0, 200 * Y, 0, 60 * Y, 2, putt: false, cobra: true);   // o Cobra também bateu: desiste
        Assert.Equal(Special.None, vh.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0).Special);

        var water = Charged(BotLevel.VeryHard);
        water.Observe(0, 0, 0, 0, 200 * Y, 0, 0, ShotResult.StateWaterOrOut, putt: false);
        Assert.Equal(Special.None, water.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0).Special);   // água: Cobra não resolve
        var hard = Charged(BotLevel.Hard);
        hard.Observe(0, 0, 0, 0, 200 * Y, 0, 50 * Y, 2, putt: false);
        Assert.Equal(Special.None, hard.Plan(0, 0, 0, 200 * Y, 0, 0, hole: 0).Special);    // hard não tem Cobra
        Assert.Equal(Special.Cobra, InGameOutput.BotBlock(null, new BotShot(0, 1, 0, 1, Special.Cobra))[0x11]);
    }

    [Fact]
    public void SpecialRangeIsLearnedAndTheLongerOneIsUsed()
    {
        var vh = Charged(BotLevel.VeryHard);
        Assert.Equal(Special.Tomahawk, vh.Plan(0, 0, 0, 300 * Y, 0, 0).Special);   // fatores iguais: Tomahawk
        var c = vh.Calibration;
        // Spike foi 50 % além da tacada normal com PS duplo (250 jd → 375 jd na barra cheia)
        for (int i = 0; i < 10; i++)
            Assert.True(c.ObserveSpecial(Special.Spike, 0, ShotModel.BarOf(1), 0, 0, 0, 375 * Y, 0, 0, 2, 0, 0, 2));
        Assert.Equal(1.5f, c.SpecialFactor(Special.Spike), 1);
        Assert.Equal(Special.InitialFactor, c.SpecialFactor(Special.Tomahawk));
        Assert.Equal(1f, c.DistanceFactor);                                          // a calibração normal não muda
        Assert.Equal(Special.Spike, vh.Plan(0, 0, 0, 300 * Y, 0, 0).Special);
        Assert.False(c.ObserveSpecial(Special.Spike, 0, ShotModel.BarOf(1), 0, 0, 0, 375 * Y, 0, 0, ShotResult.StateWaterOrOut, 0, 0, 2));
        Assert.False(c.ObserveSpecial(Special.None, 0, ShotModel.BarOf(1), 0, 0, 0, 375 * Y, 0, 0, 2, 0, 0, 2));
    }
}

public class BotLevelTests
{
    const float Y = ShotModel.UnitsPerYard;

    [Theory]
    [InlineData("easy", BotLevel.Easy)] [InlineData("Facil", BotLevel.Easy)] [InlineData("difícil", BotLevel.Hard)]
    [InlineData("very hard", BotLevel.VeryHard)] [InlineData("muitodificil", BotLevel.VeryHard)] [InlineData("impossivel", BotLevel.Impossible)]
    [InlineData("normal", BotLevel.Normal)]
    public void ParsesChatNames(string text, BotLevel level) => Assert.Equal(level, BotGolfer.ParseLevel(text));

    [Fact]
    public void LevelsChangeAccuracyWindAndMemory()
    {
        Assert.Null(BotGolfer.ParseLevel("off"));
        var easy = BotGolfer.For(BotLevel.Easy, new Random(1));
        Assert.False(easy.ReadsWind);
        easy.Observe(0, 0, 0, 0, 200 * Y, 0, 0, ShotResult.StateWaterOrOut, putt: false);
        Assert.Empty(easy.Hazards);                                         // não lembra da água
        Assert.Equal(1f, BotGolfer.For(BotLevel.Impossible, new Random(1)).Accuracy);
        Assert.Equal(0.7f, BotGolfer.For(BotLevel.Normal, new Random(1), 0.7f).Accuracy);
        Assert.True(BotGolfer.For(BotLevel.Hard, new Random(1)).Accuracy > BotGolfer.For(BotLevel.Normal, new Random(1)).Accuracy);
        // easy ignora o vento: com vento forte mira igual a sem vento (a menos do erro aleatório, zerado aqui)
        var e = new BotGolfer(new Random(1), 1, readsWind: false);
        Assert.Equal(e.Plan(0, 0, 0, 200 * Y, 0, 0).Aim, e.Plan(0, 0, 0, 200 * Y, 8, 64).Aim, 4);
    }
}

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
    public void SpecialFlagGoesInTheBlockOnlyWithPowerShot()
    {
        Assert.Equal(Special.Tomahawk, InGameOutput.BotBlock(Real, new BotShot(0, 1, 0, 1, Special.Tomahawk))[0x11]);
        Assert.Equal(Special.Spike, InGameOutput.BotBlock(Real, new BotShot(0, 1, 0, 2, Special.Spike))[0x11]);
        Assert.Equal(0, InGameOutput.BotBlock(Real, new BotShot(0, 1, 0, 0, Special.Tomahawk))[0x11]);   // sem PS: nada
        Assert.Equal(0, InGameOutput.BotBlock(Real, new BotShot(4, 1, 0, 1, Special.Spike))[0x11]);      // Spike só madeira
        Assert.Equal(4, InGameOutput.BotBlock(Real, new BotShot(0, 1, 0, 1, Special.Tomahawk))[0x10]);   // fase 4
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

[Collection("db")]
public class BotLevelChatTests(DbFixture fx)
{
    [Fact]
    public async Task ChatCommandSetsLevelAndAddsTheBot()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (acc, key) = await env.NewPlayerAsync();
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new Pangya.Core.Net.PacketWriter(0x08).U8(0).U32(60000).U32(0).U8(4).U8(0).U8(3).U8(0).U8(0).Str("t").Str(""));
        await c.ExpectAsync(0x46);
        await c.SendAsync(new Pangya.Core.Net.PacketWriter(0x03).Str("x").Str("!bot dificil"));
        Assert.Equal("Bot: Hard", (await c.ExpectAsync(0x3F)).Str());
        var slot = await c.ExpectAsync(0x46);
        Assert.Equal(1, slot.U8());                                          // bot entrou
        var room = env.Game.World.Rooms.Rooms.First();
        Assert.Equal(BotLevel.Hard, room.BotLevel);
        await c.SendAsync(new Pangya.Core.Net.PacketWriter(0x03).Str("x").Str("!bot impossivel"));
        Assert.Equal("Bot: Impossible", (await c.ExpectAsync(0x3F)).Str());   // muda o nível sem outro bot
        Assert.Equal(BotLevel.Impossible, room.BotLevel);
        Assert.NotNull(room.Bot);
    }
}

/// <summary>Kit do bot por nível e stats calculados como o cliente (SPEC-bot-especiais.md §4).</summary>
[Collection("db")]
public class BotKitTests(DbFixture fx)
{
    [Fact]
    public async Task StatsMatchTheClientFormula()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var bot = await env.Players.CreateBotAsync();
        Assert.Equal([0, 17, 10, 5, 5], env.Data.PlayStats(bot).Stats);    // Hana + Air Knight, nível 1
        env.Players.EquipBot(bot, BotLevel.VeryHard);
        var (stats, driveUp) = env.Data.PlayStats(bot);
        Assert.Equal(70, bot.Level);
        Assert.Equal(23, stats[0]);                                          // alvo do very hard
        Assert.Equal(4, driveUp);                                            // Midnight Ring
        Assert.Equal(BotKit.RubyAirKnight, bot.Find(bot.Equip.ClubSetId)!.TypeId);
        Assert.Equal(BotKit.Pippin, bot.Find(bot.Equip.CaddieId)!.TypeId);
        env.Players.EquipBot(bot, BotLevel.Easy);                            // kit básico, sem caddie
        Assert.Equal(0, bot.Equip.CaddieId);
        // alcance do driver de cada nível, calculado com a fórmula do cliente
        foreach (var level in Enum.GetValues<BotLevel>())
        {
            env.Players.EquipBot(bot, level);
            var (s, up) = env.Data.PlayStats(bot);
            Assert.Equal(BotKit.DriverYards[(int)level], ShotModel.RangeYards(ShotModel.Driver, s[0], driveUp: up));
            Assert.Equal(30, s[1]);                                          // controle sempre no máximo
            Assert.Equal(new[] { 7, 9, 11, 15, 30 }[(int)level], s[3]);      // spin por nível
        }
        Assert.Equal(284f, ShotModel.RangeYards(ShotModel.Driver, 25, driveUp: 4));   // 1W: 230 + 2×25 + 4
        Assert.Equal(184f, ShotModel.RangeYards(3, 25, driveUp: 4));                  // 2I: ferro não usa a força
        Assert.Equal(20f, ShotModel.RangeYards(ShotModel.Putter1, 25, driveUp: 4));   // putter: nada
    }
}
