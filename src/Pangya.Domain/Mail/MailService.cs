using System.Text.Json.Nodes;
using Pangya.Domain.Players;

namespace Pangya.Domain.Mail;

public sealed class MailItem
{
    public int Id { get; init; }
    public int TypeId { get; init; }
    public int Quantity { get; init; }
    public JsonObject Attrs { get; init; } = [];
    public bool Taken { get; init; }
}

public sealed class MailLetter
{
    public int Id { get; init; }
    public long AccountId { get; init; }
    public string SenderNick { get; init; } = "";
    public string Message { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public bool Read { get; init; }
    public List<MailItem> Items { get; init; } = [];
}

/// <summary>Códigos de erro do envio (0x13F; também usados pela lista/leitura).</summary>
public enum MailCode : byte
{
    Ok = 0, Refused = 1, NoReceiver = 2, ReceiverHasItem = 3, NotEnough = 4, NotYours = 5, CannotAttach = 7, CannotSend = 8,
    BoxFull = 9, TooManyItems = 0x16,
}

public interface IMailStore
{
    Task<(List<MailLetter> Items, int Total)> ListAsync(long accountId, int page, int perPage);
    Task<MailLetter?> GetAsync(long accountId, int mailId);
    Task MarkReadAsync(long accountId, int mailId);
    Task<List<MailLetter>> UnreadAsync(long accountId, int max);
    Task<int> CountAsync(long accountId);
    /// <summary>Conta pelo nick (sem diferenciar maiúsculas); null = não existe.</summary>
    Task<long?> AccountByNickAsync(string nick);
    /// <summary>Apaga as cartas da conta que não têm anexo por pegar; devolve quantas não puderam ser apagadas.</summary>
    Task<int> DeleteAsync(long accountId, IReadOnlyList<int> ids);
    /// <summary>
    /// Insere a carta (com anexos) e grava <paramref name="senderChanges"/> na mesma transação: do remetente (itens que
    /// saíram, taxa) ou, numa carta do sistema (senderId null), do próprio destinatário (o que ele gastou para ganhar o
    /// prêmio, ex. cubo e chave).
    /// </summary>
    Task<int> SendAsync(long toAccount, long? senderId, string senderNick, string message, IReadOnlyList<MailItem> items,
        PlayerChanges? senderChanges);
    /// <summary>Marca os anexos como pegos e grava o que o jogador recebeu, numa transação (falha se já foram pegos).</summary>
    Task TakeAsync(long accountId, int mailId, PlayerChanges changes);
}

/// <summary>
/// Correio (SPEC-correio-presentes.md): até 4 anexos vendáveis e fora de uso por carta, taxa 100 pang (sem anexo) ou
/// 500 por anexo, até 800 caracteres, caixa até 300 cartas. Pegar anexos soma na pilha ou cria o objeto; pang vai para
/// o saldo. Tudo revalidado no servidor.
/// </summary>
public sealed class MailService(IMailStore store, IPlayerStore players, IGameData data)
{
    public const int PerPage = 20, MaxItems = 4, MaxPerItem = 99, MaxMessage = 800, BoxLimit = 300, UnreadMax = 20;
    public const long FeeNoItem = 100, FeePerItem = 500;
    public const int PangPouch = 0x1A000010;

    public IMailStore Store => store;

    public static long Fee(int items) => items == 0 ? FeeNoItem : FeePerItem * items;

    /// <summary>Enviar de um jogador (0xBB). attach = (id do objeto, tid, quantidade). Devolve os objetos que saíram (quantidade nova).</summary>
    public async Task<(MailCode Code, List<Item> Left)> SendAsync(Player from, long toAccount, string message,
        IReadOnlyList<(int Id, int TypeId, int Count)> attach)
    {
        if (toAccount == from.AccountId || toAccount <= 0) return (MailCode.NoReceiver, []);
        if (await players.LoadAsync(toAccount) == null) return (MailCode.NoReceiver, []);
        if (attach.Count > MaxItems) return (MailCode.TooManyItems, []);
        if (message.Length > MaxMessage || (message.Length == 0 && attach.Count == 0)) return (MailCode.CannotSend, []);
        if (await store.CountAsync(toAccount) >= BoxLimit) return (MailCode.BoxFull, []);
        long fee = Fee(attach.Count);
        if (from.Pang < fee) return (MailCode.CannotSend, []);
        var ch = new PlayerChanges { Pang = from.Pang - fee };
        var items = new List<MailItem>();
        var left = new List<Item>();
        var seenTid = new HashSet<int>();
        foreach (var (id, tid, count) in attach)
        {
            if (from.Find(id) is not { Location: ItemLocation.Inventory } it || it.TypeId != tid) return (MailCode.NotYours, []);
            if (!seenTid.Add(tid)) return (MailCode.CannotAttach, []);
            if (it.Group == ItemGroup.Card || !data.CanTrade(tid) || it.ExpiresAt != null || tid == Item.BasicBall
                || PlayerActions.IsBusy(from, it)) return (MailCode.CannotAttach, []);
            int qty = it.IsConsumable ? count : 1;
            if (qty < 1 || qty > MaxPerItem || (it.IsConsumable && qty > PlayerActions.Free(from, it))) return (MailCode.NotEnough, []);
            var after = it.Clone();
            after.Quantity = it.IsConsumable ? it.Quantity - qty : 0;
            if (after.Quantity == 0) ch.Removed.Add(it.Id); else ch.Updated.Add(after);
            left.Add(after);
            items.Add(new MailItem { TypeId = tid, Quantity = qty, Attrs = (JsonObject)it.Attrs.DeepClone() });
        }
        await store.SendAsync(toAccount, from.AccountId, from.Nickname, message, items, ch);
        from.Pang -= fee;
        foreach (var x in left) if (x.Quantity == 0) from.Items.Remove(x.Id); else from.Items[x.Id] = x;
        return (MailCode.Ok, left);
    }

    /// <summary>
    /// Carta do sistema (presentes da loja, prêmios, GM): anexos (tid, quantidade). <paramref name="receiverCost"/> = o que
    /// o destinatário gastou pelo prêmio, gravado na mesma transação da carta.
    /// </summary>
    public Task<int> SendSystemAsync(long toAccount, string senderNick, string message, IReadOnlyList<(int TypeId, int Quantity)> attach,
        PlayerChanges? receiverCost = null)
    {
        var items = new List<MailItem>(attach.Count);
        foreach (var (tid, qty) in attach) items.Add(new MailItem { TypeId = tid, Quantity = qty });
        return store.SendAsync(toAccount, null, senderNick, message, items, receiverCost);
    }

    /// <summary>
    /// Pegar os anexos (0xBF). Empilháveis somam na pilha (ou criam uma), únicos viram objetos novos (recusa se o jogador
    /// já tem um personagem/caddie igual), pang vai para o saldo. Devolve (item no inventário, era pilha) e o pang somado.
    /// </summary>
    public async Task<(bool Ok, List<(MailItem Mail, Item Item)> Placed, long Pang)> TakeAsync(Player p, int mailId)
    {
        var mail = await store.GetAsync(p.AccountId, mailId);
        if (mail == null) return (false, [], 0);
        var pending = new List<MailItem>();
        foreach (var it in mail.Items) if (!it.Taken) pending.Add(it);
        if (pending.Count == 0) return (false, [], 0);
        var ch = new PlayerChanges();
        var placed = new List<(MailItem, Item)>();
        var touched = new Dictionary<int, Item>();
        long pang = 0;
        int need = 0;
        foreach (var m in pending) if (m.TypeId != PangPouch && !IsStack(m.TypeId)) need++;
        foreach (var m in pending) if (m.TypeId != PangPouch && IsStack(m.TypeId) && p.FindType(m.TypeId) == null) need++;
        var ids = need > 0 ? await players.NewIdsAsync(need) : [];
        int next = 0;
        foreach (var m in pending)
        {
            if (m.TypeId == PangPouch) { pang += m.Quantity; continue; }
            if (!data.Exists(m.TypeId)) return (false, [], 0);
            if (IsStack(m.TypeId))
            {
                var existing = p.FindType(m.TypeId);
                if (existing != null && touched.TryGetValue(existing.Id, out var t)) existing = t;
                var stack = existing?.Clone() ?? new Item { Id = ids[next++], TypeId = m.TypeId, Quantity = 0 };
                stack.Quantity = Math.Min(stack.Quantity + m.Quantity, short.MaxValue);
                (existing != null || touched.ContainsKey(stack.Id) ? ch.Updated : ch.Added).Add(stack);
                touched[stack.Id] = stack;
                placed.Add((m, stack));
            }
            else
            {
                if (Item.GroupOf(m.TypeId) is ItemGroup.Character or ItemGroup.Caddie && p.FindType(m.TypeId) != null) return (false, [], 0);
                var it = new Item { Id = ids[next++], TypeId = m.TypeId, Quantity = 1, Attrs = (JsonObject)m.Attrs.DeepClone() };
                ch.Added.Add(it);
                placed.Add((m, it));
            }
        }
        if (pang > 0) ch.Pang = p.Pang + pang;
        try { await store.TakeAsync(p.AccountId, mailId, ch); }
        catch (Exception) { return (false, [], 0); }                          // pegos ao mesmo tempo em outra sessão
        foreach (var (_, it) in placed) p.Items[it.Id] = it;
        p.Pang += pang;
        return (true, placed, pang);
    }

    static bool IsStack(int tid) => Item.GroupOf(tid) is ItemGroup.Ball or ItemGroup.Usable;
}
