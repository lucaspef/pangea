using Pangya.Domain.Players;

namespace Pangya.Domain.Messenger;

/// <summary>Bilhete (쪽지) recebido.</summary>
public sealed record Note(long Id, long SenderId, string SenderNick, string Text, DateTime CreatedAt);

/// <summary>Códigos do 0x93/0x111 (resposta ao envio de bilhete).</summary>
public enum NoteCode : uint { Ok = 0, Failed = 1, BoxFull = 3, Myself = 7, NoPang = 0xC }

public interface INoteStore
{
    /// <summary>Destinatário existe (tem jogador criado).</summary>
    Task<bool> ExistsAsync(long account);
    Task<int> UndeliveredAsync(long account);
    /// <summary>Grava o bilhete e o custo do remetente na mesma transação.</summary>
    Task SendAsync(long to, long from, string fromNick, string text, PlayerChanges senderCost);
    /// <summary>Os mais novos (para a lista do cliente, que é substituída a cada entrega).</summary>
    Task<List<Note>> RecentAsync(long account, int max);
    Task MarkDeliveredAsync(long account);
}

/// <summary>
/// Bilhetes pelo game (0x3C/0x111, SPEC-messenger.md §5.3): até 63 bytes, 10 pang, só para outro jogador que existe e
/// não me bloqueou no mensageiro; no máximo <see cref="BoxLimit"/> não lidos por destinatário.
/// </summary>
public sealed class NoteService(INoteStore store, IFriendStore? friends = null)
{
    public const long Cost = 10;
    public const int MaxText = 63, BoxLimit = 50, ListSize = 20;
    public INoteStore Store => store;

    public async Task<NoteCode> SendAsync(Player from, long to, string text)
    {
        text = text.Trim();
        if (to == from.AccountId) return NoteCode.Myself;
        if (text.Length == 0 || text.Length > MaxText || to <= 0 || !await store.ExistsAsync(to)) return NoteCode.Failed;
        if (from.Pang < Cost) return NoteCode.NoPang;
        if (friends != null && await friends.GetAsync(to, from.AccountId) is { Blocked: true }) return NoteCode.Failed;
        if (await store.UndeliveredAsync(to) >= BoxLimit) return NoteCode.BoxFull;
        await store.SendAsync(to, from.AccountId, from.Nickname, text, new PlayerChanges { Pang = from.Pang - Cost });
        from.Pang -= Cost;
        return NoteCode.Ok;
    }
}
