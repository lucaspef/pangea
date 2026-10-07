using System.Text.Json.Nodes;

namespace Pangya.Domain.Players;

/// <summary>Grupo do item = typeid >> 26 (itemmanager.cpp:2484); igual em todas as versões do cliente.</summary>
public enum ItemGroup
{
    Character = 1, Part = 2, Club = 3, ClubSet = 4, Ball = 5, Usable = 6, Caddie = 7, CaddieItem = 8, SetItem = 9,
    Skin = 0xE, HairColor = 0xF, Mascot = 0x10, Furniture = 0x12, Card = 0x1F,
}

/// <summary>Um objeto do jogo de um jogador. Id único global (sequência do banco).</summary>
public sealed class Item
{
    public int Id { get; init; }
    public int TypeId { get; init; }
    public int Quantity { get; set; } = 1;
    /// <summary>Atributos que dependem do tipo (partes do personagem, upgrades, mensagem do mascote...).</summary>
    public JsonObject Attrs { get; set; } = [];
    public DateTime? ExpiresAt { get; set; }

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
    public void Set(string key, int[] values) => Attrs[key] = new JsonArray(values.Select(v => (JsonNode)v).ToArray());
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
    public int Flags { get; set; }
    public Equipment Equip { get; set; } = new();
    public List<Item> Items { get; init; } = [];

    public const int FlagTutorialDone = 1;

    public Item? Find(int id) => id == 0 ? null : Items.Find(i => i.Id == id);
    public IEnumerable<Item> OfGroup(ItemGroup g) => Items.Where(i => i.Group == g);
    public Item? Character => Find(Equip.CharacterId) ?? OfGroup(ItemGroup.Character).FirstOrDefault();
}

/// <summary>Acesso aos dados de jogador (implementado em Pangya.Data).</summary>
public interface IPlayerStore
{
    Task<Player?> LoadAsync(long accountId);
    /// <summary>Cria o jogador com os itens iniciais numa transação; ids vêm do banco.</summary>
    Task<Player> CreateAsync(long accountId, NewPlayer spec);
}

/// <summary>Conteúdo inicial de um jogador novo.</summary>
public sealed record NewPlayer(long Pang, long Cookie, IReadOnlyList<NewItem> Items);

/// <summary>Item inicial; Equip diz em que posição ele entra equipado.</summary>
public sealed record NewItem(int TypeId, int Quantity, JsonObject? Attrs = null, bool Equip = false);
