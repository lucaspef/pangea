using System.Text.Json.Nodes;

namespace Pangya.Domain.Players;

/// <summary>Grupo do item = typeid >> 26 (itemmanager.cpp:2484); igual em todas as versões do cliente.</summary>
public enum ItemGroup
{
    Character = 1, Part = 2, Club = 3, ClubSet = 4, Ball = 5, Usable = 6, Caddie = 7, CaddieItem = 8, SetItem = 9,
    Skin = 0xE, HairColor = 0xF, Mascot = 0x10, Furniture = 0x12, Card = 0x1F,
}

/// <summary>Onde o objeto está.</summary>
public enum ItemLocation : short { Inventory = 0, Locker = 1, ActiveCard = 2 }

/// <summary>Um objeto do jogo de um jogador. Id único global (sequência do banco).</summary>
public sealed class Item
{
    public int Id { get; init; }
    public int TypeId { get; init; }
    public int Quantity { get; set; } = 1;
    /// <summary>Atributos que dependem do tipo (partes do personagem, upgrades, mensagem do mascote...).</summary>
    public JsonObject Attrs { get; set; } = [];
    public DateTime? ExpiresAt { get; set; }
    public ItemLocation Location { get; set; }

    /// <summary>Cópia independente (atributos inclusive), para mudar sem afetar o original até salvar.</summary>
    public Item Clone() => new() { Id = Id, TypeId = TypeId, Quantity = Quantity, Attrs = (System.Text.Json.Nodes.JsonObject)Attrs.DeepClone(), ExpiresAt = ExpiresAt, Location = Location };

    public ItemGroup Group => GroupOf(TypeId);

    /// <summary>
    /// Bola básica (Pangya Aztec). Nunca é gasta: o cliente volta para ela quando a bola equipada acaba ou não
    /// está no inventário (lobbymain.cpp:12337), então o servidor também nunca desconta essa bola.
    /// </summary>
    public const int BasicBall = 0x14000000;
    public bool IsConsumable => Group is ItemGroup.Ball or ItemGroup.Usable && TypeId != BasicBall;
    public static ItemGroup GroupOf(int typeId) => (ItemGroup)((uint)typeId >> 26);

    public int[] IntArray(string key, int length)
    {
        var a = new int[length];
        if (Attrs[key] is JsonArray arr)
            for (int i = 0; i < Math.Min(length, arr.Count); i++) a[i] = arr[i]?.GetValue<int>() ?? 0;
        return a;
    }

    public int Int(string key) => Attrs[key]?.GetValue<int>() ?? 0;
    public void Set(string key, int[] values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        Attrs[key] = arr;
    }
    public void Set(string key, int value) => Attrs[key] = value;
}

/// <summary>O que o jogador está usando. Personagem/caddie/club/mascote por id; bola, itens e skins por typeid.</summary>
public sealed class Equipment
{
    public int CharacterId { get; set; }
    public int CaddieId { get; set; }
    public int ClubSetId { get; set; }
    public int BallTypeId { get; set; }
    public int[] ItemSlots { get; set; } = new int[10];
    public int[] SkinTypeIds { get; set; } = new int[6];
    public int MascotId { get; set; }
}

/// <summary>Dados de jogo de uma conta, carregados ao entrar no game server.</summary>
public sealed class Player
{
    public long AccountId { get; init; }
    public string Login { get; init; } = "";
    public string Nickname { get; init; } = "";
    public int IdentityFlags { get; init; }
    public int Level { get; set; }
    public int Exp { get; set; }
    public long Pang { get; set; }
    public long Cookie { get; set; }
    public long LockerPang { get; set; }
    public int Flags { get; set; }
    public Equipment Equip { get; set; } = new();
    /// <summary>Todos os objetos do jogador, por id.</summary>
    public Dictionary<int, Item> Items { get; } = [];

    public const int FlagTutorialDone = 1;

    public void Add(Item it) => Items[it.Id] = it;
    public Item? Find(int id) => Items.GetValueOrDefault(id);

    /// <summary>Itens de um grupo, em ordem de id (ordem de criação).</summary>
    public List<Item> OfGroup(ItemGroup g)
    {
        var list = new List<Item>();
        foreach (var it in Items.Values)
            if (it.Group == g && it.Location == ItemLocation.Inventory) list.Add(it);
        list.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    /// <summary>Personagem equipado (ou o primeiro, se o equipado sumiu).</summary>
    public Item? Character
    {
        get
        {
            if (Find(Equip.CharacterId) is { } c) return c;
            var all = OfGroup(ItemGroup.Character);
            return all.Count > 0 ? all[0] : null;
        }
    }

    /// <summary>Primeiro item com o typeid (bolas e consumíveis ficam numa pilha por typeid).</summary>
    public Item? FindType(int typeId)
    {
        Item? best = null;
        foreach (var it in Items.Values)
            if (it.TypeId == typeId && it.Location == ItemLocation.Inventory && (best == null || it.Id < best.Id)) best = it;
        return best;
    }
}

/// <summary>Acesso aos dados de jogador (implementado em Pangya.Data).</summary>
public interface IPlayerStore
{
    Task<Player?> LoadAsync(long accountId);
    /// <summary>Cria o jogador com os itens iniciais numa transação; ids vêm do banco.</summary>
    Task<Player> CreateAsync(long accountId, NewPlayer spec);
    /// <summary>Reserva ids únicos (da mesma sequência dos itens), ex.: para os objetos do bot.</summary>
    Task<int[]> NewIdsAsync(int count);
    /// <summary>Grava várias mudanças do jogador numa transação (tudo ou nada).</summary>
    Task ApplyAsync(long accountId, PlayerChanges changes);
    /// <summary>Grava quantidade/atributos de um item (quantidade 0 apaga).</summary>
    Task SaveItemAsync(long accountId, Item item);
    Task SaveEquipAsync(long accountId, Equipment equip);
}

/// <summary>Conteúdo inicial de um jogador novo.</summary>
public sealed record NewPlayer(long Pang, long Cookie, IReadOnlyList<NewItem> Items);

/// <summary>Item inicial; Equip diz em que posição ele entra equipado.</summary>
public sealed record NewItem(int TypeId, int Quantity, JsonObject? Attrs = null, bool Equip = false);

/// <summary>Mudanças a gravar juntas (compra, upgrade, armário...). Itens novos já têm id reservado.</summary>
public sealed class PlayerChanges
{
    public List<Item> Added { get; } = [];
    public List<Item> Updated { get; } = [];
    public List<int> Removed { get; } = [];
    public long? Pang { get; set; }
    public long? Cookie { get; set; }
    public long? LockerPang { get; set; }
    public int? Level { get; set; }
    public int? Exp { get; set; }
    public int? Flags { get; set; }
    public Equipment? Equip { get; set; }
    public bool IsEmpty => Added.Count == 0 && Updated.Count == 0 && Removed.Count == 0 && Pang == null && Cookie == null
        && LockerPang == null && Level == null && Exp == null && Flags == null && Equip == null;
}
