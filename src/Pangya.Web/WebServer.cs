using System.Net;
using System.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pangya.Core.Logging;
using Pangya.Data;
using Pangya.Domain.Accounts;

namespace Pangya.Web;

/// <summary>
/// Servidor HTTP:
/// - POST /Secure/Login/LoginForGame.aspx: login do cliente KR (multipart id/pwd/gamecode), responde XML com o AuthKey;
/// - GET/POST /register: página de cadastro de conta.
/// </summary>
public static class WebServer
{
    public const string LoginPath = "/Secure/Login/LoginForGame.aspx";

    public static WebApplication Build(ServerServices services, int? portOverride = null)
    {
        var cfg = services.Config;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Parse(cfg.Network.BindIp), portOverride ?? cfg.Web.Port);
            k.Limits.MaxRequestBodySize = 8 * 1024;
            k.Limits.MaxRequestHeadersTotalSize = 8 * 1024;
            k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            k.Limits.MaxConcurrentConnections = 1000;
            k.AddServerHeader = false;
        });
        var app = builder.Build();

        app.MapPost(LoginPath, async (HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var form = ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;
            string login = form?["id"].ToString() ?? "", pwd = form?["pwd"].ToString() ?? "";
            var (status, acc) = await services.AccountService.AuthenticateAsync(login, pwd, ip);
            Log.Info($"WEB login id={login} ip={ip}: {status}");
            if (status != AuthStatus.Ok) return Xml(LoginXml.Failure(status, acc));
            var key = await services.Sessions.IssueWebAuthAsync(acc!.Id);
            return Xml(LoginXml.Success(key, acc.Id));
        });

        // emblema da guilda (SPEC-guilda.md §4): upload do cliente (multipart) e download por nome
        string markDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cfg.Data.IffPath)) ?? ".", "GuildMark");
        app.MapPost("/Guild/upload.asp", async (HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var form = ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;
            var file = form?.Files.GetFile("FILENAME");
            int.TryParse(form?["EMBLEM_IDX"].ToString(), out int idx);
            int.TryParse(form?["GUILD_IDX"].ToString(), out int guild);
            long.TryParse(form?["UID"].ToString(), out long uid);
            string mark = form?["EMBLEM"].ToString() ?? "";
            var t = idx > 0 ? await services.Guilds.EmblemTicketAsync(idx) : null;
            bool ok = file != null && t is { } tk && tk.GuildId == guild && tk.AccountId == uid && tk.Mark == mark && !tk.Uploaded
                && Pangya.Domain.Guilds.GuildService.ValidMarkName(mark);
            byte[] png = [];
            if (ok)
            {
                using var ms = new MemoryStream();
                await file!.CopyToAsync(ms);
                png = ms.ToArray();
                ok = Pangya.Domain.Guilds.GuildService.ValidEmblemPng(png);
            }
            Log.Info($"WEB emblema idx={idx} guilda={guild} uid={uid} '{mark}' ip={ip}: {(ok ? "ok" : "recusado")}");
            if (!ok) return Results.Content("PANGYA_UPDATE_FAIL", "text/plain");
            Directory.CreateDirectory(markDir);
            await File.WriteAllBytesAsync(Path.Combine(markDir, mark + ".png"), png);
            await services.Guilds.MarkEmblemUploadedAsync(idx);
            return Results.Content("PANGYA_UPDATE_OK", "text/plain");
        });
        app.MapGet("/_Files/GuildMark/{name}", (string name) =>
        {
            string mark = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            string path = Path.Combine(markDir, mark + ".png");
            return Pangya.Domain.Guilds.GuildService.ValidMarkName(mark) && File.Exists(path)
                ? Results.File(path, "image/png") : Results.NotFound();
        });

        app.MapGet("/register", () => Html(RegisterPage.Render(null, false)));
        app.MapPost("/register", async (HttpContext ctx) =>
        {
            if (!ctx.Request.HasFormContentType) return Results.BadRequest();
            var form = await ctx.Request.ReadFormAsync();
            string login = form["login"].ToString(), pw = form["password"].ToString(), pw2 = form["password2"].ToString();
            if (pw != pw2) return Html(RegisterPage.Render("As senhas não conferem.", false));
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var (status, _) = await services.AccountService.RegisterAsync(login, pw, ip);
            Log.Info($"WEB cadastro login={login} ip={ip}: {status}");
            return Html(RegisterPage.Render(status switch
            {
                RegisterStatus.Ok => $"Conta \"{login}\" criada! Já pode entrar no jogo.",
                RegisterStatus.InvalidLogin => "Login inválido: use de 3 a 20 letras, números ou _.",
                RegisterStatus.InvalidPassword => $"A senha precisa ter de {AccountRules.MinPassword} a {AccountRules.MaxPassword} caracteres.",
                RegisterStatus.LoginTaken => "Esse login já existe.",
                _ => "Tentativas demais. Espere um minuto.",
            }, status == RegisterStatus.Ok));
        });
        return app;
    }

    static IResult Xml(string xml) => Results.Content(xml, "text/xml");
    static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

    public static async Task RunAsync(ServerServices services, CancellationToken ct)
    {
        await using var app = Build(services);
        Log.Info($"WEB: escutando em {services.Config.Network.BindIp}:{services.Config.Web.Port}" +
                 (services.Config.Web.AutoRegister ? " (AUTO-CADASTRO LIGADO: só para testes!)" : ""));
        await app.StartAsync(ct);
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        await app.StopAsync();
    }
}

/// <summary>Resposta XML que o cliente KR espera (cHttpNtreevAuthResult, logininfo.cpp).</summary>
public static class LoginXml
{
    public static string Success(string authKey, long memberNo) =>
        "<?xml version=\"1.0\"?><response><result>true</result><messages>OK</messages>" +
        $"<arg>AuthKey={authKey}|MemberNo={memberNo}|PCBangNo=0</arg></response>";

    public static string Failure(AuthStatus status, Account? acc) =>
        $"<?xml version=\"1.0\"?><response><result>false</result><messages>{SecurityElement.Escape(Message(status, acc))}</messages></response>";

    // O cliente converte <messages> de UTF-8 para a codepage OEM do Windows (logininfo.cpp:449, CP_OEMCP):
    // fora de um Windows coreano, só ASCII sobrevive. Por isso: português sem acentos.
    static string Message(AuthStatus status, Account? acc) => status switch
    {
        AuthStatus.Blocked => "Conta bloqueada." + (acc?.BlockReason is { } r ? " Motivo: " + Ascii(r) : ""),
        AuthStatus.TooManyAttempts => "Tentativas demais. Espere 1 minuto.",
        _ => "ID ou senha incorretos.",
    };

    /// <summary>Remove acentos e o que não for ASCII visível.</summary>
    static string Ascii(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (c is >= ' ' and <= '~') sb.Append(c);
        return sb.ToString();
    }
}

static class RegisterPage
{
    public static string Render(string? message, bool ok) => $$"""
        <!doctype html>
        <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>PangYa — Criar conta</title>
        <style>
          body { font-family: system-ui, sans-serif; background: #e8f4ff; color: #123; display: flex; justify-content: center; padding: 40px 16px; }
          form { background: #fff; padding: 24px 28px; border-radius: 12px; box-shadow: 0 4px 18px #0002; width: 100%; max-width: 340px; }
          h1 { font-size: 22px; margin: 0 0 16px; }
          label { display: block; margin: 12px 0 4px; font-size: 14px; }
          input { width: 100%; box-sizing: border-box; padding: 9px; border: 1px solid #9bc; border-radius: 6px; font-size: 15px; }
          button { margin-top: 18px; width: 100%; padding: 10px; border: 0; border-radius: 6px; background: #1a7ad8; color: #fff; font-size: 16px; cursor: pointer; }
          .msg { padding: 10px; border-radius: 6px; margin-bottom: 8px; background: {{(ok ? "#dff5df" : "#fde2e2")}}; }
          small { color: #567; }
        </style></head>
        <body><form method="post" action="/register">
          <h1>Criar conta</h1>
          {{(message == null ? "" : $"<div class=\"msg\">{SecurityElement.Escape(message)}</div>")}}
          <label>Login</label><input name="login" maxlength="20" pattern="[A-Za-z0-9_]{3,20}" required autocomplete="username">
          <small>3 a 20 letras, números ou _</small>
          <label>Senha</label><input name="password" type="password" minlength="{{AccountRules.MinPassword}}" maxlength="{{AccountRules.MaxPassword}}" required autocomplete="new-password">
          <label>Repita a senha</label><input name="password2" type="password" required autocomplete="new-password">
          <small>O jogo envia a senha sem criptografia: não use a mesma senha de outros sites.</small>
          <button type="submit">Criar conta</button>
        </form></body></html>
        """;
}
