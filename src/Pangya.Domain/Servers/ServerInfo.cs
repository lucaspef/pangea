namespace Pangya.Domain.Servers;

/// <summary>Um servidor online anunciado na lista do cliente.</summary>
public sealed record ServerInfo(int Id, string Kind, string Name, string Address, int Port, int MaxUsers, int CurUsers, int Flags);

/// <summary>Registro dos servidores online (implementado em Pangya.Data).</summary>
public interface IServerRegistry
{
    /// <summary>Cria/renova o registro; vale até agora + ttl.</summary>
    Task HeartbeatAsync(ServerInfo server, TimeSpan ttl);
    Task RemoveAsync(int id);
    Task<IReadOnlyList<ServerInfo>> ListAsync(string kind);
}
