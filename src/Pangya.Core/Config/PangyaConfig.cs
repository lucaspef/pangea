using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pangya.Core.Config;

/// <summary>
/// Configuração de todos os servidores, lida de um arquivo JSON (padrão: config/pangya.json).
/// Nada de IP, porta, banco ou segredo fica no código. Campos desconhecidos no arquivo são erro
/// (evita erro de digitação passar despercebido).
/// </summary>
public sealed class PangyaConfig
{
    /// <summary>Servidores que este processo roda (se a linha de comando não disser): web, login, game, messenger.</summary>
    public string[] Run { get; set; } = ["web", "login", "game"];
    public NetworkConfig Network { get; set; } = new();
    public WebConfig Web { get; set; } = new();
    public LoginConfig Login { get; set; } = new();
    public GameConfig Game { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public DataConfig Data { get; set; } = new();
    public NewPlayerConfig NewPlayer { get; set; } = new();
    public LotteryConfig Lottery { get; set; } = new();
    public MessengerConfig Messenger { get; set; } = new();
    public RankingConfig Ranking { get; set; } = new();

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Caminho padrão: variável PANGYA_CONFIG ou config/pangya.json.</summary>
    public static string DefaultPath => Environment.GetEnvironmentVariable("PANGYA_CONFIG") ?? "config/pangya.json";

    public static PangyaConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            throw new FileNotFoundException($"Arquivo de configuração não encontrado: {Path.GetFullPath(path)} (copie config/pangya.example.json)");
        return Parse(File.ReadAllText(path));
    }

    public static PangyaConfig Parse(string json)
    {
        var cfg = JsonSerializer.Deserialize<PangyaConfig>(json, Json) ?? throw new InvalidDataException("Configuração vazia");
        cfg.Validate();
        return cfg;
    }

    void Validate()
    {
        if (string.IsNullOrWhiteSpace(Database.ConnectionString))
            throw new InvalidDataException("Database.ConnectionString é obrigatório");
        Limits.Validate();
    }
}

public sealed class NetworkConfig
{
    /// <summary>IP onde os servidores escutam.</summary>
    public string BindIp { get; set; } = "0.0.0.0";
    /// <summary>IP anunciado ao cliente (lista de servidores).</summary>
    public string PublicIp { get; set; } = "127.0.0.1";
}

/// <summary>Servidor HTTP: login do cliente (LoginForGame.aspx) e página de cadastro.</summary>
public sealed class WebConfig
{
    public int Port { get; set; } = 80;
    /// <summary>Login com conta inexistente cria a conta. SÓ PARA TESTES: nunca ligue num servidor aberto.</summary>
    public bool AutoRegister { get; set; }
    /// <summary>Painel de administração em /admin (só contas GM). Desligado por padrão.</summary>
    public bool AdminEnabled { get; set; }
    /// <summary>IPs que podem abrir o painel (padrão: só a própria máquina).</summary>
    public string[] AdminAllowedIps { get; set; } = ["127.0.0.1", "::1"];
}

public sealed class LoginConfig
{
    /// <summary>O cliente 645 conecta na 10101 (fixo no exe); o 642 live sorteia 10101/10102.</summary>
    public int[] Ports { get; set; } = [10101];
    /// <summary>Identificador mandado no hello.</summary>
    public uint ServerUid { get; set; } = 10101;
}

/// <summary>Um game server (lobby, canais, salas...). Vários processos podem rodar, cada um com seu Id e porta.</summary>
public sealed class GameConfig
{
    /// <summary>Id na lista de servidores (único entre os game servers).</summary>
    public int Id { get; set; } = 20201;
    public string Name { get; set; } = "PangYa";
    public int Port { get; set; } = 20201;
    public int MaxUsers { get; set; } = 1000;
    public ChannelConfig[] Channels { get; set; } = [new()];
    /// <summary>Espera do bot antes de tacar (segundos).</summary>
    public double BotDelaySeconds { get; set; } = 4;
    /// <summary>Se nem todos mandarem "pronto para o tee", o servidor libera depois deste tempo (segundos).</summary>
    public double TeeFallbackSeconds { get; set; } = 15;
    /// <summary>true = o bot só passa a vez (estouro de tempo) em vez de tacar.</summary>
    public bool BotPasses { get; set; }
    /// <summary>Mapas permitidos (número do curso). Vazio = os ativos no Course.iff, como o cliente mostra.
    /// Ex.: liberar o Wiz City (19) quando o pak dele estiver instalado no cliente.</summary>
    public int[] Courses { get; set; } = [];
    /// <summary>Precisão do bot, 0..1 (1 = sem erro aleatório de mira/força).</summary>
    public float BotAccuracy { get; set; } = 0.85f;
    /// <summary>
    /// Velocidade da bola do bot no VS, como o Time Booster (S->C 0xC5 para todos; o item do cliente usa 3). 0 ou 1 = normal.
    /// </summary>
    public float BotFastForward { get; set; } = 2;
    /// <summary>Espera depois da tacada do bot até acelerar (a bola já em voo; antes disso o cliente volta para 1×).</summary>
    public double BotFastForwardDelaySeconds { get; set; } = 1.5;
    public RewardConfig Rewards { get; set; } = new();
    public TreasureHunterConfig TreasureHunter { get; set; } = new();
}

/// <summary>
/// Treasure Hunter (SPEC-treasure-hunter.md): barra de pontos da partida (stroke/team) e caixas de prêmio no fim.
/// Prêmios só de itens 0x18..0x1B (o cliente soma no inventário dele); 0x1A000010 = pang.
/// </summary>
public sealed class TreasureHunterConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Taxa de caixas em % (100 = a tabela do GB).</summary>
    public int RatePercent { get; set; } = 100;
    public TreasurePrize[] Prizes { get; set; } =
    [
        new() { TypeId = 0x1A000010, Min = 50, Max = 300, Weight = 40 },     // pang
        new() { TypeId = 0x18000004, Min = 1, Max = 2, Weight = 10 },       // 체력 보조제 (power shot)
        new() { TypeId = 0x18000006, Min = 1, Max = 1, Weight = 8 },        // 사일런트 윈드
        new() { TypeId = 0x1A000011, Min = 1, Max = 3, Weight = 10 },       // 타임부스터
        new() { TypeId = 0x1A000040, Min = 1, Max = 2, Weight = 8 },        // 오토 캘리퍼스
        new() { TypeId = 0x18000002, Min = 1, Max = 1, Weight = 6 },        // 럭키 팡야
        new() { TypeId = 0x18000025, Min = 1, Max = 1, Weight = 5 },        // 체력 보충제 (gauge +33)
        new() { TypeId = 0x18000027, Min = 1, Max = 1, Weight = 3 },        // 체력 강화제 (power shot +15)
    ];
}

public sealed class TreasurePrize
{
    public int TypeId { get; set; }
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
    public int Weight { get; set; } = 1;
}

/// <summary>
/// Mensageiro (amigos, online, conversa; docs/protocolo/SPEC-messenger.md). Roda no mesmo processo do game server, que
/// confirma quem está logado. O cliente descarta servidores com usuários ≥ máximo − 150: MaxUsers alto.
/// </summary>
public sealed class MessengerConfig
{
    public int Id { get; set; } = 30303;
    public string Name { get; set; } = "Messenger";
    public int Port { get; set; } = 30303;
    public int MaxUsers { get; set; } = 3000;
    public int MaxFriends { get; set; } = 50;
}

/// <summary>
/// Ranking (docs/protocolo/SPEC-ranking.md). Roda no processo do game (que manda o endereço no 0xA0 e confirma quem
/// pede). O cliente só aceita IP numérico (Network.PublicIp). Retrato recalculado ao subir e todo dia na hora RefreshHour.
/// </summary>
public sealed class RankingConfig
{
    public int Id { get; set; } = 30474;
    public string Name { get; set; } = "Ranking";
    public int Port { get; set; } = 30474;
    /// <summary>Hora local (0..23) do recálculo diário.</summary>
    public int RefreshHour { get; set; } = 5;
}

/// <summary>Recompensa de fim de partida (o pang informado pelo cliente é limitado por buraco).</summary>
public sealed class RewardConfig
{
    /// <summary>Taxa de EXP do servidor em % (100 = normal), como a taxa do servidor GB.</summary>
    public int ExpRate { get; set; } = 100;
    public int MaxPangPerHole { get; set; } = 1000;
    /// <summary>
    /// Bots contam como jogadores para os troféus do torneio (que só saem com 10+ jogadores). false = fiel ao original;
    /// true serve para testar sozinho com bots.
    /// </summary>
    public bool TrophiesCountBots { get; set; }
}

public sealed class ChannelConfig
{
    public string Name { get; set; } = "Canal 1";
    public int MaxUsers { get; set; } = 100;
}

/// <summary>O que um jogador novo recebe (typeids do pangya.iff).</summary>
public sealed class NewPlayerConfig
{
    public long Pang { get; set; } = 100_000;
    public long Cookie { get; set; }
    /// <summary>Air Knight: o club set que o tutorial dá (golfruletutorial.cpp:125).</summary>
    public int ClubSet { get; set; } = 0x10000000;
    /// <summary>Pangya Aztec: a bola básica.</summary>
    public int Ball { get; set; } = 0x14000000;
    public int BallCount { get; set; } = 100;
}

public sealed class DatabaseConfig
{
    public string ConnectionString { get; set; } = "";
}

public sealed class LoggingConfig
{
    /// <summary>Debug, Info, Warn ou Error. Debug mostra cada pacote.</summary>
    public Logging.LogLevel Level { get; set; } = Logging.LogLevel.Info;
    /// <summary>Pasta dos arquivos de log (vazio = só console).</summary>
    public string Dir { get; set; } = "logs";
}

public sealed class DataConfig
{
    /// <summary>Dados do jogo do cliente (ZIP de tabelas .iff).</summary>
    public string IffPath { get; set; } = "data/pangya.iff";
}

/// <summary>Limites contra abuso.</summary>
public sealed class LimitsConfig
{
    public int MaxConnectionsPerIp { get; set; } = 10;
    public int MaxPacketsPerSecond { get; set; } = 100;
    public int MaxPacketSize { get; set; } = 8192;
    public int MaxLoginAttemptsPerMinute { get; set; } = 10;
    /// <summary>Inatividade antes do login (conexões que não se identificam).</summary>
    public int IdleTimeoutSeconds { get; set; } = 120;
    /// <summary>Inatividade depois do login no game server (jogador parado no lobby). A conexão de login autenticada não expira.</summary>
    public int SessionIdleTimeoutSeconds { get; set; } = 900;

    internal void Validate()
    {
        if (MaxConnectionsPerIp < 1 || MaxPacketsPerSecond < 1 || MaxPacketSize < 16 || MaxLoginAttemptsPerMinute < 1 || IdleTimeoutSeconds < 5)
            throw new InvalidDataException("Limits: valores inválidos");
    }
}

/// <summary>
/// Papel Shop (봉다리) e raspadinha: o cliente não tem tabela de prêmios, então tudo vem daqui
/// (docs/protocolo/SPEC-papel-raspadinha.md §1.5 e §2.6). Pools padrão = silhuetas do cliente KR.
/// </summary>
public sealed class LotteryConfig
{
    /// <summary>Preço de uma jogada do Papel Shop sem cupom.</summary>
    public long PapelPrice { get; set; } = 900;
    /// <summary>Peso de sair 1, 2, 3, 4 e 5 bolas.</summary>
    public int[] PapelBallWeights { get; set; } = [35, 30, 20, 10, 5];
    /// <summary>Chance (em 1000) de cada bola ser de cookie e rara.</summary>
    public int PapelCookiePerMille { get; set; } = 140;
    public int PapelRarePerMille { get; set; } = 10;
    /// <summary>Peso de a raspadinha dar 0, 1 e 2 itens.</summary>
    public int[] ScratchCountWeights { get; set; } = [30, 69, 1];
    public int ScratchCookiePerMille { get; set; } = 180;
    public int ScratchRarePerMille { get; set; } = 20;

    public PrizeConfig[] Normal { get; set; } =
    [
        new(0x18000008, 11), new(0x18000007, 11), new(0x18000001, 11), new(0x18000000, 13), new(0x18000004, 12),
        new(0x18000005, 11), new(0x1A000028, 12, 1, 1), new(0x1A00003D, 9, 1, 1), new(0x1A000041, 10, 1, 1),
    ];
    public PrizeConfig[] Cookie { get; set; } =
    [
        new(0x1800000E, 7), new(0x1800000B, 7), new(0x1800000A, 7), new(0x18000009, 7), new(0x18000006, 6),
        new(0x1A00004F, 7), new(0x1A000002, 5), new(0x1A000011, 5), new(0x14000005, 5), new(0x14000003, 5),
        new(0x14000002, 5), new(0x14000001, 5), new(0x14000020, 4), new(0x18000010, 5), new(0x18000011, 5),
        new(0x18000012, 5), new(0x1A000040, 4), new(0x18000028, 3), new(0x18000027, 3),
    ];
    /// <summary>Raros (peças, club sets...): vazio = a chance de raro vira item de cookie.</summary>
    public PrizeConfig[] Rare { get; set; } = [];
}

/// <summary>Um prêmio possível: typeid, peso e quantidade (sorteada entre Min e Max; itens não empilháveis = 1).</summary>
public sealed class PrizeConfig
{
    public PrizeConfig() { }
    public PrizeConfig(int typeId, int weight, int min = 1, int max = 3) { TypeId = typeId; Weight = weight; Min = min; Max = max; }
    public int TypeId { get; set; }
    public int Weight { get; set; } = 1;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 3;
}
