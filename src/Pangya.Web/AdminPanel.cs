using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Pangya.Core.Logging;
using Pangya.Data;
using Pangya.Domain.Accounts;
using Pangya.Domain.Game;
using Pangya.Domain.Players;

namespace Pangya.Web;

/// <summary>
/// Painel de administração (fase 9) em /admin: procurar conta, bloquear/desbloquear, ajustar pang/cookie/nível, entregar
/// item, expulsar e ver a auditoria. Só com Web.AdminEnabled, só dos IPs de Web.AdminAllowedIps e só para contas GM.
/// Sessão por cookie HttpOnly/SameSite=Strict e token anti-CSRF em todo POST; tudo vai para a auditoria. Mudanças no
/// jogador exigem que ele esteja desconectado (a sessão de jogo aberta sobrescreveria).
/// </summary>
public sealed class AdminPanel(ServerServices s, GameWorld? world)
{
    const string Cookie = "pyadm", Base = "/admin";
    const int GmFlag = 0x04;
    static readonly TimeSpan SessionTtl = TimeSpan.FromHours(8);

    sealed record Session(long AccountId, string Name, string Csrf, DateTime Expires);
    readonly ConcurrentDictionary<string, Session> sessions = new();
    Pangya.Protocol.KR645.Kr645GameData? data;
    Pangya.Protocol.KR645.Kr645GameData Data => data ??= Pangya.Protocol.KR645.Kr645GameData.Load(s.Config.Data.IffPath);

    public void Map(WebApplication app)
    {
        // Delegate explícito: um lambda (HttpContext) => Task<IResult> viraria RequestDelegate e o resultado se perderia
        app.MapGet(Base, (Delegate)HomeAsync);
        app.MapPost(Base + "/login", (Delegate)LoginAsync);
        app.MapPost(Base + "/logout", (Delegate)LogoutAsync);
        app.MapGet(Base + "/player", (Delegate)((HttpContext c) => Guard(c, ses => PlayerAsync(ses, c.Request.Query["q"].ToString(), null))));
        app.MapGet(Base + "/audit", (Delegate)AuditPageAsync);
        app.MapPost(Base + "/action", (Delegate)((HttpContext c) => Guard(c, ses => ActionAsync(c, ses), post: true)));
    }

    Task<IResult> HomeAsync(HttpContext c) =>
        Guard(c, async ses => Page(ses, "Painel", SearchForm(ses, "") + "<h2>Últimas ações</h2>" + await AuditAsync(null, 30)));

    Task<IResult> LogoutAsync(HttpContext c) => Guard(c, ses =>
    {
        sessions.TryRemove(c.Request.Cookies[Cookie] ?? "", out _);
        c.Response.Cookies.Delete(Cookie, new CookieOptions { Path = Base });
        return Task.FromResult(Results.Redirect(Base));
    }, post: true);

    Task<IResult> AuditPageAsync(HttpContext c) => Guard(c, async ses =>
    {
        var q = c.Request.Query["q"].ToString();
        return Page(ses, "Auditoria", Form(ses, "get", "/audit", Input("q", "Filtro", q) + Button("Filtrar")) + await AuditAsync(q));
    });

    // ------------------------------------------------------------------ acesso

    bool Allowed(HttpContext c)
    {
        var ip = c.Connection.RemoteIpAddress;
        if (!s.Config.Web.AdminEnabled || ip == null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        foreach (var a in s.Config.Web.AdminAllowedIps)
            if (IPAddress.TryParse(a, out var allowed) && allowed.Equals(ip)) return true;
        return false;
    }

    Session? Current(HttpContext c)
    {
        var token = c.Request.Cookies[Cookie];
        if (token == null || !sessions.TryGetValue(token, out var ses)) return null;
        if (ses.Expires < DateTime.UtcNow) { sessions.TryRemove(token, out _); return null; }
        return ses;
    }

    /// <summary>IP liberado, sessão válida e (POST) token anti-CSRF certo; senão 404 / tela de login / 400.</summary>
    async Task<IResult> Guard(HttpContext c, Func<Session, Task<IResult>> run, bool post = false)
    {
        if (!Allowed(c)) return Results.NotFound();
        var ses = Current(c);
        if (ses == null) return post ? Results.Redirect(Base) : LoginPage(null);
        if (post)
        {
            var form = c.Request.HasFormContentType ? await c.Request.ReadFormAsync() : null;
            var sent = Encoding.UTF8.GetBytes(form?["csrf"].ToString() ?? "");
            if (!CryptographicOperations.FixedTimeEquals(sent, Encoding.UTF8.GetBytes(ses.Csrf))) return Results.BadRequest();
        }
        return await run(ses);
    }

    async Task<IResult> LoginAsync(HttpContext c)
    {
        if (!Allowed(c)) return Results.NotFound();
        var form = c.Request.HasFormContentType ? await c.Request.ReadFormAsync() : null;
        string login = form?["login"].ToString() ?? "", pwd = form?["password"].ToString() ?? "";
        var ip = c.Connection.RemoteIpAddress?.ToString() ?? "?";
        var (status, acc) = await s.AccountService.AuthenticateAsync(login, pwd, ip);
        if (status != AuthStatus.Ok || acc == null || (acc.IdentityFlags & GmFlag) == 0)
        {
            Log.Warn($"WEB admin: login recusado {login} ip={ip} ({status}{(acc != null && status == AuthStatus.Ok ? ", não é GM" : "")})");
            return LoginPage("Login recusado.");
        }
        var now = DateTime.UtcNow;                                             // limpa as sessões vencidas
        foreach (var (k, v) in sessions) if (v.Expires < now) sessions.TryRemove(k, out _);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var name = acc.Nickname ?? acc.Login;
        sessions[token] = new Session(acc.Id, name, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), DateTime.UtcNow + SessionTtl);
        c.Response.Cookies.Append(Cookie, token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Path = Base, MaxAge = SessionTtl, Secure = c.Request.IsHttps,
        });
        await s.Audit.WriteAsync(acc.Id, name, "admin:login", "", ip);
        return Results.Redirect(Base);
    }

    // ------------------------------------------------------------------ jogador

    async Task<Account?> FindAsync(string q)
    {
        q = q.Trim();
        if (q.Length == 0) return null;
        if (q.StartsWith('#') && long.TryParse(q[1..], out long id)) return await s.Accounts.FindByIdAsync(id);
        return await s.Accounts.FindByLoginAsync(q) ?? await s.Accounts.FindByNicknameAsync(q);
    }

    bool Online(long id) => world?.Find(id) != null;

    async Task<IResult> PlayerAsync(Session ses, string q, string? message)
    {
        var acc = await FindAsync(q);
        var sb = new StringBuilder(SearchForm(ses, q));
        if (message != null) sb.Append($"<p class=\"msg\">{E(message)}</p>");
        if (acc == null) return Page(ses, "Jogador", sb.Append(q.Length > 0 ? "<p>Conta não encontrada.</p>" : "").ToString());
        var p = await s.Players.LoadAsync(acc.Id);
        bool online = Online(acc.Id);
        sb.Append("<table>");
        Row(sb, "Conta", $"#{acc.Id} {acc.Login}");
        Row(sb, "Nick", acc.Nickname ?? "(sem nick)");
        Row(sb, "Identidade", $"0x{acc.IdentityFlags:X}" + ((acc.IdentityFlags & GmFlag) != 0 ? " (GM)" : ""));
        Row(sb, "Bloqueio", acc.IsBlocked(DateTime.UtcNow) ? $"até {acc.BlockedUntil:yyyy-MM-dd HH:mm} UTC: {acc.BlockReason}" : "não");
        Row(sb, "Online", online ? "sim" : world == null ? "desconhecido (sem game neste processo)" : "não");
        if (p != null)
        {
            Row(sb, "Nível / EXP", $"{p.Level} / {p.Exp}");
            Row(sb, "Pang / Cookie", $"{p.Pang} / {p.Cookie}");
            Row(sb, "Objetos", p.Items.Count.ToString());
        }
        sb.Append("</table>");
        string id = acc.Id.ToString(), back = Hidden("q", q) + Hidden("id", id);
        sb.Append("<h2>Ações</h2>");
        sb.Append(Form(ses, "post", "/action", back + Hidden("op", "block") + Input("days", "Dias", "7") + Input("reason", "Motivo", "") + Button("Bloquear (e expulsar)")));
        sb.Append(Form(ses, "post", "/action", back + Hidden("op", "unblock") + Button("Desbloquear")));
        if (online) sb.Append(Form(ses, "post", "/action", back + Hidden("op", "kick") + Button("Expulsar do jogo")));
        if (p != null)
        {
            sb.Append(Form(ses, "post", "/action", back + Hidden("op", "set") + Input("pang", "Pang", p.Pang.ToString())
                + Input("cookie", "Cookie", p.Cookie.ToString()) + Input("level", "Nível", p.Level.ToString()) + Button("Salvar (desconectado)")));
            sb.Append(Form(ses, "post", "/action", back + Hidden("op", "give") + Input("tid", "Typeid (0x...)", "")
                + Input("qty", "Quantidade", "1") + Input("days", "Dias (0 = sem prazo)", "0") + Button("Entregar item (desconectado)")));
        }
        return Page(ses, "Jogador", sb.ToString());
    }

    async Task<IResult> ActionAsync(HttpContext c, Session ses)
    {
        var f = await c.Request.ReadFormAsync();
        string q = f["q"].ToString(), op = f["op"].ToString();
        if (!long.TryParse(f["id"], out long id) || await s.Accounts.FindByIdAsync(id) is not { } acc)
            return await PlayerAsync(ses, q, "Conta inválida.");
        string who = acc.Nickname ?? acc.Login, result;
        switch (op)
        {
            case "block":
                if ((acc.IdentityFlags & GmFlag) != 0) { result = "Não dá para bloquear um GM."; break; }
                int days = int.TryParse(f["days"], out int d) ? Math.Clamp(d, 1, 36500) : 7;
                string reason = f["reason"].ToString().Trim();
                if (reason.Length > 200) reason = reason[..200];
                await s.Accounts.SetBlockAsync(id, DateTime.UtcNow.AddDays(days), reason);
                world?.Find(id)?.Kick($"bloqueado pelo painel ({ses.Name})");
                await Audit(ses, "block", who, $"{days} dias: {reason}");
                result = $"Bloqueado por {days} dias.";
                break;
            case "unblock":
                await s.Accounts.SetBlockAsync(id, null, null);
                await Audit(ses, "unblock", who);
                result = "Desbloqueado.";
                break;
            case "kick":
                if (world?.Find(id) is { } g) { g.Kick($"expulso pelo painel ({ses.Name})"); await Audit(ses, "kick", who); result = "Expulso."; }
                else result = "Não está online.";
                break;
            case "set":
                if (Online(id)) { result = "Desconecte o jogador antes (a sessão aberta sobrescreveria)."; break; }
                if (!long.TryParse(f["pang"], out long pang) || !long.TryParse(f["cookie"], out long cookie) || !int.TryParse(f["level"], out int level)
                    || pang < 0 || cookie < 0 || level < 0)
                { result = "Valores inválidos."; break; }
                level = Math.Min(level, Levels.Max);
                await s.Players.ApplyAsync(id, new PlayerChanges { Pang = pang, Cookie = cookie, Level = level, Exp = 0 });
                await Audit(ses, "set", who, $"pang={pang} cookie={cookie} nível={level}");
                result = "Salvo.";
                break;
            case "give":
                if (Online(id)) { result = "Desconecte o jogador antes (a sessão aberta sobrescreveria)."; break; }
                result = await GiveAsync(ses, id, who, f["tid"].ToString(), f["qty"].ToString(), f["days"].ToString());
                break;
            default:
                result = "Ação desconhecida.";
                break;
        }
        return await PlayerAsync(ses, q, result);
    }

    async Task<string> GiveAsync(Session ses, long id, string who, string tidText, string qtyText, string daysText)
    {
        tidText = tidText.Trim();
        bool hex = tidText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!int.TryParse(hex ? tidText[2..] : tidText, hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer, null, out int tid)
            || !int.TryParse(qtyText, out int qty) || !int.TryParse(daysText, out int days) || qty is < 1 or > 9999 || days is < 0 or > 3650)
            return "Valores inválidos.";
        if (!Data.Exists(tid)) return $"Typeid 0x{tid:X8} não existe no pangya.iff.";
        var players = new PlayerService(s.Players, Data, s.Config.NewPlayer);
        if (await players.LoadAsync(id) is not { } p) return "Jogador sem personagem.";
        var (code, granted) = await new Pangya.Domain.Shop.ShopService(s.Players, Data).GiveAsync(p, tid, qty, days);
        await Audit(ses, "item-give", who, $"0x{tid:X8} x{qty} {days}d -> {code}");
        return code == Pangya.Domain.Shop.ShopCode.Ok ? $"Entregue ({granted.Count} objeto(s))." : $"Não entregue: {code}.";
    }

    Task Audit(Session ses, string action, string target, string details = "") =>
        s.Audit.WriteAsync(ses.AccountId, ses.Name, "admin:" + action, target, details);

    async Task<string> AuditAsync(string? q, int max = 200)
    {
        var list = await s.Audit.Store.RecentAsync(max, q);
        var sb = new StringBuilder("<table><tr><th>Quando (UTC)</th><th>Quem</th><th>Ação</th><th>Alvo</th><th>Detalhes</th></tr>");
        foreach (var e in list)
            sb.Append($"<tr><td>{e.At:yyyy-MM-dd HH:mm:ss}</td><td>{E(e.Actor)}</td><td>{E(e.Action)}</td><td>{E(e.Target)}</td><td>{E(e.Details)}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    // ------------------------------------------------------------------ HTML

    static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
    static void Row(StringBuilder sb, string k, string v) => sb.Append($"<tr><th>{E(k)}</th><td>{E(v)}</td></tr>");
    static string Hidden(string name, string value) => $"<input type=\"hidden\" name=\"{name}\" value=\"{E(value)}\">";
    static string Input(string name, string label, string value) =>
        $"<label>{E(label)} <input name=\"{name}\" value=\"{E(value)}\" maxlength=\"200\"></label> ";
    static string Button(string text) => $"<button type=\"submit\">{E(text)}</button>";
    static string Form(Session ses, string method, string action, string body) =>
        $"<form method=\"{method}\" action=\"{Base}{action}\">{(method == "post" ? Hidden("csrf", ses.Csrf) : "")}{body}</form>";
    static string SearchForm(Session ses, string q) =>
        Form(ses, "get", "/player", Input("q", "Login, nick ou #id", q) + Button("Procurar"));

    static IResult Page(Session ses, string title, string body) => Html($$"""
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>PangYa admin — {{E(title)}}</title>
        <style>
          body { font-family: system-ui, sans-serif; margin: 0; background: #f4f7fb; color: #123; }
          header { background: #1a3d6b; color: #fff; padding: 10px 18px; display: flex; gap: 18px; align-items: center; }
          header a { color: #cfe3ff; } main { padding: 16px 18px; }
          table { border-collapse: collapse; margin: 8px 0; background: #fff; } th, td { border: 1px solid #cdd; padding: 4px 8px; text-align: left; font-size: 14px; }
          form { margin: 8px 0; padding: 8px; background: #fff; border: 1px solid #dde; border-radius: 6px; }
          input { padding: 4px; } button { padding: 5px 10px; } .msg { background: #fff6d6; padding: 8px; border-radius: 6px; }
        </style></head><body>
        <header><b>PangYa admin</b><a href="/admin">painel</a><a href="/admin/audit">auditoria</a><span>{{E(ses.Name)}}</span>
        <form method="post" action="/admin/logout" style="margin:0;padding:0;border:0;background:none">{{Hidden("csrf", ses.Csrf)}}<button>Sair</button></form></header>
        <main><h1>{{E(title)}}</h1>{{body}}</main></body></html>
        """);

    static IResult LoginPage(string? message) => Html($$"""
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>PangYa admin</title></head>
        <body style="font-family:system-ui,sans-serif;padding:40px">
        <h1>PangYa admin</h1>{{(message == null ? "" : "<p>" + E(message) + "</p>")}}
        <form method="post" action="/admin/login"><label>Login <input name="login" autocomplete="username"></label>
        <label>Senha <input name="password" type="password" autocomplete="current-password"></label> <button>Entrar</button></form>
        </body></html>
        """);

    static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
}
