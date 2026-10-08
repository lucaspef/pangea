namespace Pangya.Domain.Guilds;

/// <summary>Cargos (GUILD_CLASS_IDX): 1 mestre, 2 submestre, 3 membro, 9 pedido pendente; 0 = sem guilda.</summary>
public static class GuildClass
{
    public const int None = 0, Master = 1, SubMaster = 2, Member = 3, Waiting = 9;
    public static bool IsMember(int c) => c is Master or SubMaster or Member;
    public static bool IsManager(int c) => c is Master or SubMaster;
}

/// <summary>Estados do histórico (GUILD_STATE_IDX).</summary>
public static class GuildState
{
    public const int Requested = 1, Withdrew = 2, Approved = 3, Rejected = 4, ClassChanged = 5, Kicked = 6, Left = 7,
        Created = 8, Closed = 9, Delegated = 10, BecameMaster = 11, BecameSubMaster = 12, ApprovedSomeone = 13;
}

/// <summary>Códigos de resultado do cliente (u32 result de toda resposta de guilda).</summary>
public enum GuildCode : uint
{
    Ok = 1, Failed = 54001, NoAccount = 54002, NameTaken = 54003, LowLevel = 54004, AlreadyInGuild = 54005, Cooldown = 54006,
    NoPermission = 54008, Full = 54009, NotMember = 54010, SubMasterLimit = 54012, HasMembers = 54014, NoGuild = 54015,
    MasterCannotLeave = 54016, NotWaiting = 54018, CannotWithdraw = 54020, WaitingCannotLeave = 54021, NoKit = 54502,
    ClassNotAllowed = 54504, NotMaster = 54505, NotManager = 54507, NotInGuild = 54508, BadState = 54511,
}

public sealed class Guild
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public long MasterId { get; set; }
    public string MasterNick { get; set; } = "";
    public string Notice { get; set; } = "";
    public string Introduce { get; set; } = "";
    public string Mark { get; set; } = "";
    public int Pang { get; set; }
    public int Point { get; set; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Membros de verdade (cargos 1..3).</summary>
    public int MemberCount { get; set; }
}

public sealed class GuildMember
{
    public long AccountId { get; init; }
    public int GuildId { get; init; }
    public int Class { get; set; }
    public string Message { get; set; } = "";
    public string Nickname { get; init; } = "";
    /// <summary>Contribuição nos GuildMatch: pontos e "pang de guilda".</summary>
    public int Point { get; init; }
    public int Pang { get; init; }
}

public readonly record struct GuildHistory(long Id, int GuildId, string GuildName, int State, DateTime At);

/// <summary>O que vai no jogador (0x42, salas, lista do lobby): guilda, emblema e cargo.</summary>
public sealed record GuildTag(int Id, string Name, string Mark, int Class, int Pang);

/// <summary>Mudanças de guilda gravadas numa transação.</summary>
public sealed class GuildChange
{
    public List<(long Account, int Guild, int Class, string Message)> Upserts { get; } = [];
    public List<long> Removes { get; } = [];
    public List<(long Account, int Guild, string GuildName, int State)> History { get; } = [];
    public List<long> Cooldowns { get; } = [];
    /// <summary>Guilda alterada (nome, mestre, notícia, apresentação) ou encerrada (Close).</summary>
    public Guild? Update { get; set; }
    public bool Close { get; set; }
    /// <summary>Kit gasto: objeto, dono e quantidade que sobra (0 = apaga).</summary>
    public (int ItemId, long Account, int Left)? Kit { get; set; }
    /// <summary>Upload de emblema aplicado (marca a linha em guild_emblem_uploads).</summary>
    public int? EmblemApplied { get; set; }
}

/// <summary>Resultado de um GuildMatch para gravar: as duas guildas (vermelha, azul), vencedor e a parte de cada membro.</summary>
public sealed record GuildMatchRecord(int RedId, int BlueId, int[] Points, long[] Pang, int Winner,
    List<(long Account, int Guild, int Points, int Pang)> Members);

/// <summary>Upload de emblema pendente (0x112 -> POST HTTP -> 0x113).</summary>
public readonly record struct EmblemTicket(int Id, int GuildId, long AccountId, string Mark, bool Uploaded);

public interface IGuildStore
{
    /// <summary>Guilda aberta (null = não existe ou encerrada).</summary>
    Task<Guild?> GetAsync(int id);
    Task<(List<Guild> Items, int Total)> ListAsync(int page, int perPage, string? nameLike);
    Task<GuildMember?> MembershipAsync(long accountId);
    /// <summary>Membros e pedidos (cargo 9) da guilda, mestre primeiro.</summary>
    Task<(List<GuildMember> Items, int Total)> MembersAsync(int guildId, int page, int perPage);
    Task<int> CountClassAsync(int guildId, int cls);
    Task<bool> NameTakenAsync(string nameKey, int exceptGuild = 0);
    Task<DateTime?> CooldownAsync(long accountId);
    Task<List<GuildHistory>> HistoryAsync(long accountId, int max);
    /// <summary>Cria a guilda com o mestre (e o resto de <paramref name="change"/>) numa transação; devolve o id.</summary>
    Task<int> CreateAsync(string name, string nameKey, string introduce, long masterId, GuildChange change);
    Task ApplyAsync(GuildChange change);
    /// <summary>Novo upload de emblema: devolve o id (EMBLEM_IDX) e o nome da marca ('g' + id em hex).</summary>
    Task<EmblemTicket> NewEmblemTicketAsync(int guildId, long accountId);
    Task<EmblemTicket?> EmblemTicketAsync(int id);
    Task MarkEmblemUploadedAsync(int id);
    /// <summary>Último upload já enviado e ainda não aplicado desta conta.</summary>
    Task<EmblemTicket?> UploadedEmblemAsync(long accountId);
    /// <summary>Grava um GuildMatch numa transação: membros, guildas (pontos, pang, V/D/E) e o histórico.</summary>
    Task RecordMatchAsync(GuildMatchRecord m);
}
