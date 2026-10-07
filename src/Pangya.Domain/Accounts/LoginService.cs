using Pangya.Domain.Auth;
using Pangya.Domain.Players;

namespace Pangya.Domain.Accounts;

public enum LoginOutcome { Ok, BadKey, Blocked, NeedsNickname, NeedsCharacter }

public sealed record LoginResult(LoginOutcome Outcome, Account? Account = null, string? GameKey = null);

/// <summary>
/// Login no login server: confere a AuthKey emitida pelo Web (uso único, da mesma conta e MemberNo), bloqueio,
/// nickname e primeiro personagem; no sucesso emite a chave do game server.
/// Conta nova: NeedsNickname -> (conferir/criar nickname) -> NeedsCharacter -> (criar personagem) -> Ok.
/// </summary>
public sealed class LoginService(IAccountStore accounts, SessionService sessions, PlayerService players)
{
    public async Task<LoginResult> LoginAsync(string login, string authKey, long memberNo)
    {
        var accountId = await sessions.ConsumeWebAuthAsync(authKey);
        if (accountId == null || accountId != memberNo) return new(LoginOutcome.BadKey);
        var acc = await accounts.FindByIdAsync(accountId.Value);
        if (acc == null || !string.Equals(acc.Login, login, StringComparison.OrdinalIgnoreCase)) return new(LoginOutcome.BadKey);
        return await ContinueAsync(acc);
    }

    /// <summary>Etapas depois da chave (também usada após criar nickname e personagem).</summary>
    public async Task<LoginResult> ContinueAsync(Account acc)
    {
        acc = await accounts.FindByIdAsync(acc.Id) ?? acc;
        if (acc.IsBlocked(DateTime.UtcNow)) return new(LoginOutcome.Blocked, acc);
        if (string.IsNullOrEmpty(acc.Nickname)) return new(LoginOutcome.NeedsNickname, acc);
        if (await players.LoadAsync(acc.Id) == null) return new(LoginOutcome.NeedsCharacter, acc);
        return new(LoginOutcome.Ok, acc, await sessions.IssueGameLoginAsync(acc.Id));
    }

    public async Task<NicknameStatus> CheckNicknameAsync(Account acc, string nick) =>
        !NicknameRules.IsValid(nick, acc.Login) ? NicknameStatus.Invalid
        : await accounts.NicknameExistsAsync(nick) ? NicknameStatus.Taken : NicknameStatus.Ok;

    /// <summary>Só para conta ainda sem nickname (o nickname não muda depois).</summary>
    public async Task<NicknameStatus> CreateNicknameAsync(Account acc, string nick)
    {
        if (!string.IsNullOrEmpty(acc.Nickname)) return NicknameStatus.Error;
        var st = await CheckNicknameAsync(acc, nick);
        if (st != NicknameStatus.Ok) return st;
        return await accounts.SetNicknameAsync(acc.Id, nick) ? NicknameStatus.Ok : NicknameStatus.Taken;
    }

    public async Task<bool> CreateCharacterAsync(Account acc, int characterTypeId, int hair, int shirt) =>
        !string.IsNullOrEmpty(acc.Nickname) && await players.CreateAsync(acc.Id, characterTypeId, hair, shirt) != null;
}
