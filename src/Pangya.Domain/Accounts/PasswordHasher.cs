using System.Security.Cryptography;
using System.Text;

namespace Pangya.Domain.Accounts;

/// <summary>
/// Hash de senha com PBKDF2-SHA512 (sal aleatório de 16 bytes). Formato guardado:
/// <c>pbkdf2-sha512$iterações$sal$hash</c> (base64), para poder subir as iterações no futuro
/// sem invalidar as senhas antigas (<see cref="NeedsRehash"/>).
/// </summary>
public static class PasswordHasher
{
    public const int Iterations = 210_000;           // recomendação OWASP para PBKDF2-SHA512
    const string Scheme = "pbkdf2-sha512";

    public static string Hash(string password, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, 32);
        return $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != Scheme || !int.TryParse(p[1], out var it) || it < 1) return false;
        var expected = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(p[2]), it, HashAlgorithmName.SHA512, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool NeedsRehash(string stored) => !stored.StartsWith($"{Scheme}${Iterations}$");
}
