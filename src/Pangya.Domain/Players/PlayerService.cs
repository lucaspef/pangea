using System.Text.Json.Nodes;
using Pangya.Core.Config;
using Pangya.Core.Text;

namespace Pangya.Domain.Players;

/// <summary>Dados do jogo que dependem da versão do cliente (implementado pela camada de protocolo, a partir do IFF).</summary>
public interface IGameData
{
    bool Exists(int typeId);
    /// <summary>Partes padrão (24 typeids) de um personagem recém-criado.</summary>
    int[] DefaultParts(int characterTypeId);
    /// <summary>Item do catálogo da loja (null = não existe).</summary>
    Shop.ShopItem? GetShopItem(int typeId);
    /// <summary>Preço em pang para subir um atributo (0 força .. 4 curva) a partir do nível atual; null = não dá.</summary>
    int? UpgradePrice(int stat, int current);
    /// <summary>Todos os cards do jogo, por typeid.</summary>
    IReadOnlyDictionary<int, Shop.CardInfo> Cards { get; }
}

/// <summary>Resultado da conferência/criação de nickname (os códigos são os do cliente KR: 0x0D/0x0E).</summary>
public enum NicknameStatus { Ok = 0, Error = 1, Taken = 2, Invalid = 3 }

/// <summary>Regras de nickname do cliente (createnickdlg.cpp, hatmanager.cpp:3877-3910), também conferidas no servidor.</summary>
public static class NicknameRules
{
    public static bool IsValid(string nick, string login)
    {
        int bytes = Cp949.Encoding.GetByteCount(nick);
        if (bytes is < 4 or > 16 || nick.Contains('\'') || string.Equals(nick, login, StringComparison.OrdinalIgnoreCase)) return false;
        // ASCII visível (sem espaço) ou sílabas Hangul
        foreach (var ch in nick)
            if (ch is not (>= '\x21' and <= '\x7E' or >= '가' and <= '힣')) return false;
        return true;
    }
}

/// <summary>Criação do jogador (primeiro personagem + itens iniciais) e carga dos dados.</summary>
public sealed class PlayerService(IPlayerStore store, IGameData data, NewPlayerConfig start)
{
    /// <summary>Personagens que o cliente oferece na criação (lobbymain.cpp:14689: 0x04000000 | gênero).</summary>
    public static bool IsCreatableCharacter(int typeId) => typeId is 0x04000000 or 0x04000001;

    public Task<Player?> LoadAsync(long accountId) => store.LoadAsync(accountId);
    public IPlayerStore Store => store;

    /// <summary>Uid/guid do bot (fora da faixa das contas, que começam em 100001).</summary>
    public const long BotAccountId = 0x7F000001;
    const int BotCharacter = 0x04000001;

    /// <summary>
    /// Jogador bot em memória (não é salvo), com o mesmo kit de um jogador novo. Os ids dos objetos vêm da
    /// sequência do banco: nunca iguais aos de um jogador (ids repetidos quebravam a física no cliente).
    /// </summary>
    public async Task<Player> CreateBotAsync()
    {
        var ids = await store.NewIdsAsync(3);
        var bot = new Player { AccountId = BotAccountId, Login = "pangbot", Nickname = "Bot", Level = 1 };
        var ch = new Item { Id = ids[0], TypeId = data.Exists(BotCharacter) ? BotCharacter : 0x04000000 };
        ch.Set("parts", data.DefaultParts(ch.TypeId));
        bot.Add(ch);
        bot.Add(new Item { Id = ids[1], TypeId = start.ClubSet });
        bot.Add(new Item { Id = ids[2], TypeId = start.Ball, Quantity = start.BallCount });
        bot.Equip = new Equipment { CharacterId = ids[0], ClubSetId = ids[1], BallTypeId = start.Ball };
        return bot;
    }

    /// <summary>Cria o jogador; null se o personagem/cores são inválidos ou o jogador já existe.</summary>
    public async Task<Player?> CreateAsync(long accountId, int characterTypeId, int hairColor, int shirtColor)
    {
        if (!IsCreatableCharacter(characterTypeId) || !data.Exists(characterTypeId) || hairColor is < 0 or > 2 || shirtColor is < 0 or > 2)
            return null;
        if (await store.LoadAsync(accountId) != null) return null;
        var parts = new JsonArray();
        foreach (var t in data.DefaultParts(characterTypeId)) parts.Add(t);
        var character = new JsonObject { ["hair"] = hairColor, ["shirt"] = shirtColor, ["parts"] = parts };
        var items = new List<NewItem> { new(characterTypeId, 1, character, Equip: true) };
        if (data.Exists(start.ClubSet)) items.Add(new(start.ClubSet, 1, Equip: true));
        if (data.Exists(start.Ball)) items.Add(new(start.Ball, start.BallCount, Equip: true));
        return await store.CreateAsync(accountId, new NewPlayer(start.Pang, start.Cookie, items));
    }
}
