// Pangya.Server — roda os servidores num processo.
//   Pangya.Server [--config arquivo.json] [web] [login] ...       (sem nomes: usa "Run" da configuração)
//   Pangya.Server [--config ...] server-add <id> <tipo> <nome> <endereço> <porta> [máx]   servidor fixo na lista
//   Pangya.Server [--config ...] server-remove <id>
using Pangya.Core.Hosting;
using Pangya.Core.Logging;
using Pangya.Data;
using Pangya.Domain.Servers;
using Pangya.Login;
using Pangya.Web;

string? configPath = null;
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--config" && i + 1 < args.Length) configPath = args[++i];
    else rest.Add(args[i]);
}

return await ServerHost.RunAsync("pangya", configPath, async (cfg, ct) =>
{
    await using var s = new ServerServices(cfg);
    var applied = await Migrator.RunAsync(s.Db, ct);
    if (applied.Count > 0) Log.Info("migrações aplicadas: " + string.Join(", ", applied));

    if (rest.FirstOrDefault() == "server-add" && rest.Count >= 6)
    {
        var info = new ServerInfo(int.Parse(rest[1]), rest[2], rest[3], rest[4], int.Parse(rest[5]), rest.Count > 6 ? int.Parse(rest[6]) : 3000, 0, 0);
        await s.Registry.HeartbeatAsync(info, TimeSpan.FromDays(365 * 100));
        Log.Info($"servidor fixo registrado: {info}");
        return;
    }
    if (rest.FirstOrDefault() == "server-remove" && rest.Count == 2)
    {
        await s.Registry.RemoveAsync(int.Parse(rest[1]));
        Log.Info($"servidor {rest[1]} removido");
        return;
    }

    var names = rest.Count > 0 ? rest.ToArray() : cfg.Run;
    await Task.WhenAll(names.Select(n => n switch
    {
        "web" => WebServer.RunAsync(s, ct),
        "login" => LoginServer.RunAsync(s, ct),
        _ => throw new ArgumentException($"servidor desconhecido: {n} (use web, login)"),
    }));
});
