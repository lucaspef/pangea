using System.Net;
using Pangya.Core.Net;
using Pangya.Data;
using Pangya.Domain.Accounts;
using Pangya.Protocol.KR645.Login;

namespace Pangya.Login;

/// <summary>Sobe um TcpServer por porta de login configurada (cliente KR 645).</summary>
public static class LoginServer
{
    public static List<TcpServer> Create(ServerServices s, IEnumerable<int>? ports = null)
    {
        var cfg = s.Config;
        var ctx = new LoginContext(new LoginService(s.Accounts, s.Sessions), s.Registry, cfg.Login, cfg.Limits.MaxLoginAttemptsPerMinute);
        var ip = IPAddress.Parse(cfg.Network.BindIp);
        return (ports ?? cfg.Login.Ports).Select(port =>
            new TcpServer("LOGIN", new IPEndPoint(ip, port), cfg.Limits, c => new LoginHandler(c, ctx))).ToList();
    }

    public static Task RunAsync(ServerServices s, CancellationToken ct) =>
        Task.WhenAll(Create(s).Select(t => t.StartAsync(ct)));
}
