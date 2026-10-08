namespace Pangya.Domain.Rooms;

/// <summary>
/// Equipamento do bot por nível (docs/protocolo/SPEC-bot-especiais.md §4 e §6). Vai no 0x46/0x74 como o de um jogador,
/// e cada cliente recalcula os stats do bot a partir dele (CGolfDoc::SetPlayerLevel), então o equipamento muda a física
/// da tacada do bot em todas as máquinas.
/// Os stats finais são alvos (pedido do usuário): força pelo alcance do driver (240/250/260/280/300 jd = 230 + 2 × força
/// + anel), controle sempre 30 e spin 7/9/11/15/30. PlayerService.EquipBot ajusta os upgrades do club set até bater os
/// alvos com a fórmula do cliente (o cliente não confere o limite de upgrades do club; isso é coisa da loja).
/// </summary>
public sealed record BotKit(int Level, int CharPower, int ClubSet, int Caddie, int[] Rings, int Power, int Control, int Spin)
{
    public const int AirKnight = 0x10000000, TwinFeather = 0x10000007, AirKnight3 = 0x1000000A, RubyAirKnight = 0x10000026;
    public const int Pippin = 0x1C000010, MidnightRing = 0x70010008;
    public const int MaxStat = 30;

    public static readonly int[] DriverYards = [240, 250, 260, 280, 300];

    /// <summary>CharPower = upgrade de força do personagem (até o limite do nível: (nível − 1) / 5).</summary>
    public static BotKit For(BotLevel level) => level switch
    {
        BotLevel.Easy => new(1, 0, AirKnight, 0, [], Power: 5, Control: MaxStat, Spin: 7),
        BotLevel.Normal => new(20, 3, TwinFeather, Pippin, [], Power: 10, Control: MaxStat, Spin: 9),
        BotLevel.Hard => new(40, 7, AirKnight3, Pippin, [MidnightRing], Power: 13, Control: MaxStat, Spin: 11),
        BotLevel.VeryHard => new(70, 13, RubyAirKnight, Pippin, [MidnightRing], Power: 23, Control: MaxStat, Spin: 15),
        _ => new(70, 13, RubyAirKnight, Pippin, [MidnightRing], Power: 33, Control: MaxStat, Spin: MaxStat),
    };
}
