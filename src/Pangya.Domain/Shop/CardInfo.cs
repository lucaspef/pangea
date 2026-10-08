namespace Pangya.Domain.Shop;

/// <summary>
/// Dados de um card (Card.iff). Subtipo = (typeid &gt;&gt; 22) &amp; 0xF: 0 personagem, 1 caddie, 2 especial, 3 pacote/ticket, 4 caixa.
/// </summary>
public sealed record CardInfo(int TypeId, bool Final, int Rarity, int Ability, int AbilityValue, int UseMinutes, int Volume)
{
    public const int SubCharacter = 0, SubCaddie = 1, SubSpecial = 2, SubPack = 3, SubBox = 4;
    /// <summary>Habilidades instantâneas dos cards especiais: EXP, pang, pang aleatório.</summary>
    public const int AbilityExp = 1, AbilityPang = 4, AbilityRandomPang = 17;
    /// <summary>Especiais com prazo: pang +% (geleia preta) e EXP +% (geleia branca), aplicados pelo servidor no fim do jogo.</summary>
    public const int AbilityPangRate = 2, AbilityExpRate = 3;

    public int SubType => (TypeId >> 22) & 0xF;
}
