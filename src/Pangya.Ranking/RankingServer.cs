using System.Net;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Data;
using Pangya.Domain.Game;
using Pangya.Domain.Ranking;
using Pangya.Domain.Servers;
using Pangya.Protocol.KR645.Ranking;

namespace Pangya.Ranking;

/// <summary>
/// Servidor de ranking (cliente KR 645): escuta a porta, calcula o retrato ao subir e todo dia na hora configurada, e se
/// anuncia no registro como "ranking" (informativo: o cliente recebe o endereço do game, no 0xA0).
/// </summary>
public sealed class RankingServer
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    readonly ServerServices services;
    public RankingContext Context { get; }
    public TcpServer Tcp { get; }

    public RankingServer(ServerServices s, GameWorld world, int? portOverride = null)
    {
        services = s;
        var cfg = s.Config;
        Context = new RankingContext(new RankingService(s.Ranking), uid => world.Find(uid), s.Players, cfg.Network.PublicIp, cfg.Ranking.Port);
        Tcp = new TcpServer("RANK", new IPEndPoint(IPAddress.Parse(cfg.Network.BindIp), portOverride ?? cfg.Ranking.Port), cfg.Limits,
            c => new RankingHandler(c, Context));
    }

    /// <summary>Começa a escutar (a porta real vai para o 0xA0) e calcula o primeiro retrato.</summary>
    public async Task<Task> StartAsync(CancellationToken ct)
    {
        var accept = Tcp.StartAsync(ct);
        Context.Port = Tcp.Port;
        await RefreshAsync();
        return accept;
    }

    public async Task RefreshAsync()
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Context.Ranking.RefreshAsync();
            Log.Info($"RANK: retrato com {Context.Ranking.Current?.Players.Count ?? 0} jogadores em {t.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { Log.Error("RANK: falha ao recalcular o ranking", e); }
    }

    ServerInfo Info => new(services.Config.Ranking.Id, "ranking", services.Config.Ranking.Name, services.Config.Network.PublicIp,
        Tcp.Port, 3000, 0, 0);

    public async Task RunAsync(CancellationToken ct)
    {
        var accept = await StartAsync(ct);
        var next = NextRefresh(DateTime.Now);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await services.Registry.HeartbeatAsync(Info, HeartbeatInterval * 3); }
                catch (Exception e) { Log.Warn($"RANK: falha ao renovar registro: {e.Message}"); }
                if (DateTime.Now >= next)
                {
                    await RefreshAsync();
                    next = NextRefresh(DateTime.Now);
                }
                await Task.Delay(HeartbeatInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await services.Registry.RemoveAsync(services.Config.Ranking.Id); } catch { /* banco fora: o registro expira sozinho */ }
            await accept;
        }
    }

    DateTime NextRefresh(DateTime now)
    {
        var at = now.Date.AddHours(Math.Clamp(services.Config.Ranking.RefreshHour, 0, 23));
        return at > now ? at : at.AddDays(1);
    }
}
