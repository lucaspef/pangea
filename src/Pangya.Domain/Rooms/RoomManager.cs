using Pangya.Domain.Game;

namespace Pangya.Domain.Rooms;

public enum JoinResult : byte { Ok = 0, FullOrPlaying = 1, NotFound = 2, WrongPassword = 3 }

/// <summary>
/// Salas de um game server e quem está na tela de lista de salas. Todas as operações de sala e partida
/// acontecem sob <see cref="Sync"/> (um lock por game server: operações curtas, os envios só enfileiram).
/// </summary>
public sealed class RoomManager
{
    readonly Dictionary<int, Room> rooms = [];
    int nextIndex = 1;

    public object Sync { get; } = new();
    public Dictionary<int, Room>.ValueCollection Rooms => rooms.Values;
    /// <summary>Sessões na tela de lista de salas (recebem as atualizações da lista).</summary>
    public HashSet<IGameSession> Lobby { get; } = [];

    public Room? Get(int index) => rooms.GetValueOrDefault(index);

    public Room Create(RoomSettings settings, long ownerId)
    {
        settings.Normalize();
        while (rooms.ContainsKey(nextIndex)) nextIndex = nextIndex % 0xFFFE + 1;
        var room = new Room { Index = nextIndex, OwnerId = ownerId };
        nextIndex = nextIndex % 0xFFFE + 1;
        CopySettings(settings, room.Settings);
        rooms[room.Index] = room;
        return room;
    }

    static void CopySettings(RoomSettings from, RoomSettings to)
    {
        to.Title = from.Title; to.Password = from.Password; to.Mode = from.Mode; to.Course = from.Course;
        to.Holes = from.Holes; to.HoleType = from.HoleType; to.ShotTimeMs = from.ShotTimeMs;
        to.GameTimeMs = from.GameTimeMs; to.MaxPlayers = from.MaxPlayers; to.Sleep = from.Sleep;
    }

    public static JoinResult CanJoin(Room? room, string password) =>
        room == null ? JoinResult.NotFound
        : room.Settings.Password.Length > 0 && password != room.Settings.Password ? JoinResult.WrongPassword
        : room.State != RoomState.Waiting || room.Players.Count >= room.Settings.MaxPlayers ? JoinResult.FullOrPlaying
        : JoinResult.Ok;

    /// <summary>Adiciona o jogador (time alternado; o primeiro humano é o dono).</summary>
    public static RoomPlayer Join(Room room, RoomPlayer p)
    {
        p.Master = room.HumanCount == 0 && !p.IsBot;
        p.Team = (byte)(room.Players.Count % 2);
        room.Add(p);
        return p;
    }

    /// <summary>Tira o jogador. Devolve o novo dono (se o dono saiu e sobrou humano) e se a sala fechou (sem humanos).</summary>
    public (RoomPlayer? NewMaster, bool Closed) Leave(Room room, RoomPlayer p)
    {
        room.Remove(p);
        room.Game?.PlayerLeft(p);
        if (room.HumanCount == 0)
        {
            room.Game?.Cancel();
            rooms.Remove(room.Index);
            return (null, true);
        }
        if (!p.Master) return (null, false);
        foreach (var m in room.Players)
        {
            if (m.IsBot) continue;
            m.Master = true;
            room.OwnerId = m.Player.AccountId;
            return (m, false);
        }
        return (null, false);
    }

    /// <summary>Sorteia mapa (se aleatório), ordem e sementes dos buracos e marca a sala como jogando.</summary>
    public static void PrepareStart(Room room, Random rng)
    {
        var s = room.Settings;
        room.CoursePlayed = s.Course >= 0x7F && s.Course != 0xFD ? (byte)rng.Next(11) : s.Course > 0x7F ? (byte)(s.Course - 0x80) : s.Course;
        int start = s.HoleType switch { 1 => 9, 2 => rng.Next(18), _ => 0 };
        var order = new byte[18];
        var seeds = new uint[18];
        for (int i = 0; i < 18; i++)
        {
            order[i] = (byte)((start + i) % 18 + 1);
            seeds[i] = (uint)rng.NextInt64(uint.MaxValue);
        }
        if (s.HoleType == 3) rng.Shuffle(order);
        room.HoleOrder = order;
        room.HoleSeeds = seeds;
        room.GameSeed = (uint)rng.NextInt64(uint.MaxValue);
        room.State = RoomState.Playing;
        foreach (var p in room.Players) p.Ready = p.Master || p.IsBot;
    }

    /// <summary>Fim de partida: a sala volta a esperar.</summary>
    public static void FinishGame(Room room)
    {
        room.State = RoomState.Waiting;
        room.Game = null;
        foreach (var p in room.Players) p.Ready = p.IsBot;
    }
}
