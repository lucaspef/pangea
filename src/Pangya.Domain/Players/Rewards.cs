using Pangya.Core.Config;

namespace Pangya.Domain.Players;

/// <summary>
/// Recompensa de fim de partida. O pang vem do resultado que o cliente informa (a física é dele), então o servidor
/// limita por buraco jogado; o EXP é calculado no servidor. Quem saiu antes do fim não ganha nada.
/// </summary>
public static class Rewards
{
    public readonly record struct Reward(long Pang, int Exp, int LevelsUp);

    /// <summary>
    /// Entrada da fórmula de EXP (Versus.requestFinishExpGame / Tourney do GB): jogadores na partida, estrelas do
    /// curso (dificuldade 1..5 do Course.iff), posição final (0 = 1º), se a posição desconta 10% por lugar (VS sim,
    /// torneio não) e o nível do jogador (informativo: no 70 a barra ainda enche até o máximo, ver Levels.AddExp).
    /// </summary>
    public readonly record struct ExpInput(int Players, float Stars, int Position, bool PositionPenalty, int Level);

    /// <summary>
    /// EXP = estrelas × buracos × jogadores × (1 + EXP% dos cards/itens) × taxa do servidor, × (1 − 0,1 × posição) no VS;
    /// 0 para quem saiu. Truncado a cada passo, como o GB (que dava 0 no nível 70; aqui não: a barra do 70 tem 1,6 M).
    /// </summary>
    public static int Exp(ExpInput e, int holes, bool finished, int expRate, RewardConfig cfg)
    {
        if (!finished || holes <= 0) return 0;
        double exp = Math.Floor(Math.Max(e.Players, 1) * holes * Math.Max(e.Stars, 1));
        exp = Math.Floor(exp * (1 + Math.Max(expRate, 0) / 100.0) * (Math.Max(cfg.ExpRate, 0) / 100.0));
        if (e.PositionPenalty) exp = Math.Floor(exp * Math.Max(0, 1 - 0.1 * e.Position));
        return (int)exp;
    }

    /// <summary>
    /// Pang = (informado + bônus) limitado por buraco e depois × (1 + pang%/100); EXP pela fórmula do GB (<see cref="Exp"/>).
    /// Os % vêm dos cards especiais em vigor (o cliente só mostra o pang multiplicado na tela; EXP é só do servidor).
    /// Sem ExpInput (testes, chamadas antigas): 1 jogador, 1 estrela, 1º lugar.
    /// </summary>
    public static (long Pang, int Exp) Compute(uint reportedPang, uint reportedBonus, int holes, bool finished, RewardConfig cfg,
        int pangRate = 0, int expRate = 0, ExpInput? expIn = null)
    {
        if (!finished || holes <= 0) return (0, 0);
        long pang = Math.Min((long)reportedPang + reportedBonus, (long)cfg.MaxPangPerHole * holes);
        int exp = Exp(expIn ?? new ExpInput(1, 1, 0, false, 0), holes, finished, expRate, cfg);
        return ((long)Math.Round(pang * (1 + Math.Max(pangRate, 0) / 100.0)), exp);
    }

    /// <summary>
    /// Calcula, grava (pang, nível, EXP e a estatística do curso, se o modo tiver placar contra o par) e só então
    /// aplica no jogador. course = (mapa, placar) ou null.
    /// </summary>
    public static async Task<Reward> ApplyAsync(IPlayerStore store, Player p, uint reportedPang, uint reportedBonus, int holes, bool finished,
        RewardConfig cfg, (int Course, int Score)? course = null, int pangRate = 0, int expRate = 0, GameStats? stats = null,
        ExpInput? expIn = null)
    {
        var (pang, exp) = Compute(reportedPang, reportedBonus, holes, finished, cfg, pangRate, expRate, expIn);
        // totais do perfil: só partida terminada; os contadores vêm do último 0x31/0x06 do cliente, limitados por buraco
        var totals = finished && holes > 0 ? PlayerStats.After(p.Stats, stats, holes, course?.Score) : null;
        Dictionary<int, CourseRecord>? courses = null;
        if (finished && holes > 0 && course is { } c)
        {
            courses = new Dictionary<int, CourseRecord>(p.Courses);
            courses[c.Course] = new CourseResult(c.Course, holes, c.Score, pang, p.Character?.TypeId ?? 0).ApplyTo(p.Courses.GetValueOrDefault(c.Course));
        }
        if (pang == 0 && exp == 0 && courses == null && totals == null) return default;
        var after = new Player { Level = p.Level, Exp = p.Exp };
        int levels = Levels.AddExp(after, exp);
        await store.ApplyAsync(p.AccountId, new PlayerChanges { Pang = p.Pang + pang, Level = after.Level, Exp = after.Exp, Courses = courses, Stats = totals });
        p.Pang += pang;
        p.Level = after.Level;
        p.Exp = after.Exp;
        if (courses != null && course is { } played) p.Courses[played.Course] = courses[played.Course];
        if (totals != null) p.Stats = totals;
        return new Reward(pang, exp, levels);
    }
}
