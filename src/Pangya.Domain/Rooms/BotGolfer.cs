namespace Pangya.Domain.Rooms;

/// <summary>
/// Modelo da tacada como o próprio cliente estima (o oponente de computador, rival.cpp; docs/protocolo/SPEC-ingame.md, "Bot").
/// Ângulo: radianos a partir do +Z do mundo, direção = (-sen a, 0, cos a) (wmath.cpp:283). Distância linear na barra:
/// unidades = alcance(jardas) × 3,2 × (barra − 140) / 360 (rival.cpp:87; 1 jarda = 3,2 unidades).
/// </summary>
public static class ShotModel
{
    public const float BarStart = 140, BarLength = 360, UnitsPerYard = 3.2f;
    public const int Driver = 0, Iron9 = 10, PitchingWedge = 11, SandWedge = 12, Putter1 = 13, Putter2 = 14;
    /// <summary>Alcance em jardas por índice de taco (+0x25): 1W 2W 3W 2I..9I PW SW 1PT 2PT.</summary>
    static readonly int[] Ranges = [230, 210, 190, 180, 170, 160, 150, 140, 130, 120, 110, 100, 80, 20, 10];
    /// <summary>Taco de madeira até este índice: alcance + 2 × stat de força.</summary>
    const int LastWood = 2;

    /// <summary>
    /// Alcance do taco em jardas. Putters: "putt longo" (+20 jardas) fora do green ou a 15+ jardas da bandeira.
    /// PW/SW têm alcances especiais perto da bandeira que não são modelados (o bot não usa esses tacos).
    /// </summary>
    public static float RangeYards(int club, int powerStat = 0, float yardsToPin = 0, bool onGreen = true)
    {
        float r = Ranges[Math.Clamp(club, 0, Ranges.Length - 1)];
        if (club <= LastWood) r += 2 * powerStat;
        if (club >= Putter1 && (!onGreen || yardsToPin >= 15)) r += 20;
        return r;
    }

    public static float BarOf(float power) => BarStart + BarLength * Math.Clamp(power, 0f, 1f);
    public static float PowerOf(float bar) => (bar - BarStart) / BarLength;

    /// <summary>Distância (unidades) prevista para o taco e a barra, sem vento (lie = fator do terreno, 1 no fairway).</summary>
    public static float Distance(float rangeYards, float bar, float lie = 1) => rangeYards * UnitsPerYard * PowerOf(bar) * lie;

    /// <summary>Ângulo de mira para um deslocamento (dx, dz): atan2(−dx, dz), a fórmula do cliente.</summary>
    public static float AimTo(float dx, float dz) => MathF.Atan2(-dx, dz);
    public static (float X, float Z) Direction(float aim) => (-MathF.Sin(aim), MathF.Cos(aim));

    /// <summary>Vento global do 0x59 (wind.cpp): intensidade (byte+1) na direção byte × 0,02464 rad.</summary>
    public static (float X, float Z) Wind(byte strength, byte direction)
    {
        var (x, z) = Direction(direction * 0.02464f);
        return (x * (strength + 1), z * (strength + 1));
    }

    /// <summary>Quanto o vento desloca a bola: o oponente do cliente mira em alvo − 3 × vento (não em putts).</summary>
    public const float WindFactor = 3;

    /// <summary>Diferença entre ângulos normalizada para −π..π.</summary>
    public static float AngleDiff(float a, float b)
    {
        float d = (a - b) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        else if (d < -MathF.PI) d += 2 * MathF.PI;
        return d;
    }
}

/// <summary>Tacada escolhida pelo bot: taco (+0x25), força 0..1 (barra = 140 + 360 × força) e mira (+0x19).</summary>
public readonly record struct BotShot(int Club, float Power, float Aim)
{
    public float Bar => ShotModel.BarOf(Power);
}

/// <summary>
/// Calibração durante a partida: compara onde cada tacada limpa (sem efeito nem tacada especial) caiu com o que o
/// <see cref="ShotModel"/> previa e acumula (média móvel) um fator de distância e um desvio de mira.
/// - mira: de todas as tacadas limpas que não são putt (a mira não depende dos stats do jogador);
/// - distância: só das tacadas do bot e dos ferros dos humanos (madeiras dependem do stat de força de cada um).
/// Ficam de fora: putts, água/OB, bola no buraco, tacadas curtas (&lt; 10 jardas) e desvios grandes demais
/// (obstáculo, curva) — a calibração só corrige um erro sistemático pequeno.
/// </summary>
public sealed class ShotCalibration
{
    const float Alpha = 0.3f, MinUnits = 10 * ShotModel.UnitsPerYard, MaxAngle = 0.3f;
    public float DistanceFactor { get; private set; } = 1;
    public float AimOffset { get; private set; }
    public int Samples { get; private set; }

    /// <summary>Uma tacada e onde a bola parou. false = descartada.</summary>
    public bool Observe(int club, float bar, float aim, float startX, float startZ, float endX, float endZ,
        byte windStrength, byte windDirection, byte state, bool learnDistance, int powerStat = 0)
    {
        if (club >= ShotModel.Putter1 || state is ShotResult.StateWaterOrOut or ShotResult.StateHoled) return false;
        float d = ShotModel.Distance(ShotModel.RangeYards(club, powerStat), bar);                       // previsto, na mira
        var (wx, wz) = ShotModel.Wind(windStrength, windDirection);
        float ax = endX - startX - wx * ShotModel.WindFactor, az = endZ - startZ - wz * ShotModel.WindFactor;   // real sem o vento
        float aLen = MathF.Sqrt(ax * ax + az * az);
        if (d < MinUnits || aLen < MinUnits) return false;
        float ratio = aLen / d, err = ShotModel.AngleDiff(ShotModel.AimTo(ax, az), aim);
        if (ratio is < 0.5f or > 2f || MathF.Abs(err) > MaxAngle) return false;
        AimOffset = Math.Clamp(AimOffset + Alpha * (err - AimOffset), -0.15f, 0.15f);
        if (learnDistance) DistanceFactor = Math.Clamp(DistanceFactor + Alpha * (ratio - DistanceFactor), 0.8f, 1.25f);
        Samples++;
        return true;
    }
}

/// <summary>
/// Cérebro do bot (sem bytes: a camada de protocolo só empacota o <see cref="BotShot"/>). Inspirado no oponente de
/// computador do cliente (CRival::SetVariable): taco pela distância, força linear, mira em alvo − 3 × vento.
/// O servidor não conhece a geometria do campo (dogleg, árvores, água) nem o terreno da bola, então:
/// - mira na bandeira, mas nunca além do alcance do taco: buracos longos são feitos em etapas;
/// - putter quando está a até <see cref="PuttYards"/> da bandeira (supõe green; o lie real não chega ao servidor);
/// - de 'PuttYards' a 110 jardas usa o ferro 9 (PW/SW têm alcances especiais perto da bandeira);
/// - depois de cair na água/OB no buraco, joga com 2 tacos mais curtos (como o CRival faz);
/// - memória do buraco (<see cref="Observe"/>): onde as bolas de todos pararam bem (rota segura, ex.: dogleg) e onde
///   tacadas caíram na água/OB ou pararam muito antes do previsto (obstáculo). O alvo direto só é usado se não cai perto
///   de um perigo conhecido; senão vai pelo lugar seguro que mais aproxima da bandeira, ou faz lay-up curto/desviado.
///   Assim nunca repete a tacada que já falhou;
/// - erro aleatório de mira e força conforme <see cref="Accuracy"/> (1 = perfeito).
/// </summary>
public sealed class BotGolfer(Random rng, float accuracy = 0.85f)
{
    public const float PuttYards = 20;
    /// <summary>O putt mira um pouco além do buraco (a bola tem de chegar).</summary>
    const float PuttExtraYards = 1;
    const float MaxAimError = 0.15f, MaxPowerError = 0.3f;

    public float Accuracy { get; } = Math.Clamp(accuracy, 0f, 1f);
    public ShotCalibration Calibration { get; } = new();
    /// <summary>Stat de força do bot (alcance das madeiras); o bot tem o kit de um jogador novo.</summary>
    public int PowerStat { get; init; }

    /// <summary>Raio (jardas) em volta de um perigo conhecido em que o bot não mira.</summary>
    public const float HazardYards = 15;
    /// <summary>Tacada que parou antes desta fração do previsto bateu em algo (árvore, parede): o caminho é perigo.</summary>
    const float BlockedFraction = 0.4f;
    int memoryHole = -1;
    readonly List<(float X, float Z)> safe = [], hazards = [];

    /// <summary>Perigos e lugares seguros conhecidos no buraco atual (para testes e log).</summary>
    public IReadOnlyList<(float X, float Z)> Hazards => hazards;
    public IReadOnlyList<(float X, float Z)> SafeSpots => safe;

    void UseHole(int hole)
    {
        if (hole == memoryHole) return;
        memoryHole = hole;
        safe.Clear();
        hazards.Clear();
    }

    /// <summary>
    /// Resultado de uma tacada de qualquer jogador no buraco: (sx, sz) saída, (tx, tz) onde a tacada deveria cair
    /// (mira × distância prevista), (ex, ez) onde parou. Água/OB (a bola volta ao ponto de saída) e tacada barrada
    /// marcam o ponto previsto como perigo; bola parada em jogo marca um lugar seguro. Putts não contam.
    /// </summary>
    public void Observe(int hole, float sx, float sz, float tx, float tz, float ex, float ez, byte state, bool putt)
    {
        UseHole(hole);
        if (putt || state == ShotResult.StateHoled) return;
        float planned = Dist(sx, sz, tx, tz), moved = Dist(sx, sz, ex, ez);
        if (state == ShotResult.StateWaterOrOut || (planned > 30 * ShotModel.UnitsPerYard && moved < planned * BlockedFraction))
            hazards.Add((tx, tz));
        else if (moved > 10 * ShotModel.UnitsPerYard)
            safe.Add((ex, ez));
    }

    static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    bool NearHazard(float x, float z)
    {
        foreach (var (hx, hz) in hazards)
            if (Dist(x, z, hx, hz) < HazardYards * ShotModel.UnitsPerYard) return true;
        return false;
    }

    /// <summary>
    /// Alvo da tacada longa: direto (bandeira ou o mais longe que o maior taco alcança na linha dela) se não cai perto
    /// de um perigo; senão o lugar seguro que mais aproxima da bandeira; senão lay-ups (mais curto e/ou desviado).
    /// </summary>
    (float X, float Z) Target(float x, float z, float pinX, float pinZ)
    {
        float reach = ShotModel.RangeYards(ShotModel.Driver, PowerStat) * Calibration.DistanceFactor * ShotModel.UnitsPerYard;
        float dist = Dist(x, z, pinX, pinZ);
        var direct = dist <= reach ? (pinX, pinZ) : (x + (pinX - x) * reach / dist, z + (pinZ - z) * reach / dist);
        if (hazards.Count == 0 || !NearHazard(direct.Item1, direct.Item2)) return direct;

        (float X, float Z)? best = null;
        float bestLeft = dist - 20 * ShotModel.UnitsPerYard;                  // tem de avançar pelo menos 20 jardas
        foreach (var (sx, sz) in safe)
        {
            float left = Dist(sx, sz, pinX, pinZ);
            if (left < bestLeft && Dist(x, z, sx, sz) <= reach && !NearHazard(sx, sz)) (best, bestLeft) = ((sx, sz), left);
        }
        if (best is { } b) return b;

        float baseAim = ShotModel.AimTo(pinX - x, pinZ - z), len = MathF.Min(dist, reach);
        foreach (var (turn, frac) in LayUps)
        {
            var (ux, uz) = ShotModel.Direction(baseAim + turn);
            float tx = x + ux * len * frac, tz = z + uz * len * frac;
            if (!NearHazard(tx, tz)) return (tx, tz);
        }
        var (fx, fz) = ShotModel.Direction(baseAim);                          // tudo marcado: bem curto na linha
        return (x + fx * len * 0.3f, z + fz * len * 0.3f);
    }

    /// <summary>Lay-ups em ordem de preferência: (desvio da linha da bandeira em rad, fração da distância).</summary>
    static readonly (float Turn, float Frac)[] LayUps =
        [(0, 0.7f), (0.3f, 0.8f), (-0.3f, 0.8f), (0, 0.5f), (0.6f, 0.7f), (-0.6f, 0.7f), (0.3f, 0.5f), (-0.3f, 0.5f)];

    /// <summary>Planeja a tacada da bola (x, z) para a bandeira (pinX, pinZ) com o vento atual. hole = índice do buraco (memória).</summary>
    public BotShot Plan(float x, float z, float pinX, float pinZ, byte windStrength, byte windDirection, bool cautious = false, int hole = -1)
    {
        if (hole >= 0) UseHole(hole);
        float dx = pinX - x, dz = pinZ - z;
        float yards = MathF.Sqrt(dx * dx + dz * dz) / ShotModel.UnitsPerYard;
        if (yards > PuttYards)
        {
            var (tx, tz) = Target(x, z, pinX, pinZ);
            (dx, dz) = (tx - x, tz - z);
            yards = MathF.Sqrt(dx * dx + dz * dz) / ShotModel.UnitsPerYard;
            if (yards <= PuttYards) yards = PuttYards + 1;                    // lay-up curto ainda é tacada, não putt
        }
        float power, aim;
        int club;
        if (yards <= PuttYards)
        {
            club = ShotModel.Putter1;
            power = (yards + PuttExtraYards) / ShotModel.RangeYards(club, 0, yards);
            aim = ShotModel.AimTo(dx, dz);
        }
        else
        {
            var (wx, wz) = ShotModel.Wind(windStrength, windDirection);
            wx *= ShotModel.WindFactor;
            wz *= ShotModel.WindFactor;
            float factor = Calibration.DistanceFactor;
            float need = MathF.Sqrt((dx - wx) * (dx - wx) + (dz - wz) * (dz - wz)) / ShotModel.UnitsPerYard;   // já com o vento
            club = ClubFor(need / factor);
            if (cautious) club = Math.Min(club + 2, ShotModel.Iron9);
            float reach = ShotModel.RangeYards(club, PowerStat) * factor;      // alcance real (calibrado)
            if (yards > reach) { dx *= reach / yards; dz *= reach / yards; }    // alvo: até onde o taco alcança
            dx -= wx;
            dz -= wz;
            power = MathF.Sqrt(dx * dx + dz * dz) / ShotModel.UnitsPerYard / reach;
            aim = ShotModel.AimTo(dx, dz) - Calibration.AimOffset;
        }
        float miss = 1 - Accuracy;
        aim += (float)(rng.NextDouble() * 2 - 1) * miss * MaxAimError;
        power *= 1 + (float)(rng.NextDouble() * 2 - 1) * miss * MaxPowerError;
        return new BotShot(club, Math.Clamp(power, 0.01f, 1f), aim);
    }

    /// <summary>O taco mais curto que alcança (1W..9I); longe demais = driver.</summary>
    public int ClubFor(float yards)
    {
        for (int c = ShotModel.Iron9; c > ShotModel.Driver; c--)
            if (ShotModel.RangeYards(c, PowerStat) >= yards) return c;
        return ShotModel.Driver;
    }
}
