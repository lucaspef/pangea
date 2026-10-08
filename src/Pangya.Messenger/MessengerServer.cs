using System.Net;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Data;
using Pangya.Domain.Game;
using Pangya.Domain.Messenger;
using Pangya.Domain.Servers;
using Pangya.Protocol.KR645.Messenger;

namespace Pangya.Messenger;

/// <summary>
/// Mensageiro (cliente KR 645): escuta a porta e se anuncia no registro como "messenger" (o login manda a lista no 0x09
/// e o game no 0xFA). Precisa do mundo do game server do mesmo processo para confirmar quem está jogando.
/// </summary>
public sealed class MessengerServer
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    readonly ServerServices services;
    public MessengerContext Context { get; }
    public TcpServer Tcp { get; }

    public MessengerServer(ServerServices s, GameWorld world, int? portOverride = null)
    {
        services = s;
        var cfg = s.Config;
        Context = new MessengerContext(new FriendService(s.Friends, cfg.Messenger.MaxFriends), uid => world.Find(uid), cfg.Game.Id);
        Tcp = new TcpServer("MSN", new IPEndPoint(IPAddress.Parse(cfg.Network.BindIp), portOverride ?? cfg.Messenger.Port), cfg.Limits,
            c => new MessengerHandler(c, Context));
    }

    ServerInfo Info => new(services.Config.Messenger.Id, "messenger", services.Config.Messenger.Name, services.Config.Network.PublicIp,
        Tcp.Port, services.Config.Messenger.MaxUsers, Context.OnlineCount, 0);

    public async Task RunAsync(CancellationToken ct)
    {
        var accept = Tcp.StartAsync(ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await services.Registry.HeartbeatAsync(Info, HeartbeatInterval * 3); }
                catch (Exception e) { Log.Warn($"MSN: falha ao renovar registro: {e.Message}"); }
                await Task.Delay(HeartbeatInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await services.Registry.RemoveAsync(services.Config.Messenger.Id); } catch { /* banco fora: o registro expira sozinho */ }
            await accept;
        }
    }
}
