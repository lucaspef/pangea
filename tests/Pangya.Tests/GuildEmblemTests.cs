using System.Buffers.Binary;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Pangya.Core.Net;
using Pangya.Domain.Guilds;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;
using Pangya.Web;

namespace Pangya.Tests;

public class EmblemRuleTests
{
    /// <summary>Cabeçalho PNG (assinatura + IHDR) com largura, altura, profundidade e tipo de cor.</summary>
    public static byte[] Png(int w, int h, byte depth = 8, byte color = 6)
    {
        var b = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
        b[24] = depth;
        b[25] = color;
        return b;
    }

    [Fact]
    public void PngAndMarkRules()
    {
        Assert.True(GuildService.ValidEmblemPng(Png(22, 20)));
        Assert.False(GuildService.ValidEmblemPng(Png(23, 20)));               // largo demais
        Assert.False(GuildService.ValidEmblemPng(Png(22, 20, color: 2)));     // RGB (24 bits)
        Assert.False(GuildService.ValidEmblemPng("GIF89a............................"u8));
        Assert.True(GuildService.ValidMarkName("g1a"));
        Assert.False(GuildService.ValidMarkName("../x"));
        Assert.False(GuildService.ValidMarkName("gZZ"));
    }
}

/// <summary>Emblema: 0x112 -> POST /Guild/upload.asp -> 0x113 -> 0x3B, e o download (SPEC-guilda.md §4).</summary>
[Collection("db")]
public class GuildEmblemTests(DbFixture fx)
{
    [Fact]
    public async Task UploadAppliesTheMarkAndServesIt()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        await using var web = WebServer.Build(env.S, 0);
        await web.StartAsync();
        var baseUri = new Uri(web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

        var (acc, key) = await env.NewPlayerAsync();
        var p = (await env.Players.LoadAsync(acc.Id))!;
        var shop = new ShopService(env.Players.Store, env.Data);
        await shop.GiveAsync(p, GuildService.CreateKit, 1);
        await shop.GiveAsync(p, GuildService.MarkKit, 1);
        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0xFE).Str("E" + Guid.NewGuid().ToString("N")[..10]).Str("intro"));
        Assert.Equal(1u, (await c.ExpectAsync(0x1B3)).U32());
        var st = await c.ExpectAsync(0x1BD);
        st.U32();
        uint gid = st.Struct<GUILD_USER_INFO>().guildUID;

        await c.SendAsync(new PacketWriter(0x112).U32(gid));
        var ticket = await c.ExpectAsync(0x1C7);
        Assert.Equal(1u, ticket.U32());
        uint idx = ticket.U32();
        string mark = ticket.Str();

        using var http = new HttpClient { BaseAddress = baseUri };
        async Task<string> PostAsync(long uid)
        {
            using var form = new MultipartFormDataContent("--MULTI-PARTS-FORM-DATA-BOUNDARY");
            form.Add(new StringContent(idx.ToString()), "EMBLEM_IDX");
            form.Add(new StringContent(gid.ToString()), "GUILD_IDX");
            form.Add(new StringContent(uid.ToString()), "UID");
            form.Add(new StringContent(mark), "EMBLEM");
            var file = new ByteArrayContent(EmblemRuleTests.Png(22, 20));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "FILENAME", "C:\\pangya\\mark.png");
            var resp = await http.PostAsync("/Guild/upload.asp", form);
            return await resp.Content.ReadAsStringAsync();
        }
        Assert.StartsWith("PANGYA_UPDATE_FAIL", await PostAsync(acc.Id + 1));   // outra conta: recusado
        Assert.StartsWith("PANGYA_UPDATE_OK", await PostAsync(acc.Id));

        await c.SendAsync(new PacketWriter(0x113));
        Assert.Equal(1u, (await c.ExpectAsync(0x1C8)).U32());
        Assert.Equal(1, (await c.ExpectAsync(0xA5)).U8());                  // kit de emblema gasto
        var info = (await c.ExpectAsync(0x3B)).Struct<GUILD_INFO>();
        Assert.Equal(mark, Pangya.Core.Text.Cp949.Read(info.guildMark));
        Assert.Equal(mark, (await env.S.Guilds.GetAsync((int)gid))!.Mark);
        Assert.Null((await env.Players.LoadAsync(acc.Id))!.FindType(GuildService.MarkKit));

        var png = await http.GetByteArrayAsync($"/_Files/GuildMark/{mark}.png");
        Assert.Equal(EmblemRuleTests.Png(22, 20), png);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync("/_Files/GuildMark/..%2Fsecret.png")).StatusCode);
    }
}
