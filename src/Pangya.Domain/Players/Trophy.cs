namespace Pangya.Domain.Players;

/// <summary>
/// Troféus do torneio (docs/protocolo/SPEC-resultado-fim-de-jogo.md §3.4). A sala tem um troféu do Match.iff pela média
/// de nível (0x2C000000 | faixa &lt;&lt; 16; faixas 0-5 amador 6º..1º, 6-12 pro 1º..7º); no fim, as primeiras posições
/// ganham ouro/prata/bronze conforme o número de jogadores e buracos, e o perfil conta quantos de cada faixa
/// (sTrophyStatistics = u16[13][3]).
/// </summary>
public static class Trophy
{
    public const int MatchBase = 0x2C000000, Ranks = 13, Kinds = 3, Count = Ranks * Kinds;
    public const int None = 0, Gold = 1, Silver = 2, Bronze = 3;

    /// <summary>Troféu da sala pela média de nível dos jogadores (5 níveis por faixa, como o GB).</summary>
    public static int RoomTid(IReadOnlyList<int> levels)
    {
        if (levels.Count == 0) return MatchBase;
        long sum = 0;
        foreach (var lv in levels) sum += Math.Max(lv, 0);
        int rank = (int)Math.Min(sum / levels.Count / 5, Ranks - 1);
        return MatchBase | (rank << 16);
    }

    /// <summary>Faixa do troféu de torneio (0..12); -1 para os especiais (0x2D.., 0x2E.., 0x2F..), que são do mesmo grupo.</summary>
    public static int RankOf(int roomTid) => (roomTid & unchecked((int)0xFF00FFFF)) == MatchBase ? (roomTid >> 16) & 0xFF : -1;

    /// <summary>
    /// Troféu de cada posição (índice 0 = 1º) para quantos jogadores terminaram (sem quem saiu): 18 buracos a partir de
    /// 10 jogadores, 9 buracos a partir de 15 (regras do GB, Tourney.cs).
    /// </summary>
    public static int[] ByPosition(int players, int holes)
    {
        if (holes >= 18)
            return players switch
            {
                >= 27 => [Gold, Silver, Silver, Bronze, Bronze, Bronze],
                >= 23 => [Gold, Silver, Bronze, Bronze],
                >= 19 => [Gold, Silver, Bronze],
                >= 15 => [Silver, Bronze],
                >= 10 => [Bronze],
                _ => [],
            };
        if (holes >= 9)
            return players switch
            {
                >= 27 => [Gold, Silver, Bronze],
                >= 19 => [Silver, Bronze],
                >= 15 => [Bronze],
                _ => [],
            };
        return [];
    }

    /// <summary>Soma um troféu na contagem do perfil (ignora troféu fora das 13 faixas ou tipo inválido).</summary>
    public static void Add(int[] counts, int roomTid, int kind)
    {
        int rank = RankOf(roomTid);
        if (rank is < 0 or >= Ranks || kind is < Gold or > Bronze || counts.Length < Count) return;
        ref int c = ref counts[rank * Kinds + kind - 1];
        if (c < ushort.MaxValue) c++;
    }
}
