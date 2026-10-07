namespace Pangya.Domain.Players;

/// <summary>
/// Estatística de um curso (o sMapStatistics do perfil), só com o que o servidor calcula: partidas, buracos, soma do
/// placar (tacadas - par), melhor placar, maior pang de uma partida e o personagem do recorde.
/// </summary>
public sealed class CourseRecord
{
    /// <summary>cBestScore do cliente para "sem recorde" (a linha aparece com "-").</summary>
    public const int NoRecord = 127;
    /// <summary>Só partidas completas do curso (18 buracos) valem recorde.</summary>
    public const int RecordHoles = 18;

    public int Games { get; set; }
    public int Holes { get; set; }
    public int TotalScore { get; set; }
    public int BestScore { get; set; } = NoRecord;
    public long MaxPang { get; set; }
    public int CharacterTypeId { get; set; }

    public CourseRecord Clone() => (CourseRecord)MemberwiseClone();
}

/// <summary>Uma partida terminada num curso, para os recordes (modos com placar contra o par).</summary>
public readonly record struct CourseResult(int Course, int Holes, int Score, long Pang, int CharacterTypeId)
{
    /// <summary>O registro depois desta partida (o original não muda).</summary>
    public CourseRecord ApplyTo(CourseRecord? old)
    {
        var r = old?.Clone() ?? new CourseRecord();
        r.Games++;
        r.Holes += Holes;
        r.TotalScore += Score;
        r.MaxPang = Math.Max(r.MaxPang, Pang);
        if (Holes >= CourseRecord.RecordHoles && Score < r.BestScore)
        {
            r.BestScore = Math.Max(Score, -128);
            r.CharacterTypeId = CharacterTypeId;
        }
        return r;
    }
}
