using System.Security.Cryptography;
using Pangya.Domain.Game;
using Pangya.Domain.Players;

namespace Pangya.Domain.Rooms;

/// <summary>Tipo de jogo (eGameType do cliente; os valores são os mesmos em todas as versões conhecidas).</summary>
public enum GameMode : byte
{
    Stroke = 0, Team = 1, AvatarChat = 2, Match = 3, Tournament = 4, Team30s = 5, GuildMatch = 6, PangBattle = 7,
    MyRoom = 8, Approach = 9, NewApproach = 10, TutorialBasic = 11, TutorialAdvanced = 12, OfflineGhost = 13,
}

public enum RoomState { Waiting, Playing }

/// <summary>Guilda de um lado do GuildMatch (vai no sGuildRoomInfo do sRoomInfo).</summary>
public sealed record GuildSide(int Id, string Name, string Mark);

/// <summary>Configuração de uma sala (o que o dono escolhe).</summary>
public sealed class RoomSettings
{
    public string Title { get; set; } = "Sala";
    public string Password { get; set; } = "";
    public GameMode Mode { get; set; }
    /// <summary>Mapa escolhido (0..19; 0x7F = aleatório).</summary>
    public byte Course { get; set; }
    public byte Holes { get; set; } = 18;
    /// <summary>Ordem dos buracos: 0 em ordem, 1 volta primeiro, 2 início aleatório, 3 embaralhado.</summary>
    public byte HoleType { get; set; }
    public uint ShotTimeMs { get; set; }
    public uint GameTimeMs { get; set; }
    public byte MaxPlayers { get; set; } = 4;
    public bool Sleep { get; set; }

    /// <summary>Corrige valores fora do permitido (nunca confiar no cliente).</summary>
    public void Normalize()
    {
        Title = Title.Length > 31 ? Title[..31] : Title.Length == 0 ? "Sala" : Title;
        Password = Password.Length > 15 ? Password[..15] : Password;
        Holes = Math.Clamp(Holes, (byte)1, (byte)18);
        HoleType = HoleType <= 3 ? HoleType : (byte)0;
        MaxPlayers = Math.Clamp(MaxPlayers, (byte)1, MassGame.IsMass(Mode) || Mode == GameMode.AvatarChat ? (byte)30 : (byte)4);   // torneio/approach/lounge: até 30
        ShotTimeMs = Math.Min(ShotTimeMs, 600_000);
        GameTimeMs = Math.Min(GameTimeMs, 7_200_000);
        if (Mode == GameMode.GuildMatch)                                    // SPEC-guildmatch.md §1.1: o cliente trava assim
        {
            Course = 0x7F;
            Holes = Holes <= 9 ? (byte)9 : (byte)18;
            MaxPlayers = MaxPlayers <= 10 ? (byte)10 : MaxPlayers <= 20 ? (byte)20 : (byte)30;
            ShotTimeMs = 0;
            if (GameTimeMs == 0) GameTimeMs = Holes == 9 ? 1_200_000u : 1_800_000u;
        }
    }
}

/// <summary>Um participante da sala (jogador ou bot).</summary>
public sealed class RoomPlayer
{
    /// <summary>Id usado pelo cliente para o jogador dentro da sala e da partida (sUserInfo.dwGuid = uid da conta).</summary>
    public uint Guid { get; init; }
    public required Player Player { get; init; }
    /// <summary>null = bot.</summary>
    public IGameSession? Session { get; init; }
    public bool IsBot => Session == null;
    public int Slot { get; set; }
    public bool Master { get; set; }
    public bool Ready { get; set; }
    public byte Team { get; set; }
    // lounge (modo 2): onde o avatar está e o que está fazendo, para quem entra depois (sSlotInfo.location/action/state)
    public float X { get; set; }
    public float Z { get; set; }
    public float Angle { get; set; }
    public uint Action { get; set; }
    public uint State { get; set; }
    /// <summary>Título da loja pessoal (sSlotInfo.strTradeTitle; vazio = sem loja).</summary>
    public string TradeTitle { get; set; } = "";
    /// <summary>
    /// Itens SP do lounge (SPEC-lounge-sp.md): valor atual de cada efeito 0..4 (1 = desligado; gigante, cabeça grande,
    /// velocidade, brilho, luva) e o último uso (recarga).
    /// </summary>
    public float[] SpValues { get; } = [1, 1, 1, 1, 1];
    public long SpLastUse { get; set; }
}

public sealed class Room
{
    public int Index { get; init; }
    public RoomSettings Settings { get; } = new();
    public RoomState State { get; set; }
    /// <summary>GuildMatch: guilda do lado vermelho (0, de quem criou) e azul (1); null = lado vazio.</summary>
    public GuildSide?[] GuildSides { get; } = new GuildSide?[2];

    /// <summary>Lado (0/1) da guilda na sala; -1 = nenhum.</summary>
    public int SideOf(int guildId)
    {
        for (int i = 0; i < 2; i++) if (GuildSides[i]?.Id == guildId) return i;
        return -1;
    }

    /// <summary>Clima posto por GM (/weather: 0 bom, 1 nublado, 2 chuva, 3 neve); null = o do cliente.</summary>
    public byte? Weather { get; set; }
    /// <summary>Chave de 16 bytes da sala (sRoomInfo.RoomKey): os resultados de tacada vêm cifrados com ela.</summary>
    public byte[] Key { get; } = RandomNumberGenerator.GetBytes(16);
    /// <summary>Participantes em ordem de slot (a ordem importa: connectionRank e ordem do tee).</summary>
    public List<RoomPlayer> Players { get; } = [];
    readonly Dictionary<IGameSession, RoomPlayer> bySession = [];
    readonly Dictionary<uint, RoomPlayer> byGuid = [];
    public long OwnerId { get; set; }
    /// <summary>Lojas pessoais abertas no lounge, pelo guid do dono.</summary>
    public Dictionary<uint, PersonalShop> Shops { get; } = [];

    // definidos ao começar a partida
    public byte CoursePlayed { get; set; }
    public byte[] HoleOrder { get; set; } = [];
    public uint[] HoleSeeds { get; set; } = [];
    public uint GameSeed { get; set; }
    /// <summary>Partida em andamento (null na espera).</summary>
    public RoomGame? Game { get; set; }
    /// <summary>Moedas/caixas do Wiz City desta partida (null em outros cursos).</summary>
    public FieldItems? Field { get; set; }

    /// <summary>Sessões humanas na sala (chave = sessão).</summary>
    public Dictionary<IGameSession, RoomPlayer>.KeyCollection Humans => bySession.Keys;
    public int HumanCount => bySession.Count;
    public RoomPlayer? Find(IGameSession s) => bySession.GetValueOrDefault(s);
    public RoomPlayer? Find(uint guid) => byGuid.GetValueOrDefault(guid);
    public RoomPlayer? Bot { get; private set; }
    /// <summary>Dificuldade do bot desta sala (vale na próxima partida).</summary>
    public BotLevel BotLevel { get; set; } = BotLevel.Normal;

    public void Add(RoomPlayer p)
    {
        Players.Add(p);
        byGuid[p.Guid] = p;
        if (p.Session != null) bySession[p.Session] = p;
        else Bot = p;
        Renumber();
    }

    public void Remove(RoomPlayer p)
    {
        Players.Remove(p);
        byGuid.Remove(p.Guid);
        if (p.Session != null) bySession.Remove(p.Session);
        else if (Bot == p) Bot = null;
        Renumber();
    }

    /// <summary>Renumera os slots (connectionRank começa em 1, na ordem de entrada).</summary>
    void Renumber()
    {
        for (int i = 0; i < Players.Count; i++) Players[i].Slot = i + 1;
    }
}
