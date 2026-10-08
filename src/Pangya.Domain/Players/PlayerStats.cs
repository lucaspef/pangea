namespace Pangya.Domain.Players;

/// <summary>
/// Estatística de uma partida como o cliente conta (sPangYaUserStatistics do 0x31/0x06; docs/protocolo/SPEC-perfil-mapas.md
/// §5). HoleIn = chip-in (acertou o buraco de fora do green), PuttIn = putts que caíram. Vem do cliente: só serve para
/// estatística, com limites por buraco jogado (GameStats.Sane).
/// </summary>
public readonly record struct GameStats(
    long Drive, long Putt, long ShotTime, float Longest, long Pangya, long TimeOut, long OB, long Distance, long Hole,
    long HoleInOne, long Bunker, long Fairway, long Albatross, long HoleIn, long PuttIn, float LongestPuttIn, float LongestChipIn)
{
    /// <summary>Limita cada contador ao possível em `holes` buracos (cliente modificado não infla o perfil).</summary>
    public GameStats Sane(int holes)
    {
        long h = Math.Max(holes, 0);
        static long C(long v, long max) => Math.Clamp(v, 0, max);
        static float F(float v, float max) => float.IsFinite(v) ? Math.Clamp(v, 0, max) : 0;
        return new GameStats(C(Drive, h * 10), C(Putt, h * 20), C(ShotTime, h * 3600), F(Longest, 1000), C(Pangya, h * 20),
            C(TimeOut, h * 20), C(OB, h * 20), C(Distance, h * 20000), C(Hole, h), C(HoleInOne, h), C(Bunker, h * 20),
            C(Fairway, h), C(Albatross, h), C(HoleIn, h), C(PuttIn, h), F(LongestPuttIn, 300), F(LongestChipIn, 500));
    }
}

/// <summary>Totais do jogador (o que o perfil mostra: drives, putts, chip-ins, hole-in-one, albatross, recordes de distância...).</summary>
public sealed class PlayerStats
{
    public long Drive { get; set; }
    public long Putt { get; set; }
    public long ShotTime { get; set; }
    public float Longest { get; set; }
    public long Pangya { get; set; }
    public long TimeOut { get; set; }
    public long OB { get; set; }
    public long Distance { get; set; }
    public long Hole { get; set; }
    public long HoleInOne { get; set; }
    public long Bunker { get; set; }
    public long Fairway { get; set; }
    public long Albatross { get; set; }
    public long HoleIn { get; set; }
    public long PuttIn { get; set; }
    public float LongestPuttIn { get; set; }
    public float LongestChipIn { get; set; }
    public long TotalScore { get; set; }
    public long Games { get; set; }
    /// <summary>Troféus do torneio: [faixa × 3 + (ouro 0, prata 1, bronze 2)] (Trophy).</summary>
    public int[] Trophies { get; set; } = new int[Trophy.Count];

    public PlayerStats Clone()
    {
        var c = (PlayerStats)MemberwiseClone();
        c.Trophies = new int[Trophy.Count];
        Array.Copy(Trophies, c.Trophies, Math.Min(Trophies.Length, Trophy.Count));   // gravado antes dos troféus: vazio
        return c;
    }

    /// <summary>Totais depois de uma partida terminada (o original não muda). score = tacadas - par, se o modo tiver.</summary>
    public static PlayerStats After(PlayerStats? old, GameStats? game, int holes, int? score)
    {
        var t = old?.Clone() ?? new PlayerStats();
        t.Games++;
        if (score is { } s) t.TotalScore += s;
        if (game is not { } raw) return t;
        var g = raw.Sane(holes);
        t.Drive += g.Drive; t.Putt += g.Putt; t.ShotTime += g.ShotTime; t.Pangya += g.Pangya; t.TimeOut += g.TimeOut;
        t.OB += g.OB; t.Distance += g.Distance; t.Hole += g.Hole; t.HoleInOne += g.HoleInOne; t.Bunker += g.Bunker;
        t.Fairway += g.Fairway; t.Albatross += g.Albatross; t.HoleIn += g.HoleIn; t.PuttIn += g.PuttIn;
        t.Longest = Math.Max(t.Longest, g.Longest);
        t.LongestPuttIn = Math.Max(t.LongestPuttIn, g.LongestPuttIn);
        t.LongestChipIn = Math.Max(t.LongestChipIn, g.LongestChipIn);
        return t;
    }
}
