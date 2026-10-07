using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645;

/// <summary>Dados de jogo do cliente 645 (pangya.iff) vistos pelo Domain.</summary>
public sealed class Kr645GameData : IGameData
{
    readonly HashSet<int> typeIds = [];
    readonly HashSet<int> parts = [];

    public Kr645Iff Iff { get; }

    public Kr645GameData(Kr645Iff iff)
    {
        Iff = iff;
        foreach (var p in iff.Parts) parts.Add((int)p.c.TypeId);
        typeIds.UnionWith(parts);
        foreach (var x in iff.Characters) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Clubs) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.ClubSets) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Balls) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Items) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Caddies) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.CaddieItems) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.SetItems) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Skins) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.HairStyles) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Mascots) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.AuxParts) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Cards) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Furniture) typeIds.Add((int)x.c.TypeId);
    }

    public static Kr645GameData Load(string iffPath) => new(Kr645Iff.Load(iffPath));

    /// <summary>Ordem da tela de escolha de mapa (mapselectdlg.cpp:14, sem o 0x7F = aleatório).</summary>
    static readonly byte[] MapOrder = [0x13, 0x10, 0x0F, 0x0E, 0x0D, 0x0B, 0x08, 0x0A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x09];
    List<byte>? courses;

    /// <summary>Mapas que o cliente mostra: estão na ordem fixa e têm registro ativo (Final) no Course.iff (typeid 0x28000000 | mapa).</summary>
    public IReadOnlyList<byte> Courses
    {
        get
        {
            if (courses != null) return courses;
            var active = new HashSet<uint>();
            foreach (var c in Iff.Courses)
                if (c.c.Final != 0) active.Add(c.c.TypeId);
            var list = new List<byte>();
            foreach (var m in MapOrder)
                if (active.Contains(0x28000000u | m)) list.Add(m);
            return courses = list;
        }
    }

    Dictionary<int, CardInfo>? cards;

    public IReadOnlyDictionary<int, CardInfo> Cards
    {
        get
        {
            if (cards != null) return cards;
            var d = new Dictionary<int, CardInfo>();
            foreach (var c in Iff.Cards)
                d[(int)c.c.TypeId] = new CardInfo((int)c.c.TypeId, c.c.Final != 0, c.RareType, c.Avility, c.AvilityValue, c.UseTime, c.Volume);
            return cards = d;
        }
    }

    Dictionary<int, ShopItem>? shop;
    Dictionary<uint, int>? enchant;

    public ShopItem? GetShopItem(int typeId) => (shop ??= BuildShop()).GetValueOrDefault(typeId);

    /// <summary>Enchant.iff: chave 0x34000000 | atributo&lt;&lt;20 | nível atual -> pang (realmyroomtask L5598).</summary>
    public int? UpgradePrice(int stat, int current)
    {
        if (enchant == null)
        {
            enchant = [];
            foreach (var e in Iff.Enchants) enchant[e.TypeId] = (int)e.ReqdPang;
        }
        return enchant.TryGetValue(0x34000000u | (uint)(stat << 20) | (uint)current, out var p) ? p : null;
    }

    static ShopItem Common(in IFF_ITEM_COMMON c) => new()
    {
        TypeId = (int)c.TypeId, Price = c.Price, SalePrice = c.SalePrice, IsCash = c.IsCash != 0, InStock = (int)c.InStock,
    };

    static int[] Ints(ReadOnlySpan<short> s)
    {
        var a = new int[s.Length];
        for (int i = 0; i < s.Length; i++) a[i] = s[i];
        return a;
    }

    /// <summary>Catálogo com os campos que CItemManager::GetItemSalePrice (itemmanager.cpp:2833) e a loja usam.</summary>
    Dictionary<int, ShopItem> BuildShop()
    {
        var d = new Dictionary<int, ShopItem>();
        void Add(ShopItem s) => d[s.TypeId] = s;
        foreach (var x in Iff.Characters) Add(Common(x.c));
        foreach (var x in Iff.Parts) Add(Common(x.c));
        foreach (var x in Iff.Clubs) Add(Common(x.c));
        foreach (var x in Iff.Balls) Add(Common(x.c));
        foreach (var x in Iff.Caddies) Add(Common(x.c));
        foreach (var x in Iff.AuxParts) Add(Common(x.c));
        foreach (var x in Iff.Cards) Add(Common(x.c));
        foreach (var x in Iff.ClubSets)
        {
            var s = Common(x.c);
            var slots = new int[5];
            for (int i = 0; i < 5; i++) slots[i] = Math.Max(0, x.Slot[i] - x.Attr[i]);
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, UpgradeSlots = slots });
        }
        foreach (var x in Iff.Items)
        {
            var s = Common(x.c);
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, PackSize = x.COM[0] });
        }
        foreach (var x in Iff.CaddieItems)
        {
            var s = Common(x.c);
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, PeriodPrices = Ints(x.COM) });
        }
        foreach (var x in Iff.Skins)
        {
            var s = Common(x.c);
            Add(new ShopItem
            {
                TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock,
                PeriodPrices = Ints(x.COM), UnitPricedSkin = (s.TypeId & 0x3C00000) == 0x1800000,
            });
        }
        foreach (var x in Iff.Mascots)
        {
            var s = Common(x.c);
            var prices = new int[5];
            for (int i = 0; i < 5; i++) prices[i] = (sbyte)x.COM[i];      // char COM[5]
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, PeriodPrices = prices });
        }
        foreach (var x in Iff.SetItems)
        {
            var s = Common(x.c);
            var elems = new List<(int, int)>();
            for (int i = 0; i < Math.Min((int)x.nElems, 10); i++)
                if (x.ElemIds[i] != 0) elems.Add(((int)x.ElemIds[i], Math.Max((int)x.ElemNum[i], 1)));
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, SetElements = [.. elems] });
        }
        foreach (var x in Iff.HairStyles)
        {
            var s = Common(x.c);
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, HairColor = x.cHairID, HairCharacter = x.cCharID });
        }
        foreach (var x in Iff.Furniture)
        {
            var s = Common(x.c);
            Add(new ShopItem { TypeId = s.TypeId, Price = s.Price, SalePrice = s.SalePrice, IsCash = s.IsCash, InStock = s.InStock, Position = [x.Pos[0], x.Pos[1], x.Pos[2], x.Pos[3]] });
        }
        return d;
    }

    public bool Exists(int typeId) => typeIds.Contains(typeId);

    /// <summary>CItemManager::GetDefCombo (itemmanager.cpp:2737): 0x08000400 | índice&lt;&lt;18 | slot&lt;&lt;13, se existir no Part.iff.</summary>
    public int[] DefaultParts(int characterTypeId)
    {
        int idx = characterTypeId & 0xFF;
        var result = new int[24];
        for (int slot = 0; slot < 24; slot++)
        {
            int t = 0x08000400 | (idx << 18) | (slot << 13);
            result[slot] = parts.Contains(t) ? t : 0;
        }
        return result;
    }
}
