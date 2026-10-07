using Pangya.Core.Limits;

namespace Pangya.Domain.Accounts;

public enum AuthStatus { Ok, InvalidInput, UnknownLogin, WrongPassword, Blocked, TooManyAttempts }

public enum RegisterStatus { Ok, InvalidLogin, InvalidPassword, LoginTaken, TooManyAttempts }

/// <summary>
/// Cadastro e autenticação de contas. Limita tentativas por IP e por login (sem tocar no banco quando
/// passa do limite). Auto-cadastro (só para testes): login desconhecido com formato válido vira conta nova.
/// </summary>
public sealed class AccountService(IAccountStore store, int maxAttemptsPerMinute, bool autoRegister = false)
{
    readonly AttemptLimiter attempts = new(maxAttemptsPerMinute, TimeSpan.FromMinutes(1));

    public bool AutoRegister => autoRegister;

    public async Task<(RegisterStatus Status, Account? Account)> RegisterAsync(string login, string password, string ip)
    {
        if (!attempts.TryAttempt("reg:" + ip)) return (RegisterStatus.TooManyAttempts, null);
        if (!AccountRules.IsValidLogin(login)) return (RegisterStatus.InvalidLogin, null);
        if (!AccountRules.IsValidPassword(password)) return (RegisterStatus.InvalidPassword, null);
        if (await store.FindByLoginAsync(login) != null) return (RegisterStatus.LoginTaken, null);
        var acc = await store.CreateAsync(login, PasswordHasher.Hash(password));
        return acc == null ? (RegisterStatus.LoginTaken, null) : (RegisterStatus.Ok, acc);
    }

    public async Task<(AuthStatus Status, Account? Account)> AuthenticateAsync(string login, string password, string ip)
    {
        if (!attempts.TryAttempt("ip:" + ip) || !attempts.TryAttempt("login:" + login.ToLowerInvariant()))
            return (AuthStatus.TooManyAttempts, null);
        if (!AccountRules.IsValidLogin(login) || password.Length is 0 or > AccountRules.MaxPassword)
            return (AuthStatus.InvalidInput, null);

        var acc = await store.FindByLoginAsync(login);
        if (acc == null)
        {
            if (!autoRegister || !AccountRules.IsValidPassword(password)) return (AuthStatus.UnknownLogin, null);
            acc = await store.CreateAsync(login, PasswordHasher.Hash(password));
            if (acc == null) return (AuthStatus.UnknownLogin, null);
        }
        else if (!PasswordHasher.Verify(password, acc.PasswordHash))
            return (AuthStatus.WrongPassword, null);
        else if (PasswordHasher.NeedsRehash(acc.PasswordHash))
            await store.UpdatePasswordHashAsync(acc.Id, PasswordHasher.Hash(password));

        if (acc.IsBlocked(DateTime.UtcNow)) return (AuthStatus.Blocked, acc);
        await store.RecordLoginAsync(acc.Id, ip);
        return (AuthStatus.Ok, acc);
    }
}
