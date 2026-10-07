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
    public NetworkConfig Network { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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

public sealed class DatabaseConfig
{
    public string ConnectionString { get; set; } = "";
}

/// <summary>Limites contra abuso.</summary>
public sealed class LimitsConfig
{
    public int MaxConnectionsPerIp { get; set; } = 10;
    public int MaxPacketsPerSecond { get; set; } = 100;
    public int MaxPacketSize { get; set; } = 8192;
    public int MaxLoginAttemptsPerMinute { get; set; } = 10;
    public int IdleTimeoutSeconds { get; set; } = 120;

    internal void Validate()
    {
        if (MaxConnectionsPerIp < 1 || MaxPacketsPerSecond < 1 || MaxPacketSize < 16 || MaxLoginAttemptsPerMinute < 1 || IdleTimeoutSeconds < 5)
            throw new InvalidDataException("Limits: valores inválidos");
    }
}
