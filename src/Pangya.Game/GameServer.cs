using System.Net;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Data;
using Pangya.Domain.Game;
using Pangya.Domain.Players;
using Pangya.Domain.Servers;
using Pangya.Protocol.KR645;
using Pangya.Protocol.KR645.Game;

namespace Pangya.Game;

/// <summary>Game server (cliente KR 645): escuta a porta, e se anuncia no registro de servidores a cada 10 s.</summary>
public sealed class GameServer
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    readonly ServerServices services;
    public GameWorld World { get; }
    public TcpServer Tcp { get; }

    public GameServer(ServerServices s, int? portOverride = null)
    {
        services = s;
        var cfg = s.Config;
        World = new GameWorld(cfg.Game);
        var data = Kr645GameData.Load(cfg.Data.IffPath);
        World.Rooms.Courses = cfg.Game.Courses.Length > 0 ? Bytes(cfg.Game.Courses) : data.Courses;
        var ctx = new GameContext(World, s.Sessions, new PlayerService(s.Players, data, cfg.NewPlayer), data, cfg.Lottery, s.Registry);
        Tcp = new TcpServer("GAME", new IPEndPoint(IPAddress.Parse(cfg.Network.BindIp), portOverride ?? cfg.Game.Port), cfg.Limits,
            c => new GameHandler(c, ctx));
    }

    static byte[] Bytes(int[] v)
    {
        var b = new byte[v.Length];
        for (int i = 0; i < v.Length; i++) b[i] = (byte)v[i];
        return b;
    }

    ServerInfo Info => new(services.Config.Game.Id, "game", services.Config.Game.Name, services.Config.Network.PublicIp,
        Tcp.Port, services.Config.Game.MaxUsers, World.OnlineCount, 0);

    public async Task RunAsync(CancellationToken ct)
    {
        var accept = Tcp.StartAsync(ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await services.Registry.HeartbeatAsync(Info, HeartbeatInterval * 3); }
                catch (Exception e) { Log.Warn($"GAME: falha ao renovar registro: {e.Message}"); }
                await Task.Delay(HeartbeatInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await services.Registry.RemoveAsync(services.Config.Game.Id); } catch { /* banco fora: o registro expira sozinho */ }
            await accept;
        }
    }
}
