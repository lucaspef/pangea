using System.Collections.Concurrent;
using Pangya.Core.Config;
using Pangya.Domain.Players;

namespace Pangya.Domain.Game;

/// <summary>Uma sessão de jogador no game server (implementada pela camada de protocolo).</summary>
public interface IGameSession
{
    Player Player { get; }
    /// <summary>Derruba a sessão (ex.: a mesma conta entrou de novo).</summary>
    void Kick(string reason);
}

public enum ChannelJoinResult { Ok = 1, Full = 2, NotFound = 3 }

public sealed class Channel(int id, string name, int maxUsers)
{
    int count;
    public int Id { get; } = id;
    public string Name { get; } = name;
    public int MaxUsers { get; } = maxUsers;
    public int Count => count;

    internal bool TryEnter()
    {
        while (true)
        {
            int c = count;
            if (c >= MaxUsers) return false;
            if (Interlocked.CompareExchange(ref count, c + 1, c) == c) return true;
        }
    }

    internal void Leave() => Interlocked.Decrement(ref count);
}

/// <summary>Estado de um game server: jogadores online e canais. Independente de versão do cliente.</summary>
public sealed class GameWorld(GameConfig config)
{
    readonly ConcurrentDictionary<long, IGameSession> online = new();

    public GameConfig Config { get; } = config;
    public Rooms.RoomManager Rooms { get; } = new();
    public IReadOnlyList<Channel> Channels { get; } = CreateChannels(config);

    static List<Channel> CreateChannels(GameConfig cfg)
    {
        var list = new List<Channel>(cfg.Channels.Length);
        for (int i = 0; i < cfg.Channels.Length; i++) list.Add(new Channel(i, cfg.Channels[i].Name, cfg.Channels[i].MaxUsers));
        return list;
    }
    public int OnlineCount => online.Count;
    public bool IsFull => online.Count >= Config.MaxUsers;

    /// <summary>Registra a sessão; se a conta já estava online aqui, a sessão antiga é derrubada.</summary>
    public void Enter(IGameSession s) =>
        online.AddOrUpdate(s.Player.AccountId, s, (_, prev) => { if (prev != s) prev.Kick("a conta entrou de novo"); return s; });

    /// <summary>Remove a sessão (só se ainda for a atual daquela conta).</summary>
    public void Leave(IGameSession s) => online.TryRemove(new KeyValuePair<long, IGameSession>(s.Player.AccountId, s));

    public IGameSession? Find(long accountId) => online.GetValueOrDefault(accountId);

    public ChannelJoinResult JoinChannel(int id, Channel? current, out Channel? joined)
    {
        joined = id >= 0 && id < Channels.Count ? Channels[id] : null;
        if (joined == null) return ChannelJoinResult.NotFound;
        if (joined == current) return ChannelJoinResult.Ok;
        if (!joined.TryEnter()) { joined = null; return ChannelJoinResult.Full; }
        current?.Leave();
        return ChannelJoinResult.Ok;
    }

    public static void LeaveChannel(Channel? c) => c?.Leave();
}
