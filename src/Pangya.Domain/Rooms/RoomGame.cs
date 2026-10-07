namespace Pangya.Domain.Rooms;

/// <summary>Resultado de uma tacada como o cliente reporta (já decifrado pela camada de protocolo).</summary>
public readonly record struct ShotResult(uint Guid, float X, float Y, float Z, byte State, uint Pang, uint BonusPang)
{
    public const byte StateWaterOrOut = 3, StateHoled = 4;
}

/// <summary>Linha do placar final. Score = tacadas vs par (stroke) ou buracos ganhos (match/team); Net = pang ganho/perdido (pang battle).</summary>
public readonly record struct GameResult(uint Guid, int Rank, int Score, int TotalStrokes, uint Pang, uint BonusPang, long Net = 0);

public enum GameEndKind { Stroke, Team, Match, PangBattle }

/// <summary>Fim de partida por vez.</summary>
public sealed class GameEnd
{
    public GameEndKind Kind { get; init; }
    public List<GameResult> Results { get; init; } = [];
    /// <summary>Team: buracos ganhos por lado e o vencedor (0/1, 2 = empate).</summary>
    public int[] SideWins { get; init; } = [0, 0];
    public int Winner { get; init; }
    /// <summary>Pang battle: vencedor do último buraco e o vencedor geral (0xFFFFFFFF = ninguém).</summary>
    public uint LastHoleWinner { get; init; } = 0xFFFFFFFF;
    public uint OverallWinner { get; init; } = 0xFFFFFFFF;
}

/// <summary>Dados de um buraco que o cliente manda ao carregar (par, tee e bandeira).</summary>
public readonly record struct HoleInfo(byte Par, float TeeX, float TeeZ, float PinX, float PinZ);

/// <summary>Base de uma partida: sala, temporizadores e fim. Todos os métodos rodam sob o lock <see cref="RoomManager.Sync"/>.</summary>
public abstract class RoomGame(Room room, object sync)
{
    public const int GiveUpOverPar = 4;

    readonly CancellationTokenSource cts = new();
    protected readonly Random Rng = new();

    public Room Room { get; } = room;
    public bool Over { get; private set; }
    public int HoleCount { get; } = Math.Clamp((int)room.Settings.Holes, 1, 18);
    /// <summary>Par, tee e bandeira de cada buraco (vêm do cliente, 0x1A).</summary>
    public Dictionary<byte, HoleInfo> Holes { get; } = [];

    public void HoleData(byte hole, HoleInfo info) => Holes[hole] = info;
    public int ParOf(byte hole) => Holes.TryGetValue(hole, out var h) ? h.Par : 4;
    public byte HoleAt(int index) => index < Room.HoleOrder.Length ? Room.HoleOrder[index] : (byte)(index + 1);

    public abstract void PlayerLeft(RoomPlayer rp);

    public virtual void Cancel()
    {
        Over = true;
        cts.Cancel();
    }

    /// <summary>Roda a ação depois do atraso, sob o lock da sala, se a partida ainda não acabou.</summary>
    protected void Later(TimeSpan delay, Action action)
    {
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { return; }
            lock (sync)
                if (!Over) action();
        });
    }
}
