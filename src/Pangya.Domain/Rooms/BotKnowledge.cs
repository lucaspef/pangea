using System.Text.Json.Nodes;

namespace Pangya.Domain.Rooms;

/// <summary>
/// Memória de um buraco (mapa + número do buraco) para o bot: lugares seguros, perigos (água/OB, tacada barrada) e
/// obstáculos (barrado / barrado até para Cobra). Pontos a menos de <see cref="MergeYards"/> de um já conhecido não
/// entram de novo; cada lista guarda no máximo <see cref="MaxPoints"/> (sai o mais antigo).
/// </summary>
public sealed class HoleMemory
{
    public const int MaxPoints = 40;
    public const float MergeYards = 3;
    public List<(float X, float Z)> Safe { get; } = [];
    public List<(float X, float Z)> Hazards { get; } = [];
    public List<(float X, float Z)> Blocked { get; } = [];
    public List<(float X, float Z)> CobraBlocked { get; } = [];

    /// <summary>Acrescenta o ponto (false = já havia um perto, nada mudou).</summary>
    public static bool Add(List<(float X, float Z)> list, float x, float z)
    {
        float r = MergeYards * ShotModel.UnitsPerYard;
        foreach (var (px, pz) in list)
            if ((px - x) * (px - x) + (pz - z) * (pz - z) < r * r) return false;
        if (list.Count >= MaxPoints) list.RemoveAt(0);
        list.Add((x, z));
        return true;
    }

    public JsonObject ToJson() => new()
    {
        ["safe"] = Points(Safe), ["hazards"] = Points(Hazards), ["blocked"] = Points(Blocked), ["cobraBlocked"] = Points(CobraBlocked),
    };

    public static HoleMemory FromJson(JsonObject o)
    {
        var m = new HoleMemory();
        Read(o["safe"], m.Safe);
        Read(o["hazards"], m.Hazards);
        Read(o["blocked"], m.Blocked);
        Read(o["cobraBlocked"], m.CobraBlocked);
        return m;
    }

    static JsonArray Points(List<(float X, float Z)> list)
    {
        var a = new JsonArray();
        foreach (var (x, z) in list) a.Add(new JsonArray(MathF.Round(x, 2), MathF.Round(z, 2)));
        return a;
    }

    static void Read(JsonNode? n, List<(float X, float Z)> list)
    {
        if (n is not JsonArray a) return;
        foreach (var p in a)
            if (p is JsonArray { Count: 2 } xy && list.Count < MaxPoints)
                list.Add((JsonNum.F(xy[0], 0), JsonNum.F(xy[1], 0)));
    }
}

/// <summary>Tipo de ponto da memória do buraco.</summary>
public enum HoleMark { Safe, Hazard, Blocked, CobraBlocked }

/// <summary>
/// O que um nível de bot aprendeu: a calibração (fator de distância, desvio de mira, alcance das especiais) e a memória
/// de cada buraco já jogado. Compartilhado por todas as salas daquele nível; mexido só com a trava do jogo (Rooms.Sync).
/// A memória dos buracos é geometria do mapa: o que um nível observa vale para todos (parent); a calibração é de cada um.
/// </summary>
public sealed class BotLevelKnowledge(BotLevel level, BotKnowledge? parent = null)
{
    public BotLevel Level { get; } = level;
    public ShotCalibration Calibration { get; private set; } = new();
    readonly Dictionary<(byte Course, byte Hole), HoleMemory> holes = [];
    readonly HashSet<(byte Course, byte Hole)> dirtyHoles = [];
    int savedCalibration;

    public int HoleCount => holes.Count;

    /// <summary>Memória do buraco (cria vazia na primeira vez).</summary>
    public HoleMemory Hole(byte course, byte hole)
    {
        if (!holes.TryGetValue((course, hole), out var m)) holes[(course, hole)] = m = new HoleMemory();
        return m;
    }

    public void MarkDirty(byte course, byte hole) => dirtyHoles.Add((course, hole));

    /// <summary>Ponto observado no buraco: entra neste nível e em todos os outros já conhecidos. true = mudou aqui.</summary>
    public bool Record(byte course, byte hole, HoleMark mark, float x, float z)
    {
        bool changed = AddLocal(course, hole, mark, x, z);
        if (parent != null)
            foreach (var other in parent.AllLevels())
                if (other != this) other.AddLocal(course, hole, mark, x, z);
        return changed;
    }

    bool AddLocal(byte course, byte hole, HoleMark mark, float x, float z)
    {
        var m = Hole(course, hole);
        var list = mark switch { HoleMark.Safe => m.Safe, HoleMark.Hazard => m.Hazards, HoleMark.Blocked => m.Blocked, _ => m.CobraBlocked };
        if (!HoleMemory.Add(list, x, z)) return false;
        MarkDirty(course, hole);
        return true;
    }

    /// <summary>A gravação falhou: a calibração volta a contar como alterada.</summary>
    internal void CalibrationNotSaved() => savedCalibration = Calibration.Version - 1;

    internal void Restore(ShotCalibration cal)
    {
        Calibration = cal;
        savedCalibration = cal.Version;
    }

    internal void Restore(byte course, byte hole, HoleMemory m) => holes[(course, hole)] = m;

    /// <summary>O que mudou desde a última gravação (JSON pronto); limpa as marcas.</summary>
    internal void TakeChanges(List<BotHoleData> outHoles, List<BotCalibrationData> outCal)
    {
        foreach (var k in dirtyHoles)
            outHoles.Add(new BotHoleData(Level, k.Course, k.Hole, holes[k].ToJson().ToJsonString()));
        dirtyHoles.Clear();
        if (Calibration.Version != savedCalibration)
        {
            outCal.Add(new BotCalibrationData(Level, Calibration.ToJson().ToJsonString()));
            savedCalibration = Calibration.Version;
        }
    }
}

/// <summary>Linha gravada: memória de um buraco de um nível (JSON).</summary>
public sealed record BotHoleData(BotLevel Level, byte Course, byte Hole, string Json);
/// <summary>Linha gravada: calibração de um nível (JSON).</summary>
public sealed record BotCalibrationData(BotLevel Level, string Json);

/// <summary>Banco do aprendizado do bot (migração 012).</summary>
public interface IBotKnowledgeStore
{
    Task<(List<BotHoleData> Holes, List<BotCalibrationData> Calibrations)> LoadAsync();
    Task SaveAsync(IReadOnlyList<BotHoleData> holes, IReadOnlyList<BotCalibrationData> calibrations);
}

/// <summary>
/// Aprendizado do bot por nível: carregado do banco quando o servidor sobe, usado em memória pelas partidas e gravado
/// de tempos em tempos (só o que mudou). Sem banco (testes) fica só em memória, durante o processo.
/// </summary>
public sealed class BotKnowledge(IBotKnowledgeStore? store = null)
{
    readonly Dictionary<BotLevel, BotLevelKnowledge> levels = [];

    public BotLevelKnowledge For(BotLevel level)
    {
        if (!levels.TryGetValue(level, out var k)) levels[level] = k = new BotLevelKnowledge(level, this);
        return k;
    }

    /// <summary>Todos os níveis (cria os que faltam: a memória dos buracos vai para todos).</summary>
    internal List<BotLevelKnowledge> AllLevels()
    {
        var all = new List<BotLevelKnowledge>();
        foreach (var l in Enum.GetValues<BotLevel>()) all.Add(For(l));
        return all;
    }

    /// <summary>Carrega tudo do banco (antes das partidas começarem). Devolve (buracos, calibrações).</summary>
    public async Task<(int Holes, int Calibrations)> LoadAsync()
    {
        if (store == null) return (0, 0);
        var (holes, cals) = await store.LoadAsync();
        foreach (var c in cals)
            if (JsonNode.Parse(c.Json) is JsonObject o) For(c.Level).Restore(ShotCalibration.FromJson(o));
        foreach (var h in holes)
            if (JsonNode.Parse(h.Json) is JsonObject o) For(h.Level).Restore(h.Course, h.Hole, HoleMemory.FromJson(o));
        return (holes.Count, cals.Count);
    }

    /// <summary>
    /// Grava o que mudou. O retrato é tirado com a trava do jogo (sync); a escrita no banco é fora dela.
    /// Devolve quantas linhas foram gravadas.
    /// </summary>
    public async Task<int> FlushAsync(object sync)
    {
        if (store == null) return 0;
        var holes = new List<BotHoleData>();
        var cals = new List<BotCalibrationData>();
        lock (sync)
            foreach (var k in levels.Values) k.TakeChanges(holes, cals);
        if (holes.Count == 0 && cals.Count == 0) return 0;
        try { await store.SaveAsync(holes, cals); }
        catch
        {
            lock (sync)                                                     // tenta de novo na próxima vez
            {
                foreach (var h in holes) For(h.Level).MarkDirty(h.Course, h.Hole);
                foreach (var c in cals) For(c.Level).CalibrationNotSaved();
            }
            throw;
        }
        return holes.Count + cals.Count;
    }
}

static class JsonNum
{
    public static float F(JsonNode? n, float def) => n is JsonValue v && v.TryGetValue(out float f) && float.IsFinite(f) ? f : def;
}
