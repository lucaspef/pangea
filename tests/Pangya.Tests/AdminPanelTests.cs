using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Pangya.Domain.Accounts;
using Pangya.Domain.Shop;
using Pangya.Web;

namespace Pangya.Tests;

/// <summary>Painel de administração (fase 9): /admin.</summary>
[Collection("db")]
public partial class AdminPanelTests(DbFixture fx)
{
    [GeneratedRegex("name=\"csrf\" value=\"([0-9A-F]+)\"")]
    private static partial Regex CsrfRegex();

    static FormUrlEncodedContent Form(params (string K, string V)[] f)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var (k, v) in f) list.Add(new(k, v));
        return new FormUrlEncodedContent(list);
    }

    [Fact]
    public async Task GmManagesPlayersAndEverythingIsAudited()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        await using var web = WebServer.Build(env.S, 0, env.Game.World);
        await web.StartAsync();
        var baseUri = new Uri(web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = baseUri };
        var cfg = env.S.Config.Web;
        bool was = cfg.AdminEnabled;
        try
        {
            cfg.AdminEnabled = false;
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/admin")).StatusCode);   // desligado: nem existe
            cfg.AdminEnabled = true;
            Assert.Contains("type=\"password\"", await http.GetStringAsync("/admin"));

            string gmLogin = "adm" + Guid.NewGuid().ToString("N")[..10];
            var gm = (await env.S.Accounts.CreateAsync(gmLogin, PasswordHasher.Hash("senha-forte-1")))!;
            await env.S.Accounts.SetNicknameAsync(gm.Id, "GM" + gmLogin[3..11]);
            var (player, _) = await env.NewPlayerAsync();

            // conta comum não entra
            string userLogin = "usr" + Guid.NewGuid().ToString("N")[..10];
            await env.S.Accounts.CreateAsync(userLogin, PasswordHasher.Hash("senha-forte-1"));
            Assert.Contains("Login recusado", await (await http.PostAsync("/admin/login", Form(("login", userLogin), ("password", "senha-forte-1")))).Content.ReadAsStringAsync());

            await env.S.Accounts.SetIdentityFlagsAsync(gm.Id, 0x04);
            var home = await (await http.PostAsync("/admin/login", Form(("login", gmLogin), ("password", "senha-forte-1")))).Content.ReadAsStringAsync();
            string csrf = CsrfRegex().Match(home).Groups[1].Value;
            Assert.NotEmpty(csrf);

            var page = await http.GetStringAsync($"/admin/player?q={player.Login}");
            Assert.Contains(player.Nickname!, page);

            string id = player.Id.ToString();
            var noCsrf = await http.PostAsync("/admin/action", Form(("id", id), ("op", "unblock")));
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

            await http.PostAsync("/admin/action", Form(("csrf", csrf), ("id", id), ("q", player.Login), ("op", "set"),
                ("pang", "12345"), ("cookie", "77"), ("level", "99")));
            var p = (await env.Players.LoadAsync(player.Id))!;
            Assert.Equal((12345L, 77L, Pangya.Domain.Players.Levels.Max), (p.Pang, p.Cookie, p.Level));

            var gave = await (await http.PostAsync("/admin/action", Form(("csrf", csrf), ("id", id), ("q", player.Login), ("op", "give"),
                ("tid", $"0x{SpinCubeService.LuckyKey:X8}"), ("qty", "3"), ("days", "0")))).Content.ReadAsStringAsync();
            Assert.Contains("Entregue", gave);
            Assert.Equal(3, (await env.Players.LoadAsync(player.Id))!.FindType(SpinCubeService.LuckyKey)!.Quantity);

            await http.PostAsync("/admin/action", Form(("csrf", csrf), ("id", id), ("q", player.Login), ("op", "block"), ("days", "3"), ("reason", "<script>x</script>")));
            var acc = (await env.S.Accounts.FindByIdAsync(player.Id))!;
            Assert.True(acc.IsBlocked(DateTime.UtcNow));
            var shown = await http.GetStringAsync($"/admin/player?q={player.Login}");
            Assert.DoesNotContain("<script>x", shown);                                         // escapado
            Assert.Contains("&lt;script&gt;", shown);

            var audit = await env.S.Audit.Store.RecentAsync(20, gmLogin[3..11]);
            var actions = new HashSet<string>();
            foreach (var e in audit) actions.Add(e.Action);
            Assert.Superset(new HashSet<string> { "admin:login", "admin:set", "admin:item-give", "admin:block" }, actions);
            Assert.Contains("admin:block", await http.GetStringAsync("/admin/audit"));
        }
        finally { cfg.AdminEnabled = was; }
    }
}
