using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645;

/// <summary>Dados de jogo do cliente 645 (pangya.iff) vistos pelo Domain.</summary>
public sealed class Kr645GameData : IGameData
{
    readonly HashSet<int> typeIds = [];
    readonly HashSet<int> parts = [];
    readonly Dictionary<int, UccPartInfo> ucc = [];

    public Kr645Iff Iff { get; }

    public Kr645GameData(Kr645Iff iff)
    {
        Iff = iff;
        foreach (var p in iff.Parts)
        {
            parts.Add((int)p.c.TypeId);
            if (p.Category is 7 or 8 or 9) ucc[(int)p.c.TypeId] = UccInfo(p);
        }
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

    Dictionary<uint, Iff.sChar>? charStats;
    Dictionary<uint, Iff.sPart>? partStats;
    Dictionary<uint, Iff.sClubSet>? clubStats;
    Dictionary<uint, Iff.sCaddie>? caddieStats;
    Dictionary<uint, Iff.sAuxPart>? auxStats;

    public (int[] Stats, int DriveUp) PlayStats(Player p)
    {
        if (charStats == null)
        {
            var c = new Dictionary<uint, Iff.sChar>(); foreach (var x in Iff.Characters) c[x.c.TypeId] = x;
            var pt = new Dictionary<uint, Iff.sPart>(); foreach (var x in Iff.Parts) pt[x.c.TypeId] = x;
            var cs = new Dictionary<uint, Iff.sClubSet>(); foreach (var x in Iff.ClubSets) cs[x.c.TypeId] = x;
            var cd = new Dictionary<uint, Iff.sCaddie>(); foreach (var x in Iff.Caddies) cd[x.c.TypeId] = x;
            var ax = new Dictionary<uint, Iff.sAuxPart>(); foreach (var x in Iff.AuxParts) ax[x.c.TypeId] = x;
            (partStats, clubStats, caddieStats, auxStats, charStats) = (pt, cs, cd, ax, c);
        }
        var stats = new int[5];
        int driveUp = 0;
        var cap = new int[5];
        var lvl = new int[5];
        if (p.Character is { } ch && charStats.TryGetValue((uint)ch.TypeId, out var cinfo))
        {
            var upg = ch.IntArray("pcl", 5);
            for (int t = 0; t < 5; t++) { cap[t] = cinfo.Attr[t]; lvl[t] = cinfo.PCL[t] + upg[t]; }
            if (p.Level >= 6) cap[0] += (p.Level - 1) / 5;
            foreach (var tid in ch.IntArray("parts", 24))
                if (tid != 0 && partStats!.TryGetValue((uint)tid, out var part))
                    for (int t = 0; t < 5; t++) { cap[t] += part.Slot[t] + part.Attr[t]; lvl[t] += part.Attr[t]; }
            foreach (var tid in ch.IntArray("aux", 5))
                if (tid != 0 && auxStats!.TryGetValue((uint)tid, out var aux))
                {
                    for (int t = 0; t < 5; t++) { cap[t] += aux.Attr[t] + aux.Slot[t]; lvl[t] += aux.Attr[t]; }
                    driveUp += aux.DriveUp;
                }
            for (int t = 0; t < 5; t++) stats[t] = Math.Min(lvl[t], cap[t]);
        }
        if (p.Find(p.Equip.ClubSetId) is { } club && clubStats!.TryGetValue((uint)club.TypeId, out var cset))
        {
            var upg = club.IntArray("pcl", 5);
            for (int t = 0; t < 5; t++) stats[t] += cset.Attr[t] + upg[t];
        }
        if (p.Find(p.Equip.CaddieId) is { } cad && caddieStats!.TryGetValue((uint)cad.TypeId, out var cdi) && cdi.c.Level <= p.Level)
            for (int t = 0; t < 5; t++) stats[t] += cdi.Attr[t];
        stats[0] = Math.Clamp(stats[0], 0, 100) - 15;
        int penalty = Math.Max(0, stats[0] - (PowerPenalty(p.Level) + 5));
        stats[1] = Math.Clamp(stats[1] - penalty, 0, 30);
        stats[2] = Math.Clamp(stats[2] - penalty, 0, 30);
        stats[3] = Math.Clamp(stats[3], 1, 30);
        stats[4] = Math.Clamp(stats[4], 1, 30);
        return (stats, driveUp);
    }

    /// <summary>CalcPowerPenalty: 0 até o nível 5, +1 a cada 5 níveis, 13 a partir do 66.</summary>
    static int PowerPenalty(int level) => level <= 5 ? 0 : Math.Min(level / 5, 13);

    Dictionary<int, byte>? salable;

    public bool CanTrade(int typeId)
    {
        if (salable == null)
        {
            var d = new Dictionary<int, byte>();
            void Add(in IFF_ITEM_COMMON c) => d[(int)c.TypeId] = (byte)c.IsSalable;
            foreach (var x in Iff.Parts) Add(x.c);
            foreach (var x in Iff.ClubSets) Add(x.c);
            foreach (var x in Iff.Balls) Add(x.c);
            foreach (var x in Iff.Items) Add(x.c);
            salable = d;
        }
        return salable.TryGetValue(typeId, out var s) && s is 1 or 3;
    }

    Dictionary<int, (int Ability, float Rate)>? spItems;

    public IReadOnlyDictionary<int, (int Ability, float Rate)> SpItems
    {
        get
        {
            if (spItems != null) return spItems;
            var d = new Dictionary<int, (int, float)>();
            foreach (var x in Iff.Archive.Table<Iff.sSpecialPrizeItem>("SpecialPrizeItem.iff"))
                if (x.ability <= 4) d[(int)x.typeId] = ((int)x.ability, x.rate);
            return spItems = d;
        }
    }

    public float CourseStars(int course)
    {
        foreach (var c in Iff.Courses)
            if (c.c.TypeId == (0x28000000u | (uint)(course & 0xFF))) return Math.Max(c.Difficulty, 1u);
        return 1;
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

    List<MagicBoxRecipe>? magicBox;
    Dictionary<int, MagicBoxOutput[]>? magicBoxRandom;

    /// <summary>CadieMagicBox.iff na posição uiNumber-1 (o arquivo vem ordenado e contíguo; buracos ficam sem receita).</summary>
    public IReadOnlyList<MagicBoxRecipe> MagicBox
    {
        get
        {
            if (magicBox != null) return magicBox;
            int max = 0;
            foreach (var r in Iff.MagicBox) max = Math.Max(max, (int)r.uiNumber);
            var list = new List<MagicBoxRecipe>(max);
            var byNumber = new Dictionary<int, MagicBoxRecipe>();
            foreach (var r in Iff.MagicBox)
            {
                int n = 0;
                while (n < 4 && r.uiElem[n] != 0) n++;
                var elems = new int[n];
                var counts = new int[n];
                for (int i = 0; i < n; i++) { elems[i] = (int)r.uiElem[i]; counts[i] = (int)Math.Max(r.uiElemCount[i], 1u); }
                byNumber[(int)r.uiNumber] = new MagicBoxRecipe((int)r.uiNumber - 1, r.iLevel, (int)r.uiOutput,
                    (int)Math.Max(r.uiOutputCount, 1u), elems, counts, (int)r.uiRandSeq);
            }
            for (int i = 1; i <= max; i++)                              // receita ausente: sem materiais e sem saída
                list.Add(byNumber.GetValueOrDefault(i) ?? new MagicBoxRecipe(i - 1, int.MaxValue, 0, 1, [], [], 0));
            return magicBox = list;
        }
    }

    public IReadOnlyDictionary<int, MagicBoxOutput[]> MagicBoxRandom
    {
        get
        {
            if (magicBoxRandom != null) return magicBoxRandom;
            var groups = new Dictionary<int, List<MagicBoxOutput>>();
            foreach (var r in Iff.MagicBoxRandom)
            {
                if (!groups.TryGetValue((int)r.uiRandSeq, out var g)) groups[(int)r.uiRandSeq] = g = [];
                g.Add(new MagicBoxOutput((int)r.uiTypeId, (int)r.uiCount, (int)r.uiProbs));
            }
            var d = new Dictionary<int, MagicBoxOutput[]>(groups.Count);
            foreach (var (k, v) in groups) d[k] = v.ToArray();
            return magicBoxRandom = d;
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

    public UccPartInfo? UccPart(int typeId) => ucc.GetValueOrDefault(typeId);

    /// <summary>IsUccClothes / IsSameClothes (ucclibrary.c): textura base e a chave Data + Tex[0..2] + OrgTex[0..2].</summary>
    static UccPartInfo UccInfo(Iff.sPart p)
    {
        var key = new System.Text.StringBuilder();
        var data = p.Data;
        key.Append(Core.Text.Cp949.Read(data));
        string tex0 = "";
        for (int i = 0; i < 3; i++)
        {
            var t = p.Tex[i];
            var o = p.OrgTex[i];
            string ts = Core.Text.Cp949.Read(t);
            if (i == 0) tex0 = ts;
            key.Append('|').Append(ts).Append('|').Append(Core.Text.Cp949.Read(o));
        }
        return new UccPartInfo(p.Category, tex0, key.ToString().ToLowerInvariant());
    }

    /// <summary>CItemManager::GetDefCombo (itemmanager.cpp:2737): 0x08000400 | índice&lt;&lt;18 | slot&lt;&lt;13, se existir no Part.iff.</summary>
    public bool IsDefaultPart(int characterTypeId, int partTypeId) =>
        parts.Contains(partTypeId) && ((partTypeId >> 18) & 0xFF) == (characterTypeId & 0xFF)
        && (partTypeId & 0x1FF) == 0 && (partTypeId & 0xC00) == 0x400;

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
