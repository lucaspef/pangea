using Pangya.Core.Config;

namespace Pangya.Domain.Game;

/// <summary>
/// Treasure Hunter (SPEC-treasure-hunter.md): pontos da partida pelo resultado de cada buraco (teto 1000), número de
/// caixas no fim pelos pontos e o sorteio dos prêmios (lista da configuração, por peso). O servidor calcula tudo; o
/// cliente só mostra.
/// </summary>
public static class TreasureHunter
{
    public const int MaxPoints = 1000, MaxBoxes = 24;
    /// <summary>Saco de pang: o cliente soma a quantidade no pang dele.</summary>
    public const int PangTid = 0x1A000010;

    /// <summary>Pontos de um buraco (GB calcPointNormal): HIO/−3 100, −2 50, −1 30, par 15, +1 10, +2 7, +3 4, +4 1.</summary>
    public static int HolePoints(int strokes, int par)
    {
        if (strokes <= 0) return 0;
        if (strokes == 1) return 100;
        return (strokes - par) switch
        {
            <= -3 => 100, -2 => 50, -1 => 30, 0 => 15, 1 => 10, 2 => 7, 3 => 4, 4 => 1, _ => 0,
        };
    }

    /// <summary>Faixas de caixas pelos pontos (GB): até N pontos -> mínimo..máximo.</summary>
    static readonly (int UpTo, int Min, int Max)[] Boxes =
        [(100, 1, 2), (200, 2, 4), (300, 3, 5), (400, 3, 7), (500, 4, 8), (600, 4, 10), (700, 5, 11), (800, 5, 13),
         (900, 6, 14), (999, 6, 18), (MaxPoints, 12, 24)];

    /// <summary>Caixas da partida: faixa dos pontos × taxa do servidor (%), entre 1 e 24 (0 sem pontos).</summary>
    public static int BoxCount(int points, int ratePercent, Random rng)
    {
        if (points <= 0 || ratePercent <= 0) return 0;
        points = Math.Min(points, MaxPoints);
        var (_, min, max) = Boxes[^1];
        foreach (var b in Boxes)
            if (points <= b.UpTo) { (min, max) = (b.Min, b.Max); break; }
        int n = rng.Next(min, max + 1) * ratePercent / 100;
        return Math.Clamp(n, 1, MaxBoxes);
    }

    /// <summary>Sorteia n prêmios por peso (só os aceitos por <paramref name="allowed"/>); quantidade entre Min e Max.</summary>
    public static List<(int TypeId, int Count)> Draw(int n, IReadOnlyList<TreasurePrize> prizes, Func<int, bool> allowed, Random rng)
    {
        var pool = new List<TreasurePrize>();
        int total = 0;
        foreach (var p in prizes)
            if (p.Weight > 0 && p.Max >= 1 && allowed(p.TypeId)) { pool.Add(p); total += p.Weight; }
        var result = new List<(int, int)>(n);
        if (total == 0) return result;
        for (int i = 0; i < n; i++)
        {
            int roll = rng.Next(total);
            foreach (var p in pool)
            {
                if (roll < p.Weight)
                {
                    int lo = Math.Clamp(p.Min, 1, p.Max);
                    result.Add((p.TypeId, Math.Min(rng.Next(lo, p.Max + 1), ushort.MaxValue)));
                    break;
                }
                roll -= p.Weight;
            }
        }
        return result;
    }
}
