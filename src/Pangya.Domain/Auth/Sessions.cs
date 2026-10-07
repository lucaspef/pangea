using System.Security.Cryptography;

namespace Pangya.Domain.Auth;

public enum SessionKind : short
{
    /// <summary>AuthKey do login web; o cliente manda como "senha" no login server. Uso único.</summary>
    WebAuth = 1,
    /// <summary>Chave que o login server dá para entrar no game server (vale para trocar de servidor).</summary>
    GameLogin = 2,
}

/// <summary>Armazenamento das chaves de sessão (implementado em Pangya.Data).</summary>
public interface ISessionStore
{
    Task CreateAsync(string key, long accountId, SessionKind kind, DateTime expiresUtc, bool replaceOthers);
    /// <summary>Conta dona da chave, se válida. consume = true apaga a chave (uso único).</summary>
    Task<long?> ValidateAsync(string key, SessionKind kind, bool consume);
}

/// <summary>Papel do "Auth interno": emite e confere as chaves que ligam Web -> Login -> Game.</summary>
public sealed class SessionService(ISessionStore store)
{
    public static readonly TimeSpan WebAuthTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GameLoginTtl = TimeSpan.FromHours(24);

    /// <summary>16 caracteres hexa maiúsculos (64 bits aleatórios), o mesmo formato já aceito pelo cliente.</summary>
    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    public async Task<string> IssueWebAuthAsync(long accountId)
    {
        var key = NewKey();
        await store.CreateAsync(key, accountId, SessionKind.WebAuth, DateTime.UtcNow + WebAuthTtl, replaceOthers: false);
        return key;
    }

    /// <summary>Nova chave de game server; as anteriores da conta deixam de valer.</summary>
    public async Task<string> IssueGameLoginAsync(long accountId)
    {
        var key = NewKey();
        await store.CreateAsync(key, accountId, SessionKind.GameLogin, DateTime.UtcNow + GameLoginTtl, replaceOthers: true);
        return key;
    }

    public Task<long?> ConsumeWebAuthAsync(string key) =>
        IsWellFormed(key) ? store.ValidateAsync(key, SessionKind.WebAuth, consume: true) : Task.FromResult<long?>(null);

    public Task<long?> ValidateGameLoginAsync(string key) =>
        IsWellFormed(key) ? store.ValidateAsync(key, SessionKind.GameLogin, consume: false) : Task.FromResult<long?>(null);

    static bool IsWellFormed(string key)
    {
        if (key.Length != 16) return false;
        foreach (var ch in key)
            if (!Uri.IsHexDigit(ch)) return false;
        return true;
    }
}
