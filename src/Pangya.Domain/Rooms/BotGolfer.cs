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
    /// <summary>Jardas a mais por tipo de power shot (CClub::GetPowerShotFactor: nenhum, simples, duplo, item).</summary>
    public static readonly int[] PowerShotYards = [0, 10, 20, 15];

    public static float RangeYards(int club, int powerStat = 0, float yardsToPin = 0, bool onGreen = true, int driveUp = 0, int powerShot = 0)
    {
        float r = Ranges[Math.Clamp(club, 0, Ranges.Length - 1)];
        if (club < Putter1) r += PowerShotYards[Math.Clamp(powerShot, 0, 3)];        // madeiras e ferros
        if (club <= LastWood) r += 2 * powerStat;                   // ferros não usam a força (club.c GetRange)
        if (club < Putter1) r += driveUp;                           // anéis (DriveUp): todo taco menos putter
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

/// <summary>Dificuldade do bot (escolhida na sala com "!bot nível").</summary>
public enum BotLevel { Easy, Normal, Hard, VeryHard, Impossible }

/// <summary>
/// Tacada escolhida pelo bot: taco (+0x25), força 0..1 (barra = 140 + 360 × força), mira (+0x19), power shot (0x56) e
/// tacada especial (+0x11: <see cref="Special"/>).
/// </summary>
public readonly record struct BotShot(int Club, float Power, float Aim, byte PowerShot = 0, byte Special = 0, byte Phase = 4,
    float Impact = 0, int Item = 0)
{
    /// <summary>Fase da tacada (+0x10): 4 PangYa, 3 boa, 2 normal, 1 ruim. Impact = desvio do centro do impacto (+0x04).</summary>
    public const byte PhasePangya = 4;
    public float Bar => ShotModel.BarOf(Power);
}

/// <summary>
/// Itens de partida que o bot usa (SPEC-bot-itens.md): S->C 0x58 antes do 0x53, um por tacada, tirados do tidItemSlot
/// que ele mostrou no 0x74. Os de power shot armam o PS sem gastar gauge (e dispensam o 0x56).
/// </summary>
public static class BotItem
{
    public const int PowerAssist = 0x18000004, SilentWind = 0x18000006, PowerEnhancer = 0x18000027;
    /// <summary>Tipo de power shot que o item arma (0 = não é de power shot): Power Assist simples, Power Enhancer tipo 3 (+15 jd).</summary>
    public static byte PowerShotOf(int item) => item switch { PowerAssist => 1, PowerEnhancer => 3, _ => 0 };
    /// <summary>Vento (m) a partir do qual o bot gasta um Silent Wind numa tacada longa.</summary>
    public const int SilentWindMeters = 5;
    public const float SilentWindMinYards = 150;
}

/// <summary>
/// Flags de tacada especial do bloco (+0x11; SPEC-bot-especiais.md §2.2). Tomahawk e Spike precisam de power shot armado
/// e saem com a velocidade ×1,3; Spike e Cobra só com madeira. Cobra: rasante que só sobe perto do fim (passa sob
/// galhos); o bot usa quando a linha direta já foi barrada por um obstáculo.
/// </summary>
public static class Special
{
    public const byte None = 0, Tomahawk = 0x10, Cobra = 0x20, Spike = 0x40;
    /// <summary>Chute inicial do alcance em relação à tacada normal com o mesmo power shot (velocidade ×1,3 ≈ +25 %).</summary>
    public const float InitialFactor = 1.25f;
    /// <summary>Cobra: alcance ≈ o nominal (sobe 100 jd antes do fim; SPEC-bot-especiais.md §6).</summary>
    public const float CobraInitialFactor = 1f;

    public static float Initial(byte kind) => kind == Cobra ? CobraInitialFactor : InitialFactor;
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
    const float ClubAlpha = 0.4f, MinClub = 0.7f, MaxClub = 1.3f;
    const float Alpha = 0.3f, MinUnits = 10 * ShotModel.UnitsPerYard, MaxAngle = 0.3f;
    public float DistanceFactor { get; private set; } = 1;
    public float AimOffset { get; private set; }
    public int Samples { get; private set; }
    /// <summary>Muda a cada ajuste (para gravar só o que mudou).</summary>
    public int Version { get; private set; }
    readonly Dictionary<byte, float> special = [];
    /// <summary>Fator de distância aprendido de cada taco (só depois de uma tacada com ele; antes vale o geral).</summary>
    readonly Dictionary<int, float> clubs = [];

    /// <summary>
    /// Alcance real do taco em relação ao modelo: se o ferro 5 vem caindo curto, o fator dele fica &lt; 1 e o bot passa a
    /// pegar um taco maior para a mesma distância (e um menor se vem passando).
    /// </summary>
    public float ClubFactor(int club) => clubs.TryGetValue(club, out var f) ? f : DistanceFactor;

    public System.Text.Json.Nodes.JsonObject ToJson()
    {
        var sp = new System.Text.Json.Nodes.JsonObject();
        foreach (var (k, f) in special) sp[k.ToString(System.Globalization.CultureInfo.InvariantCulture)] = f;
        var cl = new System.Text.Json.Nodes.JsonObject();
        foreach (var (k, f) in clubs) cl[k.ToString(System.Globalization.CultureInfo.InvariantCulture)] = f;
        return new() { ["distance"] = DistanceFactor, ["aim"] = AimOffset, ["samples"] = Samples, ["special"] = sp, ["clubs"] = cl };
    }

    /// <summary>Calibração gravada (valores fora das faixas voltam para dentro).</summary>
    public static ShotCalibration FromJson(System.Text.Json.Nodes.JsonObject o)
    {
        var c = new ShotCalibration
        {
            DistanceFactor = Math.Clamp(JsonNum.F(o["distance"], 1), 0.8f, 1.25f),
            AimOffset = Math.Clamp(JsonNum.F(o["aim"], 0), -0.15f, 0.15f),
            Samples = (int)Math.Max(JsonNum.F(o["samples"], 0), 0),
        };
        if (o["special"] is System.Text.Json.Nodes.JsonObject sp)
            foreach (var (k, v) in sp)
                if (byte.TryParse(k, out var kind) && kind != Special.None) c.special[kind] = Math.Clamp(JsonNum.F(v, Special.Initial(kind)), 0.9f, 1.8f);
        if (o["clubs"] is System.Text.Json.Nodes.JsonObject cl)
            foreach (var (k, v) in cl)
                if (int.TryParse(k, out var club) && club is >= 0 and < ShotModel.Putter1)
                    c.clubs[club] = Math.Clamp(JsonNum.F(v, c.DistanceFactor), MinClub, MaxClub);
        return c;
    }

    /// <summary>Alcance de uma tacada especial do bot em relação à normal (aprendido; começa em Special.InitialFactor).</summary>
    public float SpecialFactor(byte kind) => kind == Special.None ? 1 : special.GetValueOrDefault(kind, Special.Initial(kind));

    /// <summary>
    /// Tacada especial do bot: só ajusta o fator daquele tipo (a distância real comparada à prevista pela tacada normal
    /// com o mesmo power shot e o fator de distância atual). Mesmos descartes da <see cref="Observe"/>, faixa 0,9..1,8.
    /// </summary>
    public bool ObserveSpecial(byte kind, int club, float bar, float startX, float startZ, float endX, float endZ,
        byte windStrength, byte windDirection, byte state, int powerStat, int driveUp, int powerShot)
    {
        if (kind == Special.None || club >= ShotModel.Putter1 || state is ShotResult.StateWaterOrOut or ShotResult.StateHoled) return false;
        float d = ShotModel.Distance(ShotModel.RangeYards(club, powerStat, driveUp: driveUp, powerShot: powerShot), bar) * ClubFactor(club);
        var (wx, wz) = ShotModel.Wind(windStrength, windDirection);
        float ax = endX - startX - wx * ShotModel.WindFactor, az = endZ - startZ - wz * ShotModel.WindFactor;
        float aLen = MathF.Sqrt(ax * ax + az * az);
        if (d < MinUnits || aLen < MinUnits) return false;
        float ratio = aLen / d, f = SpecialFactor(kind);
        if (ratio is < 0.6f or > 2.2f) return false;
        special[kind] = Math.Clamp(f + Alpha * (ratio - f), 0.9f, 1.8f);
        Version++;
        return true;
    }

    /// <summary>Uma tacada e onde a bola parou. false = descartada.</summary>
    public bool Observe(int club, float bar, float aim, float startX, float startZ, float endX, float endZ,
        byte windStrength, byte windDirection, byte state, bool learnDistance, int powerStat = 0, int driveUp = 0, int powerShot = 0)
    {
        if (club >= ShotModel.Putter1 || state is ShotResult.StateWaterOrOut or ShotResult.StateHoled) return false;
        float d = ShotModel.Distance(ShotModel.RangeYards(club, powerStat, driveUp: driveUp, powerShot: powerShot), bar);                       // previsto, na mira
        var (wx, wz) = ShotModel.Wind(windStrength, windDirection);
        float ax = endX - startX - wx * ShotModel.WindFactor, az = endZ - startZ - wz * ShotModel.WindFactor;   // real sem o vento
        float aLen = MathF.Sqrt(ax * ax + az * az);
        if (d < MinUnits || aLen < MinUnits) return false;
        float ratio = aLen / d, err = ShotModel.AngleDiff(ShotModel.AimTo(ax, az), aim);
        if (ratio is < 0.5f or > 2f || MathF.Abs(err) > MaxAngle) return false;
        AimOffset = Math.Clamp(AimOffset + Alpha * (err - AimOffset), -0.15f, 0.15f);
        if (learnDistance)
        {
            float cf = ClubFactor(club);
            clubs[club] = Math.Clamp(cf + ClubAlpha * (ratio - cf), MinClub, MaxClub);
            DistanceFactor = Math.Clamp(DistanceFactor + Alpha * (ratio - DistanceFactor), 0.8f, 1.25f);
        }
        Samples++;
        Version++;
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
public sealed class BotGolfer(Random rng, float accuracy = 0.85f, bool readsWind = true, bool remembers = true, int maxPowerShot = 0,
    byte specials = Special.None, int impactRange = 0, BotLevelKnowledge? knowledge = null)
{
    /// <summary>
    /// O que o nível já aprendeu (calibração + memória dos buracos), compartilhado entre as partidas e gravado no banco.
    /// Sem ele (testes), um aprendizado só deste bot.
    /// </summary>
    readonly BotLevelKnowledge mem = knowledge ?? new BotLevelKnowledge(BotLevel.Normal);
    /// <summary>Mapa da partida (chave da memória dos buracos junto com o número do buraco).</summary>
    public byte Course { get; set; }

    /// <summary>
    /// Erro natural como o oponente do cliente (CRival, SPEC-bot-especiais.md §3): impacto = centro ± ImpactRange; fase 4 se
    /// |desvio| &lt; 2, 3 se &lt; N, 2 se &lt; W, senão 1 (N = max(2, precisão), W = min(N + 10, 35)). Todos os clientes aplicam o
    /// mesmo desvio da fase, de forma idêntica.
    /// </summary>
    public int ImpactRange { get; } = Math.Max(impactRange, 0);
    /// <summary>Stat de precisão do bot (fórmula do cliente), para as faixas N/W.</summary>
    public int AccuracyStat { get; set; } = 10;

    /// <summary>Área PangYa do cliente sem bônus (CPowerGauge::GetPangYaArea = 2).</summary>
    const int PangyaArea = 2;

    /// <summary>Sorteia o impacto e devolve (fase, desvio).</summary>
    public (byte Phase, float Impact) DrawImpact()
    {
        if (ImpactRange == 0) return (BotShot.PhasePangya, 0);
        int x = rng.Next(-ImpactRange, ImpactRange + 1), ax = Math.Abs(x);
        int n = Math.Max(2, AccuracyStat), w = Math.Min(n + 10, 35);
        byte phase = ax <= PangyaArea ? (byte)4 : ax <= n ? (byte)3 : ax <= w ? (byte)2 : (byte)1;   // |d| < área + 0,5
        return (phase, x);
    }
    /// <summary>Tacadas especiais que o nível permite (flags de <see cref="Special"/>).</summary>
    public byte Specials { get; } = specials;

    /// <summary>
    /// Gauge de power shot do bot, espelhando o que cada cliente calcula (SPEC-ingame "Gauge", SPEC-bot-itens §5.3): sem
    /// power shot, PangYa +12 e boa (fase 3) +4; power shot de gauge −33 (simples) / −66 (duplo), por item não custa;
    /// estouro de tempo −30; entre 0 e 99.
    /// </summary>
    public float Gauge { get; private set; }
    public const float GaugeMax = 99, GaugePerPangya = 12, GaugePerGood = 4, GaugeTimeOut = 30;

    /// <summary>Itens que o bot ainda tem nos slots (espelho do tidItemSlot do 0x74; cada uso tira uma ocorrência).</summary>
    public List<int> Items { get; } = [];
    bool Has(int item) => Items.Contains(item);
    /// <summary>A tacada usou o item: sai dos slots (o cliente também tira, até quando o item falha).</summary>
    public void UseItem(int item) => Items.Remove(item);

    /// <summary>
    /// Power shots que dá para armar agora, do menor ao maior alcance: (tipo, item que arma; 0 = gauge). Simples pelo
    /// gauge, ou pelo Power Assist se o gauge não dá; tipo 3 (+15) pelo Power Enhancer; duplo pelo gauge.
    /// </summary>
    List<(byte Ps, int Item)> PowerOptions()
    {
        var o = new List<(byte, int)>(3);
        int g = AvailablePowerShot();
        if (g >= 1) o.Add((1, 0));
        else if (Has(BotItem.PowerAssist)) o.Add((1, BotItem.PowerAssist));
        if (Has(BotItem.PowerEnhancer)) o.Add((3, BotItem.PowerEnhancer));
        if (g >= 2) o.Add((2, 0));
        return o;
    }
    /// <summary>0 = nunca usa power shot, 1 = só simples, 2 = simples ou duplo.</summary>
    public int MaxPowerShot { get; } = Math.Clamp(maxPowerShot, 0, 2);

    /// <summary>Depois que a tacada do bot saiu (ou ele estourou o tempo): atualiza o gauge.</summary>
    public void ShotDone(BotShot shot, bool timeOut = false)
    {
        float g = Gauge + (timeOut ? -GaugeTimeOut
            : shot.PowerShot > 0 ? (BotItem.PowerShotOf(shot.Item) != 0 ? 0 : shot.PowerShot == 2 ? -66 : -33)
            : shot.Phase == BotShot.PhasePangya ? GaugePerPangya : shot.Phase == 3 ? GaugePerGood : 0);
        Gauge = Math.Clamp(g, 0, GaugeMax);
    }

    /// <summary>Power shot que o bot pode usar agora (pelo nível e pelo gauge).</summary>
    int AvailablePowerShot() => MaxPowerShot >= 2 && Gauge >= 66 ? 2 : MaxPowerShot >= 1 && Gauge >= 33 ? 1 : 0;

    /// <summary>
    /// Bot de um nível: precisão (erro de mira/força), se compensa o vento e se usa a memória do buraco.
    /// Normal usa a precisão da configuração (Game.BotAccuracy).
    /// </summary>
    /// knowledge = o aprendizado guardado daquele nível (BotKnowledge.For(level)); null = começa do zero.
    public static BotGolfer For(BotLevel level, Random rng, float normalAccuracy = 0.85f, BotLevelKnowledge? knowledge = null) => level switch
    {
        BotLevel.Easy => new(rng, 0.55f, readsWind: false, remembers: false, impactRange: 25, knowledge: knowledge),
        BotLevel.Hard => new(rng, 0.93f, maxPowerShot: 2, specials: Special.Tomahawk, impactRange: 6, knowledge: knowledge),
        BotLevel.VeryHard => new(rng, 0.98f, maxPowerShot: 2, specials: Special.Tomahawk | Special.Spike | Special.Cobra, impactRange: 3, knowledge: knowledge),
        BotLevel.Impossible => new(rng, 1f, maxPowerShot: 2, specials: Special.Tomahawk | Special.Spike | Special.Cobra, knowledge: knowledge),
        _ => new(rng, normalAccuracy, maxPowerShot: 1, impactRange: 10, knowledge: knowledge),
    };

    /// <summary>Nível pelo nome do chat (pt/en, sem acento); null = desconhecido.</summary>
    public static BotLevel? ParseLevel(string s) => s.Replace(" ", "").Replace("_", "").ToLowerInvariant() switch
    {
        "easy" or "facil" or "fácil" => BotLevel.Easy,
        "normal" or "medio" or "médio" => BotLevel.Normal,
        "hard" or "dificil" or "difícil" => BotLevel.Hard,
        "veryhard" or "muitodificil" or "muitodifícil" => BotLevel.VeryHard,
        "impossible" or "impossivel" or "impossível" => BotLevel.Impossible,
        _ => null,
    };

    public bool ReadsWind { get; } = readsWind;
    public bool Remembers { get; } = remembers;

    public const float PuttYards = 20;
    /// <summary>O putt mira um pouco além do buraco (a bola tem de chegar).</summary>
    const float PuttExtraYards = 1;
    const float MaxAimError = 0.15f, MaxPowerError = 0.3f;

    public float Accuracy { get; } = Math.Clamp(accuracy, 0f, 1f);
    public ShotCalibration Calibration => mem.Calibration;
    /// <summary>Stat de força do bot (alcance das madeiras); o bot tem o kit de um jogador novo.</summary>
    public int PowerStat { get; set; }
    /// <summary>Jardas a mais dos anéis do bot (DriveUp).</summary>
    public int DriveUp { get; set; }

    /// <summary>Raio (jardas) em volta de um perigo conhecido em que o bot não mira.</summary>
    public const float HazardYards = 15;
    /// <summary>Tacada que parou antes desta fração do previsto bateu em algo (árvore, parede): o caminho é perigo.</summary>
    const float BlockedFraction = 0.4f;
    int memoryHole = -1;
    HoleMemory cur = new();
    List<(float X, float Z)> safe => cur.Safe;
    List<(float X, float Z)> hazards => cur.Hazards;
    List<(float X, float Z)> blocked => cur.Blocked;
    List<(float X, float Z)> cobraBlocked => cur.CobraBlocked;

    /// <summary>Perigos e lugares seguros conhecidos no buraco atual (para testes e log).</summary>
    public IReadOnlyList<(float X, float Z)> Hazards => hazards;
    public IReadOnlyList<(float X, float Z)> SafeSpots => safe;

    /// <summary>Passa a usar a memória do buraco (número do buraco no mapa <see cref="Course"/>).</summary>
    void UseHole(int hole)
    {
        if (hole == memoryHole) return;
        memoryHole = hole;
        cur = mem.Hole(Course, (byte)hole);
    }

    /// <summary>
    /// Resultado de uma tacada de qualquer jogador no buraco: (sx, sz) saída, (tx, tz) onde a tacada deveria cair
    /// (mira × distância prevista), (ex, ez) onde parou. Água/OB (a bola volta ao ponto de saída) e tacada barrada
    /// marcam o ponto previsto como perigo; bola parada em jogo marca um lugar seguro. Putts não contam. A tacada barrada
    /// (obstáculo) também marca o ponto como "barrado": dá para passar por baixo com Cobra, a não ser que um Cobra já
    /// tenha sido barrado ali (cobra = a tacada observada foi um Cobra).
    /// </summary>
    public void Observe(int hole, float sx, float sz, float tx, float tz, float ex, float ez, byte state, bool putt, bool cobra = false,
        float? pinX = null, float? pinZ = null)
    {
        if (!Remembers) return;
        UseHole(hole);
        if (putt || state == ShotResult.StateHoled) return;
        float planned = Dist(sx, sz, tx, tz), moved = Dist(sx, sz, ex, ez);
        bool wasBlocked = planned > 30 * ShotModel.UnitsPerYard && moved < planned * BlockedFraction;
        // a memória vai para todos os níveis (é geometria do mapa); só este bot usa
        if (state == ShotResult.StateWaterOrOut || wasBlocked)
        {
            // água/OB: o cliente só devolve o ponto de saída, não onde a bola caiu; ela pode ter caído antes do alvo
            // (vento, tacada curta, rasante). Marca também o trecho final da linha, para não repetir mais curto. Perto
            // da bandeira não marca: quem mirou nela e saiu errou a força/direção, a bandeira não é água.
            bool water = state == ShotResult.StateWaterOrOut;
            if (!water || !NearPin(tx, tz, pinX, pinZ)) mem.Record(Course, (byte)hole, HoleMark.Hazard, tx, tz);
            if (water)
                foreach (var f in WaterLine)
                {
                    float lx = sx + (tx - sx) * f, lz = sz + (tz - sz) * f;
                    if (!NearPin(lx, lz, pinX, pinZ)) mem.Record(Course, (byte)hole, HoleMark.Hazard, lx, lz);
                }
            if (state != ShotResult.StateWaterOrOut) mem.Record(Course, (byte)hole, cobra ? HoleMark.CobraBlocked : HoleMark.Blocked, tx, tz);
        }
        else if (moved > 10 * ShotModel.UnitsPerYard)
            mem.Record(Course, (byte)hole, HoleMark.Safe, ex, ez);
    }

    /// <summary>Raio (jardas) em volta da bandeira em que água/OB não é gravada.</summary>
    public const float PinClearYards = 20;

    /// <summary>Tira da memória água/OB perto da bandeira (gravados antes da regra de <see cref="PinClearYards"/>).</summary>
    void ClearNearPin(int hole, float pinX, float pinZ)
    {
        bool changed = false;
        for (int i = hazards.Count - 1; i >= 0; i--)
            if (NearPin(hazards[i].X, hazards[i].Z, pinX, pinZ) && !Near(blocked, hazards[i].X, hazards[i].Z))
            {
                hazards.RemoveAt(i);
                changed = true;
            }
        if (changed) mem.MarkDirty(Course, (byte)hole);
    }

    static bool NearPin(float x, float z, float? pinX, float? pinZ) =>
        pinX is { } px && pinZ is { } pz && Dist(x, z, px, pz) < PinClearYards * ShotModel.UnitsPerYard;

    /// <summary>Frações da linha saída -> alvo também marcadas como perigo quando a bola cai na água/OB.</summary>
    static readonly float[] WaterLine = [0.85f, 0.7f];

    static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    bool NearHazard(float x, float z) => Near(hazards, x, z);

    static bool Near(List<(float X, float Z)> list, float x, float z)
    {
        foreach (var (hx, hz) in list)
            if (Dist(x, z, hx, hz) < HazardYards * ShotModel.UnitsPerYard) return true;
        return false;
    }

    /// <summary>Todo perigo perto do ponto é obstáculo (não água/OB) que ainda não barrou um Cobra.</summary>
    bool OnlyBlockedNear(float x, float z)
    {
        foreach (var (hx, hz) in hazards)
            if (Dist(x, z, hx, hz) < HazardYards * ShotModel.UnitsPerYard && !Near(blocked, hx, hz)) return false;
        return Near(blocked, x, z) && !Near(cobraBlocked, x, z);
    }

    bool CanCobra => (Specials & Special.Cobra) != 0 && PowerOptions().Count > 0;

    /// <summary>
    /// Alvo da tacada longa: direto (bandeira ou o mais longe que o maior taco alcança na linha dela) se não cai perto
    /// de um perigo; Cobra na mesma linha se o perigo é só um obstáculo que barrou a bola (e o nível tem Cobra); senão o
    /// lugar seguro que mais aproxima da bandeira; senão lay-ups (mais curto e/ou desviado).
    /// </summary>
    (float X, float Z) Target(float x, float z, float pinX, float pinZ, out bool cobra)
    {
        cobra = false;
        float reach = MaxReachYards() * ShotModel.UnitsPerYard;
        float dist = Dist(x, z, pinX, pinZ);
        // a bandeira ao alcance (com power shot, item ou especial): vai nela. Perigo perto da bandeira é erro de quem
        // mirou nela; só um obstáculo já marcado na frente dela muda o plano
        if (dist <= reach && !Near(blocked, pinX, pinZ)) return (pinX, pinZ);
        var direct = dist <= reach ? (pinX, pinZ) : (x + (pinX - x) * reach / dist, z + (pinZ - z) * reach / dist);
        if (hazards.Count == 0 || !NearHazard(direct.Item1, direct.Item2)) return direct;
        if (CanCobra)
        {
            float cobraReach = ShotModel.RangeYards(ShotModel.Driver, PowerStat, driveUp: DriveUp, powerShot: 1)
                * Calibration.ClubFactor(ShotModel.Driver) * Calibration.SpecialFactor(Special.Cobra) * ShotModel.UnitsPerYard;
            var low = dist <= cobraReach ? (pinX, pinZ) : (x + (pinX - x) * cobraReach / dist, z + (pinZ - z) * cobraReach / dist);
            if (!NearHazard(low.Item1, low.Item2) || OnlyBlockedNear(low.Item1, low.Item2)) { cobra = true; return low; }
        }

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

    /// <summary>
    /// Desvios em ordem de preferência: (ângulo em relação à linha da bandeira em rad, fração da distância). Primeiro
    /// outra direção com a mesma força; só depois mais curto.
    /// </summary>
    static readonly (float Turn, float Frac)[] LayUps =
        [(0.15f, 1), (-0.15f, 1), (0.3f, 1), (-0.3f, 1), (0.45f, 0.95f), (-0.45f, 0.95f), (0, 0.8f), (0.3f, 0.8f), (-0.3f, 0.8f),
         (0, 0.6f), (0.6f, 0.7f), (-0.6f, 0.7f)];

    /// <summary>Alcance do driver com o maior power shot disponível (gauge ou item) e a melhor especial permitida (calibrado).</summary>
    float MaxReachYards()
    {
        var opts = PowerOptions();
        int ps = opts.Count > 0 ? opts[^1].Ps : 0;
        float r = ShotModel.RangeYards(ShotModel.Driver, PowerStat, driveUp: DriveUp, powerShot: ps) * Calibration.ClubFactor(ShotModel.Driver);
        return ps > 0 ? r * Calibration.SpecialFactor(BestSpecial()) : r;
    }

    /// <summary>A especial de maior alcance que o nível permite (pelo fator aprendido; empate = Tomahawk).</summary>
    byte BestSpecial()
    {
        byte best = Special.None;
        float f = 1;
        foreach (var k in SpecialOrder)
            if ((Specials & k) != 0 && Calibration.SpecialFactor(k) > f) (best, f) = (k, Calibration.SpecialFactor(k));
        return best;
    }

    static readonly byte[] SpecialOrder = [Special.Tomahawk, Special.Spike];

    /// <summary>Planeja a tacada da bola (x, z) para a bandeira (pinX, pinZ) com o vento atual. hole = número do buraco (memória).</summary>
    public BotShot Plan(float x, float z, float pinX, float pinZ, byte windStrength, byte windDirection, int hole = -1)
    {
        if (hole >= 0) { UseHole(hole); ClearNearPin(hole, pinX, pinZ); }
        float dx = pinX - x, dz = pinZ - z;
        float yards = MathF.Sqrt(dx * dx + dz * dz) / ShotModel.UnitsPerYard;
        bool useCobra = false;
        if (yards > PuttYards)
        {
            var (tx, tz) = Target(x, z, pinX, pinZ, out useCobra);
            (dx, dz) = (tx - x, tz - z);
            yards = MathF.Sqrt(dx * dx + dz * dz) / ShotModel.UnitsPerYard;
            if (yards <= PuttYards) yards = PuttYards + 1;                    // lay-up curto ainda é tacada, não putt
        }
        float power, aim;
        int club;
        byte ps = 0, special = Special.None;
        int item = 0;
        if (yards <= PuttYards)
        {
            club = ShotModel.Putter1;
            power = (yards + PuttExtraYards) / ShotModel.RangeYards(club, 0, yards);
            aim = ShotModel.AimTo(dx, dz);
        }
        else
        {
            var c = Choose(dx, dz, windStrength, windDirection, useCobra);
            // vento forte contra ou de lado numa tacada longa: Silent Wind (vento 1 m só nesta tacada), se não precisa de
            // item de power shot. Vento a favor ajuda (leva a bola por cima da água): esse não se corta
            if (c.Item == 0 && ReadsWind && windStrength + 1 >= BotItem.SilentWindMeters && yards > BotItem.SilentWindMinYards
                && !Tailwind(dx, dz, windStrength, windDirection) && Has(BotItem.SilentWind))
            {
                var calm = Choose(dx, dz, 0, windDirection, useCobra);
                if (calm.Item == 0) c = calm with { Item = BotItem.SilentWind };
            }
            (club, ps, special, item) = (c.Club, c.Ps, c.Special, c.Item);
            var (wx, wz) = (c.Wx, c.Wz);
            float reach = ShotModel.RangeYards(club, PowerStat, driveUp: DriveUp, powerShot: ps) * Calibration.ClubFactor(club)
                * Calibration.SpecialFactor(special);                                                        // alcance real (calibrado)
            if (yards > reach) { dx *= reach / yards; dz *= reach / yards; }    // alvo: até onde o taco alcança
            dx -= wx;
            dz -= wz;
            power = MathF.Sqrt(dx * dx + dz * dz) / ShotModel.UnitsPerYard / reach;
            aim = ShotModel.AimTo(dx, dz) - Calibration.AimOffset;
        }
        float miss = 1 - Accuracy;
        aim += (float)(rng.NextDouble() * 2 - 1) * miss * MaxAimError;
        power *= 1 + (float)(rng.NextDouble() * 2 - 1) * miss * MaxPowerError;
        // putt e tacada especial saem limpos (fase 4); o resto com o erro natural do impacto
        var (phase, impact) = club >= ShotModel.Putter1 || special != Special.None ? (BotShot.PhasePangya, 0f) : DrawImpact();
        return new BotShot(club, Math.Clamp(power, 0.01f, 1f), aim, ps, special, phase, impact, item);
    }

    /// <summary>Vento mais a favor do que de lado/contra: componente na direção da tacada ≥ metade da força.</summary>
    static bool Tailwind(float dx, float dz, byte windStrength, byte windDirection)
    {
        var (wx, wz) = ShotModel.Wind(windStrength, windDirection);
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 1e-3f) return false;
        float along = (wx * dx + wz * dz) / len;
        return along >= 0.5f * (windStrength + 1);
    }

    /// <summary>
    /// Taco, power shot, especial e item para o deslocamento (dx, dz) com aquele vento. Power shot só no driver quando
    /// nem ele alcança: o menor que resolve (gauge simples ou Power Assist, Power Enhancer, gauge duplo); se nem o maior
    /// alcança e o nível permite, o maior com Tomahawk/Spike (precisam do power shot armado).
    /// </summary>
    (int Club, byte Ps, byte Special, int Item, float Wx, float Wz) Choose(float dx, float dz, byte windStrength, byte windDirection,
        bool useCobra)
    {
        var (wx, wz) = ReadsWind ? ShotModel.Wind(windStrength, windDirection) : (0f, 0f);
        wx *= ShotModel.WindFactor;
        wz *= ShotModel.WindFactor;
        float factor = Calibration.ClubFactor(ShotModel.Driver);
        float need = MathF.Sqrt((dx - wx) * (dx - wx) + (dz - wz) * (dz - wz)) / ShotModel.UnitsPerYard;   // já com o vento
        int club = ClubFor(need);                                            // pelo alcance aprendido de cada taco
        byte ps = 0, special = Special.None;
        int item = 0;
        var opts = PowerOptions();
        if (useCobra && opts.Count > 0)                         // rasante por baixo do obstáculo: driver + PS
            return (ShotModel.Driver, opts[0].Ps, Special.Cobra, opts[0].Item, wx, wz);
        if (club == ShotModel.Driver && need / factor > ShotModel.RangeYards(club, PowerStat, driveUp: DriveUp) && opts.Count > 0)
        {
            (ps, item) = opts[^1];
            bool fits = false;
            foreach (var (p, it) in opts)
                if (need / factor <= ShotModel.RangeYards(club, PowerStat, driveUp: DriveUp, powerShot: p)) { (ps, item, fits) = (p, it, true); break; }
            if (!fits) special = BestSpecial();
        }
        return (club, ps, special, item, wx, wz);
    }

    /// <summary>O taco mais curto que alcança (1W..9I, com o alcance aprendido de cada um); longe demais = driver.</summary>
    public int ClubFor(float yards)
    {
        for (int c = ShotModel.Iron9; c > ShotModel.Driver; c--)
            if (ShotModel.RangeYards(c, PowerStat, driveUp: DriveUp) * Calibration.ClubFactor(c) >= yards) return c;
        return ShotModel.Driver;
    }
}
