// Pangya.Server — roda os servidores num processo.
//   Pangya.Server [--config arquivo.json] [web] [login] ...       (sem nomes: usa "Run" da configuração)
//   Pangya.Server [--config ...] server-add <id> <tipo> <nome> <endereço> <porta> [máx]   servidor fixo na lista
//   Pangya.Server [--config ...] server-remove <id>
//   Pangya.Server [--config ...] account-create <login> <senha> <nickname>              conta pronta para jogar
//   Pangya.Server [--config ...] player-set <login> [pang=N] [cookie=N] [level=N] [identity=N]   ajusta um jogador (desconectado)
//   Pangya.Server [--config ...] item-give <login> <typeid> [qtd] [dias]   entrega um item sem cobrar (desconectado)
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

    if (command == "player-set" && rest.Count >= 3)
    {
        // administração: muda pang/cookie/nível de um jogador (ele deve estar desconectado, senão a sessão aberta sobrescreve)
        var acc = await s.Accounts.FindByLoginAsync(rest[1]) ?? throw new InvalidOperationException($"login {rest[1]} não existe");
        var p = await s.Players.LoadAsync(acc.Id) ?? throw new InvalidOperationException($"{rest[1]} ainda não criou o personagem");
        var ch = new Pangya.Domain.Players.PlayerChanges();
        int? identity = null;
        for (int i = 2; i < rest.Count; i++)
        {
            var kv = rest[i].Split('=', 2);
            long v = kv.Length == 2 && (kv[1].StartsWith("0x") ? long.TryParse(kv[1][2..], System.Globalization.NumberStyles.HexNumber, null, out var n) : long.TryParse(kv[1], out n)) && n >= 0
                ? n : throw new ArgumentException($"valor inválido: {rest[i]}");
            switch (kv[0])
            {
                case "pang": ch.Pang = v; break;
                case "cookie": ch.Cookie = v; break;
                case "level": ch.Level = (int)Math.Min(v, Pangya.Domain.Players.Levels.Max); ch.Exp = 0; break;
                case "identity": identity = (int)v; break;     // 0x14 = GM, 0x1E = GM + admin (aceita 0x...)
                default: throw new ArgumentException($"campo desconhecido: {kv[0]} (use pang, cookie, level, identity)");
            }
        }
        await s.Players.ApplyAsync(acc.Id, ch);
        if (identity is { } id) await s.Accounts.SetIdentityFlagsAsync(acc.Id, id);
        Log.Info($"{rest[1]}: pang={ch.Pang ?? p.Pang} cookie={ch.Cookie ?? p.Cookie} nível={ch.Level ?? p.Level} identidade=0x{identity ?? acc.IdentityFlags:X}");
        return;
    }

    if (command == "item-give" && rest.Count is 3 or 4 or 5)
    {
        // administração: entrega um item (typeid do IFF, aceita 0x...) sem cobrar; jogador desconectado, como no player-set
        var acc = await s.Accounts.FindByLoginAsync(rest[1]) ?? throw new InvalidOperationException($"login {rest[1]} não existe");
        var data = Pangya.Protocol.KR645.Kr645GameData.Load(cfg.Data.IffPath);
        var players = new Pangya.Domain.Players.PlayerService(s.Players, data, cfg.NewPlayer);
        var p = await players.LoadAsync(acc.Id) ?? throw new InvalidOperationException($"{rest[1]} ainda não criou o personagem");
        static int Num(string v) => v.StartsWith("0x") ? int.Parse(v[2..], System.Globalization.NumberStyles.HexNumber) : int.Parse(v);
        var (code, granted) = await new Pangya.Domain.Shop.ShopService(s.Players, data)
            .GiveAsync(p, Num(rest[2]), rest.Count > 3 ? Num(rest[3]) : 1, rest.Count > 4 ? Num(rest[4]) : 0);
        Log.Info($"{rest[1]}: item-give {rest[2]} -> {code} " + string.Join(", ", granted));
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
