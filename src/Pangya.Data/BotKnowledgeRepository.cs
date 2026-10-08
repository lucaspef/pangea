using Dapper;
using Pangya.Domain.Rooms;

namespace Pangya.Data;

/// <summary>Aprendizado do bot (migração 012).</summary>
public sealed class BotKnowledgeRepository(Db db) : IBotKnowledgeStore
{
    sealed record HoleRow(int Level, int Course, int Hole, string Data);
    sealed record CalRow(int Level, string Data);

    public async Task<(List<BotHoleData> Holes, List<BotCalibrationData> Calibrations)> LoadAsync()
    {
        await using var c = await db.OpenAsync();
        var holes = new List<BotHoleData>();
        foreach (var r in await c.QueryAsync<HoleRow>("select level::int, course::int, hole::int, data::text from bot_holes"))
            holes.Add(new BotHoleData((BotLevel)r.Level, (byte)r.Course, (byte)r.Hole, r.Data));
        var cals = new List<BotCalibrationData>();
        foreach (var r in await c.QueryAsync<CalRow>("select level::int, data::text from bot_calibration"))
            cals.Add(new BotCalibrationData((BotLevel)r.Level, r.Data));
        return (holes, cals);
    }

    public async Task SaveAsync(IReadOnlyList<BotHoleData> holes, IReadOnlyList<BotCalibrationData> calibrations)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        foreach (var h in holes)
            await c.ExecuteAsync("""
                insert into bot_holes(level, course, hole, data) values (@level, @course, @hole, @data::jsonb)
                on conflict (level, course, hole) do update set data = excluded.data, updated_at = now()
                """, new { level = (short)h.Level, course = (short)h.Course, hole = (short)h.Hole, data = h.Json }, tx);
        foreach (var k in calibrations)
            await c.ExecuteAsync("""
                insert into bot_calibration(level, data) values (@level, @data::jsonb)
                on conflict (level) do update set data = excluded.data, updated_at = now()
                """, new { level = (short)k.Level, data = k.Json }, tx);
        await tx.CommitAsync();
    }
}
