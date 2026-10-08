namespace Pangya.Domain.Rooms;

/// <summary>
/// Equipamento do bot por nível (docs/protocolo/SPEC-bot-especiais.md §4 e §6). Vai no 0x46/0x74 como o de um jogador,
/// e cada cliente recalcula os stats do bot a partir dele (CGolfDoc::SetPlayerLevel), então o equipamento muda a física
/// da tacada do bot em todas as máquinas. CharPcl/ClubPcl = upgrades (força, controle, precisão, spin, curva), dentro
/// do limite de cada item.
/// </summary>
public sealed record BotKit(int Level, int[] CharPcl, int ClubSet, int[] ClubPcl, int Caddie, int[] Rings)
{
    public const int AirKnight = 0x10000000, TwinFeather = 0x10000007, AirKnight3 = 0x1000000A, RubyAirKnight = 0x10000026;
    public const int Pippin = 0x1C000010, MidnightRing = 0x70010008;

    public static BotKit For(BotLevel level) => level switch
    {
        BotLevel.Easy => new(1, [0, 0, 0, 0, 0], AirKnight, [0, 0, 0, 0, 0], 0, []),
        BotLevel.Normal => new(20, [3, 0, 0, 0, 0], TwinFeather, [2, 2, 2, 1, 1], Pippin, []),
        BotLevel.Hard => new(40, [7, 0, 0, 0, 0], AirKnight3, [7, 6, 6, 2, 2], Pippin, [MidnightRing]),
        _ => new(70, [13, 0, 0, 0, 0], RubyAirKnight, [6, 6, 8, 3, 3], Pippin, [MidnightRing]),   // very hard / impossible
    };
}
