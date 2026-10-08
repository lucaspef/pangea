using Pangya.Core.Config;
using Pangya.Domain.Accounts;
using Pangya.Domain.Auth;
using Pangya.Domain.Players;
using Pangya.Domain.Servers;

namespace Pangya.Data;

/// <summary>Serviços compartilhados pelos servidores de um processo (um pool de conexões com o banco).</summary>
public sealed class ServerServices : IAsyncDisposable
{
    public PangyaConfig Config { get; }
    public Db Db { get; }
    public IAccountStore Accounts { get; }
    public AccountService AccountService { get; }
    public SessionService Sessions { get; }
    public IServerRegistry Registry { get; }
    public IPlayerStore Players { get; }
    public Pangya.Domain.Guilds.IGuildStore Guilds { get; }

    public ServerServices(PangyaConfig config)
    {
        Config = config;
        Db = new Db(config.Database.ConnectionString);
        Accounts = new AccountRepository(Db);
        AccountService = new AccountService(Accounts, config.Limits.MaxLoginAttemptsPerMinute, config.Web.AutoRegister);
        Sessions = new SessionService(new SessionRepository(Db));
        Registry = new ServerRegistry(Db);
        Players = new PlayerRepository(Db);
        Guilds = new GuildRepository(Db);
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
