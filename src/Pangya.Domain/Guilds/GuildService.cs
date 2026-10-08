using Pangya.Core.Text;
using Pangya.Domain.Players;

namespace Pangya.Domain.Guilds;

/// <summary>
/// Regras de guilda (docs/protocolo/SPEC-guilda.md §2-3). O cliente libera botões pelo cargo, mas qualquer pacote pode
/// chegar: tudo é conferido de novo aqui. Cada ação devolve o código do cliente e, quando muda o estado de alguém,
/// a lista de quem precisa receber o GUILD_USER_INFO novo (0x1BD).
/// </summary>
public sealed class GuildService(IGuildStore store)
{
    public const int PerPage = 15, MaxMembers = 50, MaxSubMasters = 5, NameMinBytes = 4, NameMaxBytes = 20, TextMaxBytes = 100,
        MessageMaxBytes = 20, HistoryMax = 50;
    public const int CreateKit = 0x1A00012F, MarkKit = 0x1A000130, RenameKit = 0x1A000131;

    public IGuildStore Store => store;

    static int Bytes(string s) => Cp949.Encoding.GetByteCount(s);

    /// <summary>Nome: 4..20 bytes cp949, sem espaço (FilteringGuildString do cliente).</summary>
    public static bool ValidName(string name) => Bytes(name) is >= NameMinBytes and <= NameMaxBytes && !name.Contains(' ') && !name.Contains('\'');

    static string Key(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Guilda do jogador para o 0x42/salas (null = sem guilda).</summary>
    public async Task<GuildTag?> TagAsync(long accountId)
    {
        if (await store.MembershipAsync(accountId) is not { } m || await store.GetAsync(m.GuildId) is not { } g) return null;
        return new GuildTag(g.Id, g.Name, g.Mark, m.Class, g.Pang);
    }

    public async Task<GuildCode> CheckNameAsync(string name, int exceptGuild = 0) =>
        !ValidName(name) ? GuildCode.Failed : await store.NameTakenAsync(Key(name), exceptGuild) ? GuildCode.NameTaken : GuildCode.Ok;

    async Task<GuildCode> CanJoinSomethingAsync(long accountId)
    {
        if (await store.MembershipAsync(accountId) != null) return GuildCode.AlreadyInGuild;
        return await store.CooldownAsync(accountId) is { } until && until > DateTime.UtcNow ? GuildCode.Cooldown : GuildCode.Ok;
    }

    /// <summary>0xFE: cria com o kit 0x1A00012F do inventário (gasto na mesma transação). Devolve o id da guilda.</summary>
    public async Task<(GuildCode Code, int GuildId)> CreateAsync(Player p, string name, string introduce)
    {
        var code = await CanJoinSomethingAsync(p.AccountId);
        if (code == GuildCode.Ok) code = await CheckNameAsync(name);
        if (code == GuildCode.Ok && Bytes(introduce) > TextMaxBytes) code = GuildCode.Failed;
        var kit = p.FindType(CreateKit);
        if (code == GuildCode.Ok && kit is not { Quantity: > 0 }) code = GuildCode.NoKit;
        if (code != GuildCode.Ok) return (code, 0);
        var ch = new GuildChange { Kit = (kit!.Id, p.AccountId, kit.Quantity - 1) };
        ch.History.Add((p.AccountId, 0, name, GuildState.Created));
        int id;
        try { id = await store.CreateAsync(name, Key(name), introduce, p.AccountId, ch); }
        catch (Exception) { return (GuildCode.NameTaken, 0); }               // nome pego no mesmo instante (índice único)
        UseKit(p, kit);
        return (GuildCode.Ok, id);
    }

    static void UseKit(Player p, Item kit)
    {
        var left = kit.Clone();
        left.Quantity--;
        if (left.Quantity <= 0) p.Items.Remove(kit.Id); else p.Items[kit.Id] = left;
    }

    /// <summary>Guilda e cargo de quem pede (0x101: só membro ou quem tem pedido nela).</summary>
    public async Task<(GuildCode Code, Guild? Guild, int Class)> InfoAsync(long accountId, int guildId)
    {
        var m = await store.MembershipAsync(accountId);
        if (m == null || m.GuildId != guildId) return (GuildCode.NotInGuild, null, 0);
        var g = await store.GetAsync(guildId);
        return g == null ? (GuildCode.NoGuild, null, 0) : (GuildCode.Ok, g, m.Class);
    }

    /// <summary>0x10F: só membros (1..3) da própria guilda veem a lista (com os pedidos pendentes).</summary>
    public async Task<(GuildCode Code, List<GuildMember> Items, int Total, string GuildName)> MembersAsync(long accountId, int guildId, int page)
    {
        var m = await store.MembershipAsync(accountId);
        if (m == null || m.GuildId != guildId || !GuildClass.IsMember(m.Class)) return (GuildCode.NotInGuild, [], 0, "");
        var g = await store.GetAsync(guildId);
        if (g == null) return (GuildCode.NoGuild, [], 0, "");
        var (items, total) = await store.MembersAsync(guildId, page, PerPage);
        return (GuildCode.Ok, items, total, g.Name);
    }

    /// <summary>0x109: pedir para entrar (vira cargo 9 na guilda pedida).</summary>
    public async Task<GuildCode> RequestJoinAsync(Player p, int guildId, string text)
    {
        var code = await CanJoinSomethingAsync(p.AccountId);
        if (code != GuildCode.Ok) return code;
        var g = await store.GetAsync(guildId);
        if (g == null) return GuildCode.NoGuild;
        if (g.MemberCount >= MaxMembers) return GuildCode.Full;
        var ch = new GuildChange();
        ch.Upserts.Add((p.AccountId, guildId, GuildClass.Waiting, Trim(text, TextMaxBytes)));
        ch.History.Add((p.AccountId, guildId, g.Name, GuildState.Requested));
        await store.ApplyAsync(ch);
        return GuildCode.Ok;
    }

    /// <summary>0x10A: desistir do pedido.</summary>
    public async Task<GuildCode> WithdrawAsync(long accountId, int guildId)
    {
        var m = await store.MembershipAsync(accountId);
        if (m == null || m.GuildId != guildId || m.Class != GuildClass.Waiting) return GuildCode.CannotWithdraw;
        var g = await store.GetAsync(guildId);
        var ch = new GuildChange();
        ch.Removes.Add(accountId);
        ch.History.Add((accountId, guildId, g?.Name ?? "", GuildState.Withdrew));
        await store.ApplyAsync(ch);
        return GuildCode.Ok;
    }

    /// <summary>Gestor (1/2) da guilda pedida e o alvo nela (ou null).</summary>
    async Task<(GuildCode Code, GuildMember? Me, GuildMember? Target, Guild? Guild)> ManagerAndTargetAsync(long accountId, int guildId, long target)
    {
        var me = await store.MembershipAsync(accountId);
        if (me == null || me.GuildId != guildId || !GuildClass.IsMember(me.Class)) return (GuildCode.NotInGuild, null, null, null);
        var g = await store.GetAsync(guildId);
        if (g == null) return (GuildCode.NoGuild, null, null, null);
        var t = await store.MembershipAsync(target);
        if (t == null || t.GuildId != guildId) return (GuildCode.NotMember, me, null, g);
        return (GuildCode.Ok, me, t, g);
    }

    /// <summary>0x10B aprovar (ok = true) / 0x10C recusar: gestor, alvo com pedido.</summary>
    public async Task<GuildCode> AnswerRequestAsync(long accountId, int guildId, long target, bool approve)
    {
        var (code, me, t, g) = await ManagerAndTargetAsync(accountId, guildId, target);
        if (code != GuildCode.Ok) return code;
        if (!GuildClass.IsManager(me!.Class)) return GuildCode.NotManager;
        if (t!.Class != GuildClass.Waiting) return GuildCode.NotWaiting;
        if (approve && g!.MemberCount >= MaxMembers) return GuildCode.Full;
        var ch = new GuildChange();
        if (approve)
        {
            ch.Upserts.Add((target, guildId, GuildClass.Member, ""));
            ch.History.Add((target, guildId, g!.Name, GuildState.Approved));
            ch.History.Add((accountId, guildId, g.Name, GuildState.ApprovedSomeone));
        }
        else
        {
            ch.Removes.Add(target);
            ch.History.Add((target, guildId, g!.Name, GuildState.Rejected));
        }
        await store.ApplyAsync(ch);
        return GuildCode.Ok;
    }

    /// <summary>
    /// 0x10D cargo: só o mestre. 2 = promover membro, 3 = rebaixar submestre, 1 = passar o cargo de mestre a um submestre
    /// (o antigo mestre vira membro: o cliente não aceita mestre virar submestre). Devolve quem mudou.
    /// </summary>
    public async Task<(GuildCode Code, long[] Changed)> ChangeClassAsync(long accountId, int guildId, long target, int newClass)
    {
        var (code, me, t, g) = await ManagerAndTargetAsync(accountId, guildId, target);
        if (code != GuildCode.Ok) return (code, []);
        if (me!.Class != GuildClass.Master) return (GuildCode.NotMaster, []);
        if (target == accountId) return (GuildCode.ClassNotAllowed, []);
        var ch = new GuildChange();
        switch (newClass)
        {
            case GuildClass.SubMaster when t!.Class == GuildClass.Member:
                if (await store.CountClassAsync(guildId, GuildClass.SubMaster) >= MaxSubMasters) return (GuildCode.SubMasterLimit, []);
                ch.Upserts.Add((target, guildId, GuildClass.SubMaster, t.Message));
                ch.History.Add((target, guildId, g!.Name, GuildState.BecameSubMaster));
                break;
            case GuildClass.Member when t!.Class == GuildClass.SubMaster:
                ch.Upserts.Add((target, guildId, GuildClass.Member, t.Message));
                ch.History.Add((target, guildId, g!.Name, GuildState.ClassChanged));
                break;
            case GuildClass.Master when t!.Class == GuildClass.SubMaster:
                ch.Upserts.Add((target, guildId, GuildClass.Master, t.Message));
                ch.Upserts.Add((accountId, guildId, GuildClass.Member, me.Message));
                g!.MasterId = target;
                ch.Update = g;
                ch.History.Add((target, guildId, g.Name, GuildState.BecameMaster));
                ch.History.Add((accountId, guildId, g.Name, GuildState.Delegated));
                await store.ApplyAsync(ch);
                return (GuildCode.Ok, [target, accountId]);
            default:
                return (GuildCode.ClassNotAllowed, []);
        }
        await store.ApplyAsync(ch);
        return (GuildCode.Ok, [target]);
    }

    /// <summary>0x111 expulsar: gestor, alvo de cargo menor que o seu (o mestre expulsa 2/3/9, o submestre só 3/9).</summary>
    public async Task<GuildCode> KickAsync(long accountId, int guildId, long target)
    {
        var (code, me, t, g) = await ManagerAndTargetAsync(accountId, guildId, target);
        if (code != GuildCode.Ok) return code;
        if (!GuildClass.IsManager(me!.Class)) return GuildCode.NotManager;
        if (target == accountId || t!.Class <= me.Class) return GuildCode.NoPermission;
        var ch = new GuildChange();
        ch.Removes.Add(target);
        ch.History.Add((target, guildId, g!.Name, GuildState.Kicked));
        await store.ApplyAsync(ch);
        return GuildCode.Ok;
    }

    /// <summary>0x110 sair: submestre/membro (o mestre não sai; quem só pediu usa o 0x10A). 24 h de espera.</summary>
    public async Task<GuildCode> LeaveAsync(long accountId, int guildId)
    {
        var m = await store.MembershipAsync(accountId);
        if (m == null || m.GuildId != guildId) return GuildCode.NotInGuild;
        if (m.Class == GuildClass.Master) return GuildCode.MasterCannotLeave;
        if (m.Class == GuildClass.Waiting) return GuildCode.WaitingCannotLeave;
        var g = await store.GetAsync(guildId);
        var ch = new GuildChange();
        ch.Removes.Add(accountId);
        ch.Cooldowns.Add(accountId);
        ch.History.Add((accountId, guildId, g?.Name ?? "", GuildState.Left));
        await store.ApplyAsync(ch);
        return GuildCode.Ok;
    }

    /// <summary>0x104 encerrar: só o mestre e sem outros membros (pedidos pendentes são descartados). 24 h de espera.</summary>
    public async Task<(GuildCode Code, long[] Pending)> CloseAsync(long accountId, int guildId)
    {
        var m = await store.MembershipAsync(accountId);
        if (m == null || m.GuildId != guildId) return (GuildCode.NotInGuild, []);
        if (m.Class != GuildClass.Master) return (GuildCode.NotMaster, []);
        var g = await store.GetAsync(guildId);
        if (g == null) return (GuildCode.NoGuild, []);
        if (g.MemberCount > 1) return (GuildCode.HasMembers, []);
        var (all, _) = await store.MembersAsync(guildId, 1, MaxMembers * 4);
        var pending = new List<long>();
        foreach (var x in all) if (x.Class == GuildClass.Waiting) pending.Add(x.AccountId);
        var ch = new GuildChange { Update = g, Close = true };
        ch.Cooldowns.Add(accountId);
        ch.History.Add((accountId, guildId, g.Name, GuildState.Closed));
        foreach (var x in pending) ch.History.Add((x, guildId, g.Name, GuildState.Rejected));
        await store.ApplyAsync(ch);
        return (GuildCode.Ok, pending.ToArray());
    }

    /// <summary>0x102/0x103 notícia/apresentação: gestor, 2..100 bytes (o cliente troca ' por `; aqui volta).</summary>
    public async Task<GuildCode> SetTextAsync(long accountId, int guildId, string text, bool notice)
    {
        var m = await store.MembershipAsync(accountId);
        if (m == null || m.GuildId != guildId) return GuildCode.NotInGuild;
        if (!GuildClass.IsManager(m.Class)) return GuildCode.NotManager;
        text = text.Replace('`', '\'');
        if (Bytes(text) is < 2 or > TextMaxBytes) return GuildCode.Failed;
        var g = await store.GetAsync(guildId);
        if (g == null) return GuildCode.NoGuild;
        if (notice) g.Notice = text; else g.Introduce = text;
        await store.ApplyAsync(new GuildChange { Update = g });
        return GuildCode.Ok;
    }

    /// <summary>0x10E mensagem pessoal: a própria (membro) ou de qualquer um (gestor); até 20 bytes.</summary>
    public async Task<GuildCode> SetMessageAsync(long accountId, int guildId, long target, string message)
    {
        var (code, me, t, _) = await ManagerAndTargetAsync(accountId, guildId, target);
        if (code != GuildCode.Ok) return code;
        if (target != accountId && !GuildClass.IsManager(me!.Class)) return GuildCode.NotManager;
        message = message.Replace('`', '\'');
        if (Bytes(message) > MessageMaxBytes) return GuildCode.Failed;
        var ch = new GuildChange();
        ch.Upserts.Add((target, guildId, t!.Class, message));
        await store.ApplyAsync(ch);
        return GuildCode.Ok;
    }

    /// <summary>0x100 trocar o nome: gestor, com o kit 0x1A000131 (gasto na mesma transação), nome livre e diferente.</summary>
    public async Task<GuildCode> RenameAsync(Player p, int guildId, string name)
    {
        var m = await store.MembershipAsync(p.AccountId);
        if (m == null || m.GuildId != guildId) return GuildCode.NotInGuild;
        if (!GuildClass.IsManager(m.Class)) return GuildCode.NotMaster;
        var g = await store.GetAsync(guildId);
        if (g == null) return GuildCode.NoGuild;
        if (name == g.Name) return GuildCode.NameTaken;
        var code = await CheckNameAsync(name, guildId);
        if (code != GuildCode.Ok) return code;
        var kit = p.FindType(RenameKit);
        if (kit is not { Quantity: > 0 }) return GuildCode.NoKit;
        g.Name = name;
        try { await store.ApplyAsync(new GuildChange { Update = g, Kit = (kit.Id, p.AccountId, kit.Quantity - 1) }); }
        catch (Exception) { return GuildCode.NameTaken; }
        UseKit(p, kit);
        return GuildCode.Ok;
    }

    /// <summary>0x112: gestor com o kit 0x1A000130 abre um upload (o kit só é gasto quando o emblema é aplicado).</summary>
    public async Task<(GuildCode Code, EmblemTicket Ticket)> StartEmblemAsync(Player p, int guildId)
    {
        var m = await store.MembershipAsync(p.AccountId);
        if (m == null || m.GuildId != guildId) return (GuildCode.NotInGuild, default);
        if (!GuildClass.IsManager(m.Class)) return (GuildCode.NotManager, default);
        if (await store.GetAsync(guildId) == null) return (GuildCode.NoGuild, default);
        if (p.FindType(MarkKit) is not { Quantity: > 0 }) return (GuildCode.NoKit, default);
        return (GuildCode.Ok, await store.NewEmblemTicketAsync(guildId, p.AccountId));
    }

    /// <summary>0x113 (o POST HTTP deu certo): aplica a marca enviada, gasta o kit. Devolve a guilda atualizada.</summary>
    public async Task<(GuildCode Code, Guild? Guild)> FinishEmblemAsync(Player p)
    {
        if (await store.UploadedEmblemAsync(p.AccountId) is not { } t) return (GuildCode.Failed, null);
        var m = await store.MembershipAsync(p.AccountId);
        if (m == null || m.GuildId != t.GuildId || !GuildClass.IsManager(m.Class)) return (GuildCode.NotManager, null);
        var g = await store.GetAsync(t.GuildId);
        if (g == null) return (GuildCode.NoGuild, null);
        var kit = p.FindType(MarkKit);
        if (kit is not { Quantity: > 0 }) return (GuildCode.NoKit, null);
        g.Mark = t.Mark;
        await store.ApplyAsync(new GuildChange { Update = g, Kit = (kit.Id, p.AccountId, kit.Quantity - 1), EmblemApplied = t.Id });
        UseKit(p, kit);
        return (GuildCode.Ok, g);
    }

    public const int EmblemMaxWidth = 22, EmblemMaxHeight = 20, EmblemMaxBytes = 8 * 1024;

    /// <summary>PNG de 32 bits (RGBA 8 bits por canal) até 22×20, como o cliente exige (FrRegisterGuildMark).</summary>
    public static bool ValidEmblemPng(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 33 || png.Length > EmblemMaxBytes || !png[..8].SequenceEqual(sig) || !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        int w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[16..]);
        int h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        return w is > 0 and <= EmblemMaxWidth && h is > 0 and <= EmblemMaxHeight && png[24] == 8 && png[25] == 6;
    }

    /// <summary>Nome de marca seguro para arquivo ('g' + hex).</summary>
    public static bool ValidMarkName(string mark)
    {
        if (mark.Length is < 2 or > 11 || mark[0] != 'g') return false;
        foreach (char ch in mark[1..]) if (!char.IsAsciiHexDigitLower(ch)) return false;
        return true;
    }

    static string Trim(string s, int maxBytes)
    {
        while (Bytes(s) > maxBytes && s.Length > 0) s = s[..^1];
        return s;
    }
}
