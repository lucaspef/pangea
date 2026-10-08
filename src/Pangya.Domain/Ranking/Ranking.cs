using Pangya.Domain.Players;

namespace Pangya.Domain.Ranking;

/// <summary>Uma linha do ranking: posição atual (1-based), posição no retrato anterior (0 = nova) e valor.</summary>
public readonly record struct RankEntry(long Uid, int Position, int Previous, int Value);

/// <summary>Uma tabela (tipo, subtipo, classe) já ordenada.</summary>
public sealed class RankBoard
{
    public List<RankEntry> Entries { get; } = [];
    public Dictionary<long, int> IndexOf { get; } = [];
}

/// <summary>Quem aparece no ranking (nick e nível do momento do retrato).</summary>
public sealed record RankPlayer(long Uid, string Nickname, int Level);

/// <summary>Retrato completo, calculado de uma vez.</summary>
public sealed class RankingSnapshot
{
    public Dictionary<(int Type, int Sub, int Class), RankBoard> Boards { get; } = [];
    public Dictionary<long, RankPlayer> Players { get; } = [];
    public Dictionary<string, long> ByNick { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime At { get; init; }

    public RankBoard? Board(int type, int sub, int cls) => Boards.GetValueOrDefault((type, sub, cls));
}

/// <summary>Jogadores com estatística, sem inventário (Pangya.Data).</summary>
public interface IRankingStore
{
    Task<List<Player>> AllPlayersAsync();
}

/// <summary>
/// Ranking (docs/protocolo/SPEC-ranking.md §3, §6): 3 tipos (geral, por curso, recordes) × subtipos × 4 classes. As
/// fórmulas do servidor original não existem nas fontes; as daqui são propostas simples [P] (SPEC §6.2):
/// - geral: placar = (3 − média acima do par por buraco) × 1000; troféus = Σ (ouro 3, prata 2, bronze 1) × (faixa + 1);
///   pang ganho em partidas; buracos jogados; total = placar + troféus × 100 + buracos × 10 + pang / 100;
/// - curso: tacadas do melhor jogo de 18 buracos (72 + placar; menor é melhor; 0 esconderia a posição no cliente);
/// - recordes: albatross, hole-in-one, fairway por mil drives (o cliente mostra ×0,1 %), nível+1, distância total.
/// Valor 0 = fora da tabela. Empate: mais buracos jogados, depois uid. A posição anterior vem do retrato anterior.
/// </summary>
public sealed class RankingService(IRankingStore store)
{
    public const int Types = 3, Classes = 4, PageSize = 12;
    public static readonly int[] SubsPerType = [5, 17, 5];
    /// <summary>Subtipo do ranking por curso -> número do curso (GetRealCourseType do cliente).</summary>
    public static readonly int[] CourseBySub = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 14, 15, 16, 19];
    /// <summary>Classes: 0 todos, 1 até o nível 20, 2 de 21 a 40, 3 a partir de 41 [P: corte original desconhecido].</summary>
    public static bool InClass(int cls, int level) => cls switch { 1 => level <= 20, 2 => level is > 20 and <= 40, 3 => level > 40, _ => true };

    readonly SemaphoreSlim gate = new(1, 1);
    volatile RankingSnapshot? current;
    volatile bool refreshing;

    public RankingSnapshot? Current => current;
    /// <summary>Recalculando (os pedidos recebem 0x1F7).</summary>
    public bool Refreshing => refreshing;

    public async Task RefreshAsync()
    {
        await gate.WaitAsync();
        refreshing = true;
        try { current = Build(await store.AllPlayersAsync(), current, DateTime.UtcNow); }
        finally { refreshing = false; gate.Release(); }
    }

    /// <summary>Valor do jogador numa tabela (0 = fora). Ascendente só no ranking por curso.</summary>
    public static int Value(Player p, int type, int sub)
    {
        var t = p.Stats;
        static int Clamp(long v) => (int)Math.Clamp(v, 0, int.MaxValue);
        switch (type)
        {
            case 0:
                long score = t.Hole > 0 ? Math.Max(0, (long)Math.Round((3 - (double)t.TotalScore / t.Hole) * 1000)) : 0;
                long trophies = TrophyPoints(t);
                return sub switch
                {
                    1 => Clamp(score),
                    2 => Clamp(trophies),
                    3 => Clamp(t.PangEarned),
                    4 => Clamp(t.Hole),
                    _ => Clamp(score + trophies * 100 + t.Hole * 10 + t.PangEarned / 100),
                };
            case 1:
                if (sub < 0 || sub >= CourseBySub.Length || !p.Courses.TryGetValue(CourseBySub[sub], out var rec) || rec.BestScore == CourseRecord.NoRecord)
                    return 0;
                return Math.Max(72 + rec.BestScore, 1);
            default:
                return sub switch
                {
                    0 => Clamp(t.Albatross),
                    1 => Clamp(t.HoleInOne),
                    2 => t.Drive > 0 ? Clamp(t.Fairway * 1000 / t.Drive) : 0,
                    3 => p.Level + 1,
                    _ => Clamp(t.Distance),
                };
        }
    }

    static long TrophyPoints(PlayerStats t)
    {
        long sum = 0;
        for (int rank = 0; rank < Trophy.Ranks; rank++)
            for (int k = 0; k < Trophy.Kinds; k++)
            {
                int i = rank * Trophy.Kinds + k;
                if (i < t.Trophies.Length) sum += (long)t.Trophies[i] * (Trophy.Kinds - k) * (rank + 1);
            }
        return sum;
    }

    public static RankingSnapshot Build(List<Player> players, RankingSnapshot? previous, DateTime at)
    {
        var snap = new RankingSnapshot { At = at };
        foreach (var p in players)
        {
            if (p.Nickname.Length == 0) continue;
            snap.Players[p.AccountId] = new RankPlayer(p.AccountId, p.Nickname, p.Level);
            snap.ByNick[p.Nickname] = p.AccountId;
        }
        for (int type = 0; type < Types; type++)
            for (int sub = 0; sub < SubsPerType[type]; sub++)
            {
                var rows = new List<(Player P, int Value)>();
                foreach (var p in players)
                {
                    if (p.Nickname.Length == 0) continue;
                    int v = Value(p, type, sub);
                    if (v > 0) rows.Add((p, v));
                }
                bool asc = type == 1;
                rows.Sort((a, b) =>
                {
                    int c = asc ? a.Value.CompareTo(b.Value) : b.Value.CompareTo(a.Value);
                    if (c != 0) return c;
                    if (type == 2 && sub == 3 && a.P.Exp != b.P.Exp) return b.P.Exp.CompareTo(a.P.Exp);
                    c = b.P.Stats.Hole.CompareTo(a.P.Stats.Hole);
                    return c != 0 ? c : a.P.AccountId.CompareTo(b.P.AccountId);
                });
                for (int cls = 0; cls < Classes; cls++)
                {
                    var board = new RankBoard();
                    var old = previous?.Board(type, sub, cls);
                    foreach (var (p, v) in rows)
                    {
                        if (!InClass(cls, p.Level)) continue;
                        int prev = old != null && old.IndexOf.TryGetValue(p.AccountId, out int oi) ? old.Entries[oi].Position : 0;
                        board.IndexOf[p.AccountId] = board.Entries.Count;
                        board.Entries.Add(new RankEntry(p.AccountId, board.Entries.Count + 1, prev, v));
                    }
                    snap.Boards[(type, sub, cls)] = board;
                }
            }
        return snap;
    }

    public static int Pages(RankBoard? b) => b == null || b.Entries.Count == 0 ? 1 : (b.Entries.Count + PageSize - 1) / PageSize;
}
