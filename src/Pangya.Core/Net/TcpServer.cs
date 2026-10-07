using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Pangya.Core.Config;
using Pangya.Core.Limits;
using Pangya.Core.Logging;

namespace Pangya.Core.Net;

/// <summary>
/// Servidor TCP genérico: aceita conexões (com limite por IP), cria o handler de cada uma e roda tudo de
/// forma assíncrona. O protocolo em si fica no <see cref="IConnectionHandler"/>.
/// </summary>
public sealed class TcpServer(string name, IPEndPoint endPoint, LimitsConfig limits, Func<Connection, IConnectionHandler> handlerFactory)
{
    readonly IpConnectionLimiter perIp = new(limits.MaxConnectionsPerIp);
    readonly ConcurrentDictionary<int, Connection> connections = new();
    Socket? listener;

    public string Name => name;
    public int Port => ((IPEndPoint?)listener?.LocalEndPoint)?.Port ?? endPoint.Port;
    public IEnumerable<Connection> Connections => connections.Values;

    /// <summary>Começa a escutar (porta 0 = qualquer porta livre, útil em testes) e devolve a tarefa do laço de aceite.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Bind(endPoint);
        listener.Listen(512);
        Log.Info($"{name}: escutando em {listener.LocalEndPoint}");
        return AcceptLoopAsync(ct);
    }

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        using var reg = ct.Register(() => listener!.Dispose());
        while (!ct.IsCancellationRequested)
        {
            Socket s;
            try { s = await listener!.AcceptAsync(ct); }
            catch (Exception) when (ct.IsCancellationRequested) { break; }
            catch (SocketException e) { Log.Warn($"{name}: erro no accept: {e.SocketErrorCode}"); continue; }

            var ip = ((IPEndPoint)s.RemoteEndPoint!).Address;
            if (!perIp.TryAcquire(ip))
            {
                Log.Warn($"{name}: recusada conexão de {ip} (limite de {limits.MaxConnectionsPerIp} por IP)");
                s.Dispose();
                continue;
            }
            _ = RunConnectionAsync(s, ip);
        }
        foreach (var c in connections.Values) c.Close("servidor parando");
    }

    async Task RunConnectionAsync(Socket s, IPAddress ip)
    {
        var conn = new Connection(s, name, limits);
        connections[conn.Id] = conn;
        try
        {
            Log.Info($"{conn} conectou");
            conn.Handler = handlerFactory(conn);
            await conn.RunAsync();
        }
        catch (Exception e) { Log.Error($"{conn} erro", e); }
        finally
        {
            connections.TryRemove(conn.Id, out _);
            perIp.Release(ip);
        }
    }
}
