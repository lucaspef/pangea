using Pangya.Core.Config;

namespace Pangya.Domain.Players;

/// <summary>
/// Recompensa de fim de partida. O pang vem do resultado que o cliente informa (a física é dele), então o servidor
/// limita por buraco jogado; o EXP é calculado no servidor. Quem saiu antes do fim não ganha nada.
/// </summary>
public static class Rewards
{
    public readonly record struct Reward(long Pang, int Exp, int LevelsUp);

    public static (long Pang, int Exp) Compute(uint reportedPang, uint reportedBonus, int holes, bool finished, RewardConfig cfg) =>
        !finished || holes <= 0 ? (0, 0)
        : (Math.Min((long)reportedPang + reportedBonus, (long)cfg.MaxPangPerHole * holes), cfg.ExpPerHole * holes);

    /// <summary>Calcula, grava (pang, nível, EXP) e só então aplica no jogador.</summary>
    public static async Task<Reward> ApplyAsync(IPlayerStore store, Player p, uint reportedPang, uint reportedBonus, int holes, bool finished, RewardConfig cfg)
    {
        var (pang, exp) = Compute(reportedPang, reportedBonus, holes, finished, cfg);
        if (pang == 0 && exp == 0) return default;
        var after = new Player { Level = p.Level, Exp = p.Exp };
        int levels = Levels.AddExp(after, exp);
        await store.ApplyAsync(p.AccountId, new PlayerChanges { Pang = p.Pang + pang, Level = after.Level, Exp = after.Exp });
        p.Pang += pang;
        p.Level = after.Level;
        p.Exp = after.Exp;
        return new Reward(pang, exp, levels);
    }
}
