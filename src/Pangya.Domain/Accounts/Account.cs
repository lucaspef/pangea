using System.Text.RegularExpressions;

namespace Pangya.Domain.Accounts;

/// <summary>Conta de jogador. Id é o MemberNo/UID visto pelo cliente.</summary>
public sealed record Account(long Id, string Login, string PasswordHash, string? Nickname, int IdentityFlags,
    DateTime? BlockedUntil, string? BlockReason)
{
    public bool IsBlocked(DateTime now) => BlockedUntil is { } until && until > now;
}

/// <summary>Regras de formato (as mesmas no cadastro, no auto-cadastro e no banco).</summary>
public static partial class AccountRules
{
    public const int MinPassword = 6, MaxPassword = 64;

    [GeneratedRegex("^[A-Za-z0-9_]{3,20}$")]
    private static partial Regex LoginRegex();

    public static bool IsValidLogin(string login) => LoginRegex().IsMatch(login);
    public static bool IsValidPassword(string pw) => pw.Length is >= MinPassword and <= MaxPassword;
}

/// <summary>Acesso às contas (implementado em Pangya.Data).</summary>
public interface IAccountStore
{
    Task<Account?> FindByLoginAsync(string login);
    Task<Account?> FindByIdAsync(long id);
    /// <summary>Cria a conta; null se o login já existe.</summary>
    Task<Account?> CreateAsync(string login, string passwordHash);
    /// <summary>Define o nickname; false se já é de outra conta.</summary>
    Task<bool> SetNicknameAsync(long id, string nickname);
    Task<bool> NicknameExistsAsync(string nickname);
    Task UpdatePasswordHashAsync(long id, string passwordHash);
    /// <summary>Bits de identidade do cliente: 0x04 GM, 0x10 GM visível, 0x0E admin/desenvolvedor.</summary>
    Task SetIdentityFlagsAsync(long id, int flags);
    Task RecordLoginAsync(long id, string ip);
    Task<Account?> FindByNicknameAsync(string nickname);
    /// <summary>Bloqueia até <paramref name="until"/> (null = desbloqueia).</summary>
    Task SetBlockAsync(long id, DateTime? until, string? reason);
}
