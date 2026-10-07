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
    /// <summary>Servidores que este processo roda (se a linha de comando não disser): web, login, game.</summary>
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
    public RewardConfig Rewards { get; set; } = new();
}

/// <summary>Recompensa de fim de partida (o pang informado pelo cliente é limitado por buraco).</summary>
public sealed class RewardConfig
{
    public int ExpPerHole { get; set; } = 2;
    public int MaxPangPerHole { get; set; } = 1000;
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
