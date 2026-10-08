using Dapper;
using Pangya.Domain.Messenger;
using Pangya.Domain.Players;

namespace Pangya.Data;

/// <summary>Bilhetes do mensageiro (migração 009).</summary>
public sealed class NoteRepository(Db db) : INoteStore
{
    sealed record Row(long Id, long? SenderId, string SenderNick, string Text, DateTime CreatedAt);

    public async Task<bool> ExistsAsync(long account)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from players where account_id = @account)", new { account });
    }

    public async Task<int> UndeliveredAsync(long account)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<int>("select count(*) from notes where account_id = @account and delivered_at is null", new { account });
    }

    public async Task SendAsync(long to, long from, string fromNick, string text, PlayerChanges senderCost)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await PlayerRepository.ApplyInAsync(c, tx, from, senderCost, strict: true);
        await c.ExecuteAsync("insert into notes(account_id, sender_id, sender_nick, text) values (@to, @from, @fromNick, @text)",
            new { to, from, fromNick, text }, tx);
        await tx.CommitAsync();
    }

    public async Task<List<Note>> RecentAsync(long account, int max)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<Row>(
            "select id, sender_id, sender_nick, text, created_at from notes where account_id = @account order by id desc limit @max",
            new { account, max });
        var list = new List<Note>();
        foreach (var r in rows) list.Add(new Note(r.Id, r.SenderId ?? 0, r.SenderNick, r.Text, r.CreatedAt));
        return list;
    }

    public async Task MarkDeliveredAsync(long account)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update notes set delivered_at = now() where account_id = @account and delivered_at is null", new { account });
    }
}
