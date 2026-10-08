using System.Text.Json.Nodes;
using Dapper;
using Pangya.Domain.Mail;
using Pangya.Domain.Players;

namespace Pangya.Data;

/// <summary>Correio no PostgreSQL (migração 006). Envio e retirada gravam também as mudanças do jogador, na mesma transação.</summary>
public sealed class MailRepository(Db db) : IMailStore
{
    sealed record MailRow(int Id, long AccountId, string SenderNick, string Message, DateTime CreatedAt, DateTime? ReadAt);
    sealed record ItemRow(int Id, int MailId, int TypeId, int Quantity, string Attrs, DateTime? TakenAt);

    static async Task<List<MailLetter>> WithItemsAsync(System.Data.Common.DbConnection c, IEnumerable<MailRow> rows)
    {
        var list = new List<MailLetter>();
        var byId = new Dictionary<int, List<MailItem>>();
        var ids = new List<int>();
        foreach (var r in rows)
        {
            var items = new List<MailItem>();
            byId[r.Id] = items;
            ids.Add(r.Id);
            list.Add(new MailLetter { Id = r.Id, AccountId = r.AccountId, SenderNick = r.SenderNick, Message = r.Message,
                CreatedAt = r.CreatedAt, Read = r.ReadAt != null, Items = items });
        }
        if (ids.Count == 0) return list;
        var itemRows = await c.QueryAsync<ItemRow>(
            "select id, mail_id, type_id, quantity, attrs::text attrs, taken_at from mail_items where mail_id = any(@ids) order by id", new { ids });
        foreach (var i in itemRows)
            byId[i.MailId].Add(new MailItem { Id = i.Id, TypeId = i.TypeId, Quantity = i.Quantity, Taken = i.TakenAt != null,
                Attrs = JsonNode.Parse(i.Attrs)?.AsObject() ?? [] });
        return list;
    }

    const string MailCols = "id, account_id, sender_nick, message, created_at, read_at";

    public async Task<(List<MailLetter> Items, int Total)> ListAsync(long accountId, int page, int perPage)
    {
        await using var c = await db.OpenAsync();
        int total = await c.ExecuteScalarAsync<int>("select count(*) from mails where account_id = @accountId", new { accountId });
        var rows = await c.QueryAsync<MailRow>($"select {MailCols} from mails where account_id = @accountId order by id desc offset @skip limit @perPage",
            new { accountId, skip = (Math.Max(page, 1) - 1) * perPage, perPage });
        return (await WithItemsAsync(c, rows), total);
    }

    public async Task<MailLetter?> GetAsync(long accountId, int mailId)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<MailRow>($"select {MailCols} from mails where account_id = @accountId and id = @mailId", new { accountId, mailId });
        var list = await WithItemsAsync(c, rows);
        return list.Count == 0 ? null : list[0];
    }

    public async Task MarkReadAsync(long accountId, int mailId)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("update mails set read_at = coalesce(read_at, now()) where account_id = @accountId and id = @mailId", new { accountId, mailId });
    }

    public async Task<List<MailLetter>> UnreadAsync(long accountId, int max)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<MailRow>($"select {MailCols} from mails where account_id = @accountId and read_at is null order by id desc limit @max",
            new { accountId, max });
        return await WithItemsAsync(c, rows);
    }

    public async Task<int> CountAsync(long accountId)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<int>("select count(*) from mails where account_id = @accountId", new { accountId });
    }

    public async Task<long?> AccountByNickAsync(string nick)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<long?>("select id from accounts where lower(nickname) = lower(@nick) limit 1", new { nick });
    }

    public async Task<int> DeleteAsync(long accountId, IReadOnlyList<int> ids)
    {
        await using var c = await db.OpenAsync();
        int deleted = await c.ExecuteAsync("""
            delete from mails m where m.account_id = @accountId and m.id = any(@ids)
              and not exists (select 1 from mail_items i where i.mail_id = m.id and i.taken_at is null)
            """, new { accountId, ids = ids is int[] a ? a : new List<int>(ids).ToArray() });
        return ids.Count - deleted;
    }

    public async Task<int> SendAsync(long toAccount, long? senderId, string senderNick, string message, IReadOnlyList<MailItem> items,
        PlayerChanges? senderChanges)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        if (senderChanges != null && senderId is { } sid) await PlayerRepository.ApplyInAsync(c, tx, sid, senderChanges, strict: true);
        int id = await c.ExecuteScalarAsync<int>(
            "insert into mails(account_id, sender_id, sender_nick, message) values (@toAccount, @senderId, @senderNick, @message) returning id",
            new { toAccount, senderId, senderNick, message }, tx);
        foreach (var it in items)
            await c.ExecuteAsync("insert into mail_items(mail_id, type_id, quantity, attrs) values (@id, @TypeId, @Quantity, @attrs::jsonb)",
                new { id, it.TypeId, it.Quantity, attrs = it.Attrs.ToJsonString() }, tx);
        await tx.CommitAsync();
        return id;
    }

    public async Task TakeAsync(long accountId, int mailId, PlayerChanges changes)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        int n = await c.ExecuteAsync("""
            update mail_items i set taken_at = now() from mails m
            where i.mail_id = m.id and m.id = @mailId and m.account_id = @accountId and i.taken_at is null
            """, new { accountId, mailId }, tx);
        if (n == 0) throw new InvalidOperationException($"carta {mailId} sem anexos por pegar");
        await c.ExecuteAsync("update mails set read_at = coalesce(read_at, now()) where id = @mailId", new { mailId }, tx);
        await PlayerRepository.ApplyInAsync(c, tx, accountId, changes, strict: true);
        await tx.CommitAsync();
    }
}
