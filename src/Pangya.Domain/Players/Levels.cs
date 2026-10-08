namespace Pangya.Domain.Players;

/// <summary>
/// Níveis do jogador (CSharedDoc::LoadLevelTable = shared/sharedtables.h). ExpNeed[L] = EXP para ir do nível L ao L+1;
/// o Exp guardado é o EXP dentro do nível atual (o cliente mostra "exp / ExpNeed[nível]").
/// </summary>
public static class Levels
{
    public const int Max = 70;

    static readonly int[] ExpNeed =
    [
        30, 40, 50, 60, 70, 100, 133, 148, 163, 178,
        213, 267, 288, 309, 330, 491, 660, 716, 772, 828,
        1224, 1656, 1742, 1828, 1914, 2346, 2871, 2994, 3117, 3240,
        6480, 4536, 4684, 4832, 4980, 14940, 6972, 7168, 7364, 7560,
        15120, 9072, 9322, 9572, 9822, 19644, 9822, 10122, 10422, 10722,
        21444, 10722, 11122, 11522, 11922, 23844, 23844, 24394, 24944, 25494,
        50988, 101978, 203952, 407904, 815808, 1631616, 1631616, 1631616, 1631616, 1631616, 1631616,
    ];

    /// <summary>"Bolsa de pang": nos presentes, quantidade = pang.</summary>
    public const int PangPouch = 0x1A000010;

    /// <summary>
    /// Presente de subida de nível que o cliente mostra (s_levelUpGift, levelupitemdlg.cpp; docs/protocolo/
    /// SPEC-resultado-fim-de-jogo.md §2.6), pelo nível alcançado; 21 em diante = 1 cartão de raspadinha (evento).
    /// </summary>
    public static (int TypeId, int Qty)[] Gifts(int level) => level == 1 ? [(0x18000008, 1), (0x18000007, 1)]
        : level <= 0 ? [] : [Gift(level)];

    static (int TypeId, int Qty) Gift(int level) => level switch
    {
        2 => (0x18000005, 10), 3 => (0x1A000011, 20), 4 => (0x18000004, 10), 5 => (0x1A00000F, 5),
        6 => (PangPouch, 3000), 7 => (0x18000010, 5), 8 => (0x70000002, 18), 9 => (0x1A000028, 5), 10 => (0x1A000002, 5),
        11 => (PangPouch, 5000), 12 => (0x18000011, 10), 13 => (0x70000003, 18), 14 => (0x18000025, 10), 15 => (0x1A000002, 5),
        16 => (PangPouch, 10000), 17 => (0x7CC00003, 1), 18 => (0x1A00003D, 3), 19 => (0x18000025, 20), 20 => (0x1A000002, 5),
        _ => (0x1A000033, 1),
    };

    public static int Need(int level) => ExpNeed[Math.Clamp(level, 0, Max)];

    /// <summary>Soma EXP subindo de nível quando passa do necessário; devolve quantos níveis subiu.</summary>
    public static int AddExp(Player p, int amount)
    {
        int before = p.Level;
        int lv = Math.Clamp(p.Level, 0, Max);
        long exp = Math.Max(0, p.Exp) + Math.Max(0, amount);
        while (lv < Max && exp >= Need(lv))
        {
            exp -= Need(lv);
            lv++;
        }
        p.Level = lv;
        p.Exp = (int)Math.Min(exp, Need(lv) - 1);
        return lv - before;
    }
}
