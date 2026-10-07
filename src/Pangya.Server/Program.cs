// Pangya.Server — roda os servidores num processo.
//   Pangya.Server [--config arquivo.json] [web] [login] ...       (sem nomes: usa "Run" da configuração)
//   Pangya.Server [--config ...] server-add <id> <tipo> <nome> <endereço> <porta> [máx]   servidor fixo na lista
//   Pangya.Server [--config ...] server-remove <id>
//   Pangya.Server [--config ...] account-create <login> <senha> <nickname>              conta pronta para jogar
using Pangya.Core.Hosting;
using Pangya.Core.Logging;
using Pangya.Data;
using Pangya.Domain.Servers;
using Pangya.Game;
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

    var command = rest.Count > 0 ? rest[0] : "";
    if (command == "server-add" && rest.Count >= 6)
    {
        var info = new ServerInfo(int.Parse(rest[1]), rest[2], rest[3], rest[4], int.Parse(rest[5]), rest.Count > 6 ? int.Parse(rest[6]) : 3000, 0, 0);
        await s.Registry.HeartbeatAsync(info, TimeSpan.FromDays(365 * 100));
        Log.Info($"servidor fixo registrado: {info}");
        return;
    }
    if (command == "server-remove" && rest.Count == 2)
    {
        await s.Registry.RemoveAsync(int.Parse(rest[1]));
        Log.Info($"servidor {rest[1]} removido");
        return;
    }

    if (command == "account-create" && rest.Count == 4)
    {
        // conta pronta (com nickname e personagem padrão), sem as regras de senha do cadastro: testes e administração
        var acc = await s.Accounts.CreateAsync(rest[1], Pangya.Domain.Accounts.PasswordHasher.Hash(rest[2]))
                  ?? throw new InvalidOperationException($"login {rest[1]} já existe");
        await s.Accounts.SetNicknameAsync(acc.Id, rest[3]);
        var players = new Pangya.Domain.Players.PlayerService(s.Players,
            Pangya.Protocol.KR645.Kr645GameData.Load(cfg.Data.IffPath), cfg.NewPlayer);
        await players.CreateAsync(acc.Id, 0x04000000, 0, 0);
        Log.Info($"conta criada: {rest[1]} uid={acc.Id} nick={rest[3]}");
        return;
    }

    var names = rest.Count > 0 ? rest.ToArray() : cfg.Run;
    var tasks = new Task[names.Length];
    for (int i = 0; i < names.Length; i++)
        tasks[i] = names[i] switch
        {
            "web" => WebServer.RunAsync(s, ct),
            "login" => LoginServer.RunAsync(s, ct),
            "game" => new GameServer(s).RunAsync(ct),
            _ => throw new ArgumentException($"servidor desconhecido: {names[i]} (use web, login, game)"),
        };
    await Task.WhenAll(tasks);
});
