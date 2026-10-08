using Dapper;
using Pangya.Domain.Players;
using Pangya.Domain.Ranking;

namespace Pangya.Data;

/// <summary>Fonte do retrato do ranking: todos os jogadores com nick, nível, EXP e estatística (sem inventário).</summary>
public sealed class RankingRepository(Db db) : IRankingStore
{
    sealed record Row(long AccountId, string Nickname, short Level, int Exp, string Stats);

    public async Task<List<Player>> AllPlayersAsync()
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<Row>("""
            select p.account_id, a.nickname, p.level, p.exp, p.stats::text stats
            from players p join accounts a on a.id = p.account_id
            where a.nickname is not null
            """);
        var list = new List<Player>();
        foreach (var r in rows)
        {
            var p = new Player { AccountId = r.AccountId, Nickname = r.Nickname, Level = r.Level, Exp = r.Exp };
            PlayerRepository.ApplyStats(p, r.Stats);
            list.Add(p);
        }
        return list;
    }
}
