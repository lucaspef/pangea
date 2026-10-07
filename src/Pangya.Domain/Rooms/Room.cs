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
        MaxPlayers = Math.Clamp(MaxPlayers, (byte)1, (byte)4);
        ShotTimeMs = Math.Min(ShotTimeMs, 600_000);
        GameTimeMs = Math.Min(GameTimeMs, 7_200_000);
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
}

public sealed class Room
{
    public int Index { get; init; }
    public RoomSettings Settings { get; } = new();
    public RoomState State { get; set; }
    /// <summary>Chave de 16 bytes da sala (sRoomInfo.RoomKey): os resultados de tacada vêm cifrados com ela.</summary>
    public byte[] Key { get; } = RandomNumberGenerator.GetBytes(16);
    /// <summary>Participantes em ordem de slot (a ordem importa: connectionRank e ordem do tee).</summary>
    public List<RoomPlayer> Players { get; } = [];
    readonly Dictionary<IGameSession, RoomPlayer> bySession = [];
    readonly Dictionary<uint, RoomPlayer> byGuid = [];
    public long OwnerId { get; set; }

    // definidos ao começar a partida
    public byte CoursePlayed { get; set; }
    public byte[] HoleOrder { get; set; } = [];
    public uint[] HoleSeeds { get; set; } = [];
    public uint GameSeed { get; set; }
    /// <summary>Partida em andamento (null na espera).</summary>
    public StrokeGame? Game { get; set; }

    /// <summary>Sessões humanas na sala (chave = sessão).</summary>
    public Dictionary<IGameSession, RoomPlayer>.KeyCollection Humans => bySession.Keys;
    public int HumanCount => bySession.Count;
    public RoomPlayer? Find(IGameSession s) => bySession.GetValueOrDefault(s);
    public RoomPlayer? Find(uint guid) => byGuid.GetValueOrDefault(guid);
    public RoomPlayer? Bot { get; private set; }

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
