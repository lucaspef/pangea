using Pangya.Domain.Auth;

namespace Pangya.Domain.Accounts;

public enum LoginOutcome { Ok, BadKey, Blocked, NeedsNickname }

public sealed record LoginResult(LoginOutcome Outcome, Account? Account = null, string? GameKey = null);

/// <summary>
/// Login no login server: confere a AuthKey emitida pelo Web (uso único, da mesma conta e MemberNo),
/// bloqueio e nickname; no sucesso emite a chave do game server.
/// </summary>
public sealed class LoginService(IAccountStore accounts, SessionService sessions)
{
    public async Task<LoginResult> LoginAsync(string login, string authKey, long memberNo)
    {
        var accountId = await sessions.ConsumeWebAuthAsync(authKey);
        if (accountId == null || accountId != memberNo) return new(LoginOutcome.BadKey);
        var acc = await accounts.FindByIdAsync(accountId.Value);
        if (acc == null || !string.Equals(acc.Login, login, StringComparison.OrdinalIgnoreCase)) return new(LoginOutcome.BadKey);
        return await ContinueAsync(acc);
    }

    /// <summary>Etapas depois da chave (também usada após criar o nickname).</summary>
    public async Task<LoginResult> ContinueAsync(Account acc)
    {
        if (acc.IsBlocked(DateTime.UtcNow)) return new(LoginOutcome.Blocked, acc);
        if (string.IsNullOrEmpty(acc.Nickname)) return new(LoginOutcome.NeedsNickname, acc);
        return new(LoginOutcome.Ok, acc, await sessions.IssueGameLoginAsync(acc.Id));
    }
}
