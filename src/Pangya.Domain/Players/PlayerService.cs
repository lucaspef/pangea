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
        return nick.All(ch => ch is >= '\x21' and <= '\x7E' or >= '가' and <= '힣');
    }
}

/// <summary>Criação do jogador (primeiro personagem + itens iniciais) e carga dos dados.</summary>
public sealed class PlayerService(IPlayerStore store, IGameData data, NewPlayerConfig start)
{
    /// <summary>Personagens que o cliente oferece na criação (lobbymain.cpp:14689: 0x04000000 | gênero).</summary>
    public static bool IsCreatableCharacter(int typeId) => typeId is 0x04000000 or 0x04000001;

    public Task<Player?> LoadAsync(long accountId) => store.LoadAsync(accountId);

    /// <summary>Cria o jogador; null se o personagem/cores são inválidos ou o jogador já existe.</summary>
    public async Task<Player?> CreateAsync(long accountId, int characterTypeId, int hairColor, int shirtColor)
    {
        if (!IsCreatableCharacter(characterTypeId) || !data.Exists(characterTypeId) || hairColor is < 0 or > 2 || shirtColor is < 0 or > 2)
            return null;
        if (await store.LoadAsync(accountId) != null) return null;
        var character = new JsonObject
        {
            ["hair"] = hairColor,
            ["shirt"] = shirtColor,
            ["parts"] = new JsonArray(data.DefaultParts(characterTypeId).Select(v => (JsonNode)v).ToArray()),
        };
        var items = new List<NewItem> { new(characterTypeId, 1, character, Equip: true) };
        if (data.Exists(start.ClubSet)) items.Add(new(start.ClubSet, 1, Equip: true));
        if (data.Exists(start.Ball)) items.Add(new(start.Ball, start.BallCount, Equip: true));
        return await store.CreateAsync(accountId, new NewPlayer(start.Pang, start.Cookie, items));
    }
}
