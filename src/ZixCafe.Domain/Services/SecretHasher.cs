using System.Security.Cryptography;

namespace ZixCafe.Domain.Services;

/// <summary>
/// PIN/secret hashing with PBKDF2-HMAC-SHA256 (210k iterations, 128-bit
/// salt). Encoded as: pbkdf2-sha256$iterations$saltB64$hashB64 — upgrades
/// to new parameters must re-encode on next successful verification.
/// </summary>
public static class SecretHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string secret, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
        {
            return false;
        }
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
