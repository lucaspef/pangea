namespace Pangya.Domain.Shop;

/// <summary>
/// Um item do catálogo (vem dos dados do cliente; cada versão monta a partir do seu IFF).
/// Só o que o servidor precisa para cobrar e entregar: o cliente não manda preço.
/// </summary>
public sealed class ShopItem
{
    public int TypeId { get; init; }
    public uint Price { get; init; }
    public uint SalePrice { get; init; }
    /// <summary>true = cobra em cookies; false = em pang.</summary>
    public bool IsCash { get; init; }
    /// <summary>0 nenhum, 1 compra+presente, 2 só presente, 3 só compra, 4 vitrine, 5/6 escondido.</summary>
    public int InStock { get; init; }
    /// <summary>Itens usáveis: quantas unidades o preço cobre (COM[0]); 0 = preço por unidade.</summary>
    public int PackSize { get; init; }
    /// <summary>Preço por período (1, 7, 15, 30, 365 dias) de caddie item, skin e mascote; 0 = não vende.</summary>
    public int[] PeriodPrices { get; init; } = [];
    /// <summary>Set item: (typeid, quantidade) de cada peça.</summary>
    public (int TypeId, int Count)[] SetElements { get; init; } = [];
    /// <summary>Cor de cabelo: cor e personagem (índice) a que se aplica.</summary>
    public byte HairColor { get; init; }
    public byte HairCharacter { get; init; }
    /// <summary>Móvel: posição inicial (x, y, z, rotação).</summary>
    public float[] Position { get; init; } = [];
    /// <summary>Club set: upgrades máximos por atributo (Slot - Attr).</summary>
    public int[] UpgradeSlots { get; init; } = [];
    /// <summary>Skin vendida por unidade (não por período).</summary>
    public bool UnitPricedSkin { get; init; }

    /// <summary>Preço que não é "não está à venda" (o cliente usa 10.000.000 para isso).</summary>
    public const uint NotForSale = 10_000_000;

    public uint UnitPrice => SalePrice > 0 && SalePrice < Price ? SalePrice : Price;
}
