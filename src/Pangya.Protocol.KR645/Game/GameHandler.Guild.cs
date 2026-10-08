using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Guilds;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Guilda (docs/protocolo/SPEC-guilda.md): C->S 0xFE..0x113, resposta = pedido + 0xB5, sempre com u32 result (1 = ok,
/// senão o código vira caixa de mensagem no cliente). Quem muda de estado e está online recebe o 0x1BD (GUILD_USER_INFO).
/// A lista/busca (0x105/0x106) fica aqui também. Emblema (upload HTTP) ainda não: 0x112 responde falha.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CGuildCreate = 0xFE, CGuildCheckName = 0xFF, CGuildRename = 0x100, CGuildMine = 0x101, CGuildNotice = 0x102,
        CGuildIntro = 0x103, CGuildClose = 0x104, CGuildListPage = 0x105, CGuildSearch = 0x106, CGuildHistory = 0x107,
        CGuildJoin = 0x109, CGuildWithdraw = 0x10A, CGuildApprove = 0x10B, CGuildReject = 0x10C, CGuildClass = 0x10D,
        CGuildMessage = 0x10E, CGuildMembers = 0x10F, CGuildLeave = 0x110, CGuildKick = 0x111, CGuildMark = 0x112,
        CGuildMarkDone = 0x113;
    const ushort SGuildState = 0x1BD, SGuildRenamed = 0x3C, SGuildEmblem = 0x3B;
    /// <summary>Resposta de cada pedido = id do pedido + 0xB5.</summary>
    const ushort GuildReplyOffset = 0xB5;

    /// <summary>GUILD_USER_INFO do 0x42 (montado no login, antes do 0x42).</summary>
    GUILD_USER_INFO myGuildInfo;

    async ValueTask<bool> HandleGuildAsync(PacketReader p)
    {
        if (p.Id is < CGuildCreate or > CGuildMarkDone || p.Id == 0x108) return false;
        var g = ctx.Guilds;
        if (g == null) { p.Skip(p.Remaining); return true; }
        ushort reply = (ushort)(p.Id + GuildReplyOffset);
        long me = Player.AccountId;
        switch (p.Id)
        {
            case CGuildListPage: await GuildListAsync(reply, p.U32(), null); return true;
            case CGuildSearch: { uint page = p.U32(); await GuildListAsync(reply, page, p.Str(32)); return true; }
            case CGuildMine:
            {
                var (code, guild, cls) = await g.InfoAsync(me, (int)p.U32());
                conn.Send(code == GuildCode.Ok ? new PacketWriter(reply, 0x140).U32(1).Struct(GuildInfo(guild!, cls)) : Code(reply, code));
                return true;
            }
            case CGuildHistory:
            {
                var list = await g.Store.HistoryAsync(me, GuildService.HistoryMax);
                var w = new PacketWriter(reply, 8 + list.Count * 0x31).U32(1).U16((ushort)list.Count);
                foreach (var h in list)
                {
                    var s = new GUILD_HISTORY { index = (int)h.Id, guildUID = (uint)h.GuildId, state = h.State, time = PlayerStructs.SystemTime(h.At.ToLocalTime()) };
                    Cp949.Write(s.guildName, h.GuildName);
                    w.Struct(s);
                }
                conn.Send(w);
                return true;
            }
            case CGuildMembers:
            {
                int gid = (int)p.U32(), page = (int)Math.Max(p.U32(), 1);
                var (code, items, total, name) = await g.MembersAsync(me, gid, page);
                if (code != GuildCode.Ok) { conn.Send(Code(reply, code)); return true; }
                var w = new PacketWriter(reply, 16 + items.Count * 0x51).U32(1).U32((uint)page).U32((uint)total).U16((ushort)items.Count);
                foreach (var m in items)
                {
                    var s = new GUILD_USER_LIST { guildUID = (uint)gid, userUID = (uint)m.AccountId, classIdx = m.Class,
                        online = (byte)(ctx.World.Find(m.AccountId) != null ? 1 : 0) };
                    Cp949.Write(s.message, m.Message);
                    Cp949.Write(s.guildName, name);
                    Cp949.Write(s.nickname, m.Nickname);
                    w.Struct(s);
                }
                conn.Send(w);
                return true;
            }
            case CGuildCheckName:
            {
                string name = p.Str(32);
                var code = await g.CheckNameAsync(name);
                conn.Send(code == GuildCode.Ok ? new PacketWriter(reply).U32(1).Str(name) : Code(reply, code));
                return true;
            }
            case CGuildCreate:
            {
                string name = p.Str(32), intro = p.Str(256);
                int kitId = Player.FindType(GuildService.CreateKit)?.Id ?? 0;
                var (code, _) = await g.CreateAsync(Player, name, intro);
                conn.Send(Code(reply, code));
                if (code == GuildCode.Ok)
                {
                    Log.Info($"{conn} criou a guilda '{name}'");
                    int left = Player.Find(kitId)?.Quantity ?? 0;                  // o kit gasto (0 = some do inventário)
                    conn.Send(new PacketWriter(SItemCounts).U8(1).U32(GuildService.CreateKit).U32((uint)kitId).U16((ushort)left));
                    await PushGuildStateAsync(me);
                }
                return true;
            }
            case CGuildRename:
            {
                int gid = (int)p.U32();
                int kitId = Player.FindType(GuildService.RenameKit)?.Id ?? 0;
                var code = await g.RenameAsync(Player, gid, p.Str(32));
                conn.Send(Code(reply, code));
                if (code == GuildCode.Ok)
                    conn.Send(new PacketWriter(SItemCounts).U8(1).U32(GuildService.RenameKit).U32((uint)kitId).U16((ushort)(Player.Find(kitId)?.Quantity ?? 0)));
                if (code == GuildCode.Ok && await g.Store.GetAsync(gid) is { } renamed) await BroadcastGuildAsync(renamed, SGuildRenamed);
                return true;
            }
            case CGuildNotice or CGuildIntro:
            {
                int gid = (int)p.U32();
                p.U32();
                conn.Send(Code(reply, await g.SetTextAsync(me, gid, p.Str(256), notice: p.Id == CGuildNotice)));
                return true;
            }
            case CGuildClose:
            {
                var (code, pending) = await g.CloseAsync(me, (int)p.U32());
                conn.Send(Code(reply, code));
                if (code == GuildCode.Ok)
                {
                    await PushGuildStateAsync(me);
                    foreach (var x in pending) await PushGuildStateAsync(x);
                }
                return true;
            }
            case CGuildJoin:
            {
                int gid = (int)p.U32();
                var code = await g.RequestJoinAsync(Player, gid, p.Str(256));
                conn.Send(Code(reply, code));
                if (code == GuildCode.Ok) await PushGuildStateAsync(me);
                return true;
            }
            case CGuildWithdraw: await AnswerAndPushAsync(reply, await g.WithdrawAsync(me, (int)p.U32()), me); return true;
            case CGuildLeave: await AnswerAndPushAsync(reply, await g.LeaveAsync(me, (int)p.U32()), me); return true;
            case CGuildApprove or CGuildReject:
            {
                int gid = (int)p.U32();
                long target = p.U32();
                await AnswerAndPushAsync(reply, await g.AnswerRequestAsync(me, gid, target, approve: p.Id == CGuildApprove), target);
                return true;
            }
            case CGuildKick:
            {
                int gid = (int)p.U32();
                long target = p.U32();
                await AnswerAndPushAsync(reply, await g.KickAsync(me, gid, target), target);
                return true;
            }
            case CGuildClass:
            {
                int gid = (int)p.U32();
                long target = p.U32();
                int cls = (int)p.U32();
                var (code, changed) = await g.ChangeClassAsync(me, gid, target, cls);
                conn.Send(code == GuildCode.Ok ? new PacketWriter(reply).U32(1).U32((uint)cls) : Code(reply, code));
                foreach (var x in changed) await PushGuildStateAsync(x);
                return true;
            }
            case CGuildMessage:
            {
                int gid = (int)p.U32();
                long target = p.U32();
                conn.Send(Code(reply, await g.SetMessageAsync(me, gid, target, p.Str(64))));
                return true;
            }
            case CGuildMark:                                                    // -> 0x1C7 u32 1, u32 EMBLEM_IDX, str nome da marca
            {
                var (code, t) = await g.StartEmblemAsync(Player, (int)p.U32());
                conn.Send(code == GuildCode.Ok ? new PacketWriter(reply).U32(1).U32((uint)t.Id).Str(t.Mark) : Code(reply, code));
                return true;
            }
            case CGuildMarkDone:                                                // o POST deu certo: aplica, 0x1C8 e 0x3B aos membros
            {
                p.Skip(p.Remaining);
                int kitId = Player.FindType(GuildService.MarkKit)?.Id ?? 0;
                var (code, guild) = await g.FinishEmblemAsync(Player);
                conn.Send(Code(reply, code));
                if (code != GuildCode.Ok) return true;
                conn.Send(new PacketWriter(SItemCounts).U8(1).U32(GuildService.MarkKit).U32((uint)kitId).U16((ushort)(Player.Find(kitId)?.Quantity ?? 0)));
                await BroadcastGuildAsync(guild!, SGuildEmblem);
                Log.Info($"{conn} emblema da guilda {guild!.Id} -> {guild.Mark}");
                return true;
            }
            default: p.Skip(p.Remaining); return true;
        }
    }

    static PacketWriter Code(ushort id, GuildCode code) => new PacketWriter(id).U32((uint)code);

    async Task AnswerAndPushAsync(ushort reply, GuildCode code, long who)
    {
        conn.Send(Code(reply, code));
        if (code == GuildCode.Ok) await PushGuildStateAsync(who);
    }

    /// <summary>0x105/0x106 -> 0x1BA/0x1BB u32 1, u32 página, u32 total, u16 n, n × GUILD_LIST (15 por página).</summary>
    async Task GuildListAsync(ushort reply, uint page, string? name)
    {
        page = Math.Max(page, 1);
        var (items, total) = await ctx.Guilds!.Store.ListAsync((int)page, GuildService.PerPage, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
        // 3º u32 = total de guildas (o cliente calcula as páginas); lista vazia leva 1 como o emulador validado: com n = 0
        // o cliente só mostra "nenhuma guilda encontrada" e não usa esse número
        var w = new PacketWriter(reply, 16 + items.Count * 0xC4).U32(1).U32(page).U32((uint)Math.Max(total, 1)).U16((ushort)items.Count);
        foreach (var g in items)
        {
            var s = new GUILD_LIST { guildUID = (uint)g.Id, guildPang = g.Pang, guildPoint = g.Point, memberCount = g.MemberCount,
                createTime = PlayerStructs.SystemTime(g.CreatedAt.ToLocalTime()), masterUID = (uint)g.MasterId };
            Cp949.Write(s.guildName, g.Name);
            Cp949.Write(s.introduce, g.Introduce);
            Cp949.Write(s.masterNickname, g.MasterNick);
            Cp949.Write(s.guildMark, g.Mark);
            w.Struct(s);
        }
        conn.Send(w);
    }

    /// <summary>GUILD_USER_INFO de uma guilda com o cargo de quem recebe (guilda null = sem guilda).</summary>
    static GUILD_USER_INFO GuildUserInfo(Guild? g, int cls)
    {
        var s = new GUILD_USER_INFO();
        if (g == null) return s;
        s.guildUID = (uint)g.Id;
        s.guildPang = g.Pang;
        s.guildPoint = g.Point;
        s.memberCount = g.MemberCount;
        s.classIdx = cls;
        s.masterUID = (uint)g.MasterId;
        Cp949.Write(s.guildName, g.Name);
        Cp949.Write(s.guildMark, g.Mark);
        Cp949.Write(s.notice, g.Notice);
        Cp949.Write(s.introduce, g.Introduce);
        Cp949.Write(s.masterNickname, g.MasterNick);
        return s;
    }

    static GUILD_INFO GuildInfo(Guild g, int cls)
    {
        var u = GuildUserInfo(g, cls);
        return new GUILD_INFO
        {
            guildUID = u.guildUID, guildName = u.guildName, guildPang = u.guildPang, guildPoint = u.guildPoint, memberCount = u.memberCount,
            guildMark = u.guildMark, notice = u.notice, introduce = u.introduce, classIdx = u.classIdx, masterUID = u.masterUID,
            masterNickname = u.masterNickname, createTime = PlayerStructs.SystemTime(g.CreatedAt.ToLocalTime()),
        };
    }

    /// <summary>Guilda e cargo atuais de uma conta (para o 0x42 e o 0x1BD).</summary>
    async Task<(Guild? Guild, int Class)> GuildOfAsync(long accountId)
    {
        var store = ctx.Guilds!.Store;
        if (await store.MembershipAsync(accountId) is not { } m) return (null, GuildClass.None);
        var g = await store.GetAsync(m.GuildId);
        return g == null ? (null, GuildClass.None) : (g, m.Class);
    }

    /// <summary>Login: guilda no jogador e o GUILD_USER_INFO do 0x42.</summary>
    async Task LoadGuildAsync()
    {
        if (ctx.Guilds == null) return;
        var (g, cls) = await GuildOfAsync(Player.AccountId);
        Player.Guild = g == null ? null : new GuildTag(g.Id, g.Name, g.Mark, cls, g.Pang);
        myGuildInfo = GuildUserInfo(g, cls);
    }

    /// <summary>0x1BD u32 1, GUILD_USER_INFO para a conta, se estiver online (e atualiza a guilda no jogador dela).</summary>
    async Task PushGuildStateAsync(long accountId)
    {
        if (ctx.World.Find(accountId) is not GameHandler h) return;
        var (g, cls) = await GuildOfAsync(accountId);
        h.Player.Guild = g == null ? null : new GuildTag(g.Id, g.Name, g.Mark, cls, g.Pang);
        h.Connection.Send(new PacketWriter(SGuildState, 0x120).U32(1).Struct(GuildUserInfo(g, cls)));
    }

    /// <summary>0x3B/0x3C GUILD_INFO (emblema/nome mudou) para os membros online da guilda.</summary>
    async Task BroadcastGuildAsync(Guild g, ushort id)
    {
        var (members, _) = await ctx.Guilds!.Store.MembersAsync(g.Id, 1, GuildService.MaxMembers * 4);
        foreach (var m in members)
            if (ctx.World.Find(m.AccountId) is GameHandler h)
            {
                h.Player.Guild = new GuildTag(g.Id, g.Name, g.Mark, m.Class, g.Pang);
                h.Connection.Send(new PacketWriter(id, 0x130).Struct(GuildInfo(g, m.Class)));
            }
    }
}
