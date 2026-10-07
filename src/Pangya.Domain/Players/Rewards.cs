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

    /// <summary>
    /// Calcula, grava (pang, nível, EXP e a estatística do curso, se o modo tiver placar contra o par) e só então
    /// aplica no jogador. course = (mapa, placar) ou null.
    /// </summary>
    public static async Task<Reward> ApplyAsync(IPlayerStore store, Player p, uint reportedPang, uint reportedBonus, int holes, bool finished,
        RewardConfig cfg, (int Course, int Score)? course = null)
    {
        var (pang, exp) = Compute(reportedPang, reportedBonus, holes, finished, cfg);
        Dictionary<int, CourseRecord>? courses = null;
        if (finished && holes > 0 && course is { } c)
        {
            courses = new Dictionary<int, CourseRecord>(p.Courses);
            courses[c.Course] = new CourseResult(c.Course, holes, c.Score, pang, p.Character?.TypeId ?? 0).ApplyTo(p.Courses.GetValueOrDefault(c.Course));
        }
        if (pang == 0 && exp == 0 && courses == null) return default;
        var after = new Player { Level = p.Level, Exp = p.Exp };
        int levels = Levels.AddExp(after, exp);
        await store.ApplyAsync(p.AccountId, new PlayerChanges { Pang = p.Pang + pang, Level = after.Level, Exp = after.Exp, Courses = courses });
        p.Pang += pang;
        p.Level = after.Level;
        p.Exp = after.Exp;
        if (courses != null && course is { } played) p.Courses[played.Course] = courses[played.Course];
        return new Reward(pang, exp, levels);
    }
}
