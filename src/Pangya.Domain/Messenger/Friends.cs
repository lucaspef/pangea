namespace Pangya.Domain.Messenger;

/// <summary>Lado de uma amizade (tabela friends): eu pedi, ele pediu (falta eu aceitar) ou amigos.</summary>
public enum FriendState : short { Requested = 1, Pending = 2, Accepted = 3 }

/// <summary>Um amigo da minha lista (com o que o outro lado decidiu sobre mim).</summary>
public sealed record Friend(long AccountId, string Nickname, int Level, FriendState State, string Alias, bool Blocked, bool BlockedMe);

/// <summary>Códigos do 0x2E/0x104 (pedido de amizade) e das outras respostas (0 = ok).</summary>
public enum FriendCode : uint
{
    Ok = 0, Failed = 1, MyListFull = 2, TheirListFull = 3, AlreadyRequested = 4, Refused = 5, Myself = 6, NoSuchNick = 7,
    NotFriend = 9, AlreadyFriend = 0xB, AccountDeleted = 0xD,
}

public interface IFriendStore
{
    /// <summary>Minha lista (todos os estados), com nick e nível atuais.</summary>
    Task<List<Friend>> ListAsync(long owner);
    Task<Friend?> GetAsync(long owner, long friend);
    Task<int> CountAsync(long owner);
    /// <summary>Conta e nick exato pelo nick (sem diferenciar maiúsculas); null = não existe.</summary>
    Task<(long Id, string Nickname)?> FindByNickAsync(string nick);
    /// <summary>Pedido de A para B: (A, B, pedido) e (B, A, pendente), numa transação.</summary>
    Task RequestAsync(long from, long to);
    /// <summary>Aceita: os dois lados viram amigos (falha se o pedido sumiu).</summary>
    Task<bool> AcceptAsync(long owner, long friend);
    /// <summary>Apaga os dois lados.</summary>
    Task RemoveAsync(long a, long b);
    Task<bool> SetBlockedAsync(long owner, long friend, bool blocked);
    Task<bool> SetAliasAsync(long owner, long friend, string alias);
}

/// <summary>
/// Regras de amizade do mensageiro (SPEC-messenger.md §3, §4.3): pedido, aceitar, apagar, bloquear e apelido. Tudo
/// revalidado aqui (o cliente só manda uid e nick).
/// </summary>
public sealed class FriendService(IFriendStore store, int maxFriends = FriendService.DefaultMax)
{
    public const int DefaultMax = 50, MaxAlias = 10, MaxNick = 21;
    public IFriendStore Store => store;

    /// <summary>0x17: procurar o nick para a janela "adicionar amigo".</summary>
    public async Task<(FriendCode Code, long Id, string Nick)> LookupAsync(long me, string nick)
    {
        nick = nick.Trim();
        if (nick.Length is 0 or > MaxNick) return (FriendCode.NoSuchNick, 0, "");
        if (await store.FindByNickAsync(nick) is not { } f) return (FriendCode.NoSuchNick, 0, "");
        return f.Id == me ? (FriendCode.Myself, 0, "") : (FriendCode.Ok, f.Id, f.Nickname);
    }

    /// <summary>0x18: pedir amizade (o uid tem de bater com o nick, como o cliente mandou).</summary>
    public async Task<FriendCode> RequestAsync(long me, long target, string nick)
    {
        if (target == me) return FriendCode.Myself;
        if (await store.FindByNickAsync(nick.Trim()) is not { } f || f.Id != target) return FriendCode.NoSuchNick;
        if (await store.GetAsync(me, target) is { } mine)
            return mine.State switch
            {
                FriendState.Accepted => FriendCode.AlreadyFriend,
                FriendState.Requested => FriendCode.AlreadyRequested,
                _ => FriendCode.AlreadyRequested,                           // ele já pediu: é só aceitar (0x19)
            };
        if (await store.CountAsync(me) >= maxFriends) return FriendCode.MyListFull;
        if (await store.CountAsync(target) >= maxFriends) return FriendCode.TheirListFull;
        await store.RequestAsync(me, target);
        return FriendCode.Ok;
    }

    /// <summary>0x19: aceitar o pedido que ele me fez.</summary>
    public async Task<FriendCode> AcceptAsync(long me, long other)
    {
        if (await store.GetAsync(me, other) is not { State: FriendState.Pending }) return FriendCode.NotFriend;
        return await store.AcceptAsync(me, other) ? FriendCode.Ok : FriendCode.NotFriend;
    }

    /// <summary>0x1C: apagar (também serve para recusar um pedido).</summary>
    public async Task<FriendCode> RemoveAsync(long me, long other)
    {
        if (await store.GetAsync(me, other) == null) return FriendCode.NotFriend;
        await store.RemoveAsync(me, other);
        return FriendCode.Ok;
    }

    public async Task<FriendCode> BlockAsync(long me, long other, bool block) =>
        await store.SetBlockedAsync(me, other, block) ? FriendCode.Ok : FriendCode.NotFriend;

    /// <summary>0x1F: apelido (até 10 caracteres, como o cliente copia).</summary>
    public async Task<(FriendCode Code, string Alias)> AliasAsync(long me, long other, string alias)
    {
        alias = alias.Trim();
        if (alias.Length > MaxAlias) alias = alias[..MaxAlias];
        return await store.SetAliasAsync(me, other, alias) ? (FriendCode.Ok, alias) : (FriendCode.NotFriend, "");
    }
}
