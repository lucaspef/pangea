using Pangya.Data;
using Pangya.Domain.Rooms;

namespace Pangya.Tests;

/// <summary>Aprendizado do bot guardado por nível: compartilhado entre partidas, gravado no banco e carregado de volta.</summary>
public class BotKnowledgeTests
{
    const float Y = ShotModel.UnitsPerYard;

    sealed class MemoryStore : IBotKnowledgeStore
    {
        public readonly Dictionary<(BotLevel, byte, byte), string> Holes = [];
        public readonly Dictionary<BotLevel, string> Cals = [];
        public int Saves;
        public bool Fail;

        public Task<(List<BotHoleData> Holes, List<BotCalibrationData> Calibrations)> LoadAsync()
        {
            var h = new List<BotHoleData>();
            foreach (var ((l, c, n), j) in Holes) h.Add(new BotHoleData(l, c, n, j));
            var k = new List<BotCalibrationData>();
            foreach (var (l, j) in Cals) k.Add(new BotCalibrationData(l, j));
            return Task.FromResult((h, k));
        }

        public Task SaveAsync(IReadOnlyList<BotHoleData> holes, IReadOnlyList<BotCalibrationData> calibrations)
        {
            if (Fail) throw new InvalidOperationException("banco fora");
            Saves++;
            foreach (var h in holes) Holes[(h.Level, h.Course, h.Hole)] = h.Json;
            foreach (var c in calibrations) Cals[c.Level] = c.Json;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void HoleMemoryMergesNearPointsAndKeepsTheNewest()
    {
        var list = new List<(float X, float Z)>();
        Assert.True(HoleMemory.Add(list, 0, 0));
        Assert.False(HoleMemory.Add(list, 2 * Y, 0));                       // a 2 jardas: o mesmo lugar
        Assert.True(HoleMemory.Add(list, 10 * Y, 0));
        for (int i = 0; i < HoleMemory.MaxPoints + 5; i++) HoleMemory.Add(list, 0, (100 + i * 10) * Y);
        Assert.Equal(HoleMemory.MaxPoints, list.Count);
        Assert.DoesNotContain((0f, 0f), list);                              // o mais antigo saiu
        Assert.Contains((0f, (100 + (HoleMemory.MaxPoints + 4) * 10) * Y), list);
    }

    [Fact]
    public void NextGameOfTheSameLevelRemembersTheHoleOnTheSameCourseOnly()
    {
        var k = new BotKnowledge();
        var g1 = BotGolfer.For(BotLevel.Hard, new Random(1), knowledge: k.For(BotLevel.Hard));
        g1.Course = 5;
        g1.Observe(3, 0, 0, 0, 200 * Y, 0, 0, ShotResult.StateWaterOrOut, putt: false);
        Assert.Equal(3, g1.Hazards.Count);                                  // alvo + 85 % e 70 % da linha

        var g2 = BotGolfer.For(BotLevel.Hard, new Random(2), knowledge: k.For(BotLevel.Hard));
        g2.Course = 5;
        g2.Plan(0, 0, 0, 300 * Y, 0, 0, hole: 3);
        Assert.Equal(3, g2.Hazards.Count);                                         // partida seguinte: já sabe da água

        var other = BotGolfer.For(BotLevel.Hard, new Random(3), knowledge: k.For(BotLevel.Hard));
        other.Course = 6;
        other.Plan(0, 0, 0, 300 * Y, 0, 0, hole: 3);
        Assert.Empty(other.Hazards);                                        // outro mapa

        var normal = BotGolfer.For(BotLevel.Normal, new Random(4), knowledge: k.For(BotLevel.Normal));
        normal.Course = 5;
        normal.Plan(0, 0, 0, 300 * Y, 0, 0, hole: 3);
        Assert.Equal(3, normal.Hazards.Count);                                     // outro nível: o mapa é o mesmo
        Assert.Same(g1.Calibration, g2.Calibration);                        // calibração: separada por nível
        Assert.NotSame(g1.Calibration, normal.Calibration);
    }

    [Fact]
    public async Task FlushSavesOnlyChangesAndLoadRestoresThem()
    {
        var store = new MemoryStore();
        var sync = new object();
        var k = new BotKnowledge(store);
        var g = BotGolfer.For(BotLevel.VeryHard, new Random(1), knowledge: k.For(BotLevel.VeryHard));
        g.Course = 2;
        Assert.Equal(0, await k.FlushAsync(sync));                          // nada mudou
        g.Observe(7, 0, 0, 0, 200 * Y, 0, 50 * Y, 2, putt: false);           // barrada: perigo + obstáculo
        g.Observe(7, 0, 0, 50 * Y, 150 * Y, 50 * Y, 150 * Y, 2, putt: false); // bola boa
        // tacada limpa de ferro 5 que foi 10% mais longe: muda a calibração
        float d = ShotModel.Distance(ShotModel.RangeYards(5), 400);
        Assert.True(g.Calibration.Observe(5, 400, 0, 0, 0, 0, d * 1.1f, 0, 0, 2, learnDistance: true));
        float ps = ShotModel.Distance(ShotModel.RangeYards(ShotModel.Driver, 0, driveUp: 0, powerShot: 1), 400) * g.Calibration.DistanceFactor;
        Assert.True(g.Calibration.ObserveSpecial(Special.Tomahawk, ShotModel.Driver, 400, 0, 0, 0, ps * 1.4f, 0, 0, 2, 0, 0, 1));
        Assert.Equal(5 + 1, await k.FlushAsync(sync));                      // o buraco nos 5 níveis + 1 calibração
        Assert.Equal(0, await k.FlushAsync(sync));
        g.Observe(7, 0, 0, 50 * Y, 151 * Y, 50 * Y, 151 * Y, 2, putt: false); // mesmo lugar: nada novo
        Assert.Equal(0, await k.FlushAsync(sync));

        store.Fail = true;                                                  // banco fora: tenta de novo depois
        g.Observe(8, 0, 0, 0, 200 * Y, 0, 0, ShotResult.StateWaterOrOut, putt: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => k.FlushAsync(sync));
        store.Fail = false;
        Assert.Equal(5, await k.FlushAsync(sync));

        var loaded = new BotKnowledge(store);
        Assert.Equal((10, 1), await loaded.LoadAsync());
        var g2 = BotGolfer.For(BotLevel.VeryHard, new Random(2), knowledge: loaded.For(BotLevel.VeryHard));
        g2.Course = 2;
        g2.Plan(0, 0, 0, 300 * Y, 0, 0, hole: 7);
        Assert.Single(g2.Hazards);
        Assert.Single(g2.SafeSpots);
        Assert.Equal(g.Calibration.DistanceFactor, g2.Calibration.DistanceFactor, 3);
        Assert.Equal(g.Calibration.AimOffset, g2.Calibration.AimOffset, 4);
        Assert.Equal(g.Calibration.SpecialFactor(Special.Tomahawk), g2.Calibration.SpecialFactor(Special.Tomahawk), 3);
        Assert.Equal(g.Calibration.Samples, g2.Calibration.Samples);
        Assert.Equal(0, await loaded.FlushAsync(sync));                     // carregar não conta como mudança
    }

    [Fact]
    public void ClubThatFallsShortIsReplacedByALongerOne()
    {
        var g = new BotGolfer(new Random(1), accuracy: 1);
        float yards = ShotModel.RangeYards(5) * 0.95f;                      // o ferro 5 alcança, com folga pequena
        Assert.Equal(5, g.ClubFor(yards));
        for (int i = 0; i < 6; i++)                                         // o ferro 5 vem caindo 15% curto
        {
            float d = ShotModel.Distance(ShotModel.RangeYards(5), 500);
            Assert.True(g.Calibration.Observe(5, 500, 0, 0, 0, 0, d * 0.85f, 0, 0, 2, learnDistance: true));
        }
        Assert.InRange(g.Calibration.ClubFactor(5), 0.84f, 0.87f);
        Assert.True(g.ClubFor(yards) < 5);                                  // taco maior
        Assert.Equal(g.Calibration.DistanceFactor, g.Calibration.ClubFactor(7));   // os outros: o fator geral

        var back = ShotCalibration.FromJson(g.Calibration.ToJson());        // gravado com a calibração
        Assert.Equal(g.Calibration.ClubFactor(5), back.ClubFactor(5), 4);
    }

}

/// <summary>Repositório do aprendizado do bot no banco de teste (coleção "db": o esquema é recriado nela).</summary>
[Collection("db")]
public class BotKnowledgeRepositoryTests(DbFixture fx)
{
    [Fact]
    public async Task RepositoryRoundTripsThroughTheDatabase()
    {
        const byte Course = 250;                                            // mapa que não existe: não colide com partidas de teste
        var repo = new BotKnowledgeRepository(fx.Db);
        var mem = new HoleMemory();
        HoleMemory.Add(mem.Hazards, 1.5f, -2.25f);
        HoleMemory.Add(mem.CobraBlocked, 30, 40);
        await repo.SaveAsync([new BotHoleData(BotLevel.Impossible, Course, 18, mem.ToJson().ToJsonString())], []);
        mem.Safe.Add((9, 9));
        await repo.SaveAsync([new BotHoleData(BotLevel.Impossible, Course, 18, mem.ToJson().ToJsonString())], []);   // atualiza
        var (holes, _) = await repo.LoadAsync();
        BotHoleData? row = null;
        foreach (var h in holes) if (h is { Level: BotLevel.Impossible, Course: Course, Hole: 18 }) row = h;
        Assert.NotNull(row);
        var back = HoleMemory.FromJson((System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(row!.Json)!);
        Assert.Equal((1.5f, -2.25f), Assert.Single(back.Hazards));
        Assert.Equal((30f, 40f), Assert.Single(back.CobraBlocked));
        Assert.Equal((9f, 9f), Assert.Single(back.Safe));
        Assert.Empty(back.Blocked);
    }
}
