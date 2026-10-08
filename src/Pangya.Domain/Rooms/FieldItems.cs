namespace Pangya.Domain.Rooms;

/// <summary>
/// Itens de campo do Wiz City (curso 0x13; SPEC-modes.md "Wiz City"): moedas de pang e caixas. O servidor manda só a
/// semente e a lista tipo/índice por buraco (no fim do 0x50); as posições cada cliente gera a partir da semente.
/// Ao pegar, o 0x1C traz {tipo, índice, quantidade, textura}: vale só a cópia de quem tacou, conferida contra a
/// tabela e nunca duas vezes (jogador, buraco, índice).
/// </summary>
public sealed class FieldItems
{
    public const byte WizCity = 0x13;
    public const int Coin = 0, Box = 1;
    /// <summary>A caixa do campo dá 1 Spin Cube (aberto depois no My Room com uma Lucky Key; Shop.SpinCubeService).</summary>
    public const int BoxPrize = Shop.SpinCubeService.SpinCube;

    readonly HashSet<(uint, byte, uint)> taken = [];
    readonly Dictionary<uint, List<int>> won = [];

    /// <summary>Itens que o jogador ganhou nas caixas (para a lista de itens da tela de resultado: 0xF8/0xCC).</summary>
    public IReadOnlyList<int> WonBy(uint guid) => won.TryGetValue(guid, out var l) ? l : [];
    public uint Seed { get; }
    /// <summary>Por buraco (1..18): tipo de cada índice.</summary>
    public Dictionary<byte, int[]> PerHole { get; } = [];

    FieldItems(uint seed) => Seed = seed;

    /// <summary>Tabela da partida; null fora do Wiz City (o 0x50 manda tudo zerado).</summary>
    public static FieldItems? For(byte course, Random rng)
    {
        if (course != WizCity) return null;
        var f = new FieldItems((uint)rng.Next());
        for (byte hole = 1; hole <= 18; hole++)
        {
            // quantidades por buraco [suposição: CubeCoinSystem.getAllCoinCubeInHoleWizCity do servidor S9]
            var (boxes, total) = hole switch { 3 or 12 => (5, 60), 14 => (2, 48), 18 => (3, 33), _ => (0, 20) };
            var types = new int[total];
            for (int i = 0; i < total; i++) types[i] = i < boxes ? Box : Coin;
            f.PerHole[hole] = types;
        }
        return f;
    }

    /// <summary>Um item reportado; devolve o prêmio (pang &gt; 0, ou o Spin Cube da caixa) ou null se inválido/repetido.</summary>
    public (int Pang, int ItemTypeId)? Take(uint guid, byte hole, int type, uint index, int texture, Random rng)
    {
        if (!PerHole.TryGetValue(hole, out var types) || index >= types.Length || types[index] != type) return null;
        if (!taken.Add((guid, hole, index))) return null;
        if (type == Coin) return (rng.Next(1, (texture == 1 ? 50 : 200) + 1), 0);
        if (!won.TryGetValue(guid, out var list)) won[guid] = list = [];
        list.Add(BoxPrize);
        return (0, BoxPrize);
    }
}
