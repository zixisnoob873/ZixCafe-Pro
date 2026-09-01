using System.Security.Cryptography;

namespace ZixCafe.Domain.Services;

/// <summary>
/// Generates human-safe ticket codes: Crockford Base32 alphabet (no
/// I, L, O, U), grouped in blocks of four, with a Mod-32 check character
/// so front-desk transcription errors surface at entry time.
/// </summary>
public static class TicketCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewCode(RandomNumberGenerator rng, int blocks = 3)
    {
        var letters = blocks * 4;
        var bytes = RandomNumberGenerator.GetBytes(letters);
        var chars = new char[letters + blocks - 1];
        var p = 0;
        for (var i = 0; i < letters; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                chars[p++] = '-';
            }
            chars[p++] = Alphabet[bytes[i] & 31];
        }
        return AppendCheck(chars, Alphabet);
    }

    public static bool IsValidFormat(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }
        var compact = code.Replace("-", string.Empty).ToUpperInvariant();
        if (compact.Length < 5)
        {
            return false;
        }
        var body = compact.AsSpan(0, compact.Length - 1);
        var check = compact[^1];
        return ComputeCheckChar(body) == check
            && AllAlphabet(body);
    }

    private static bool AllAlphabet(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!Alphabet.Contains(c)) return false;
        }
        return true;
    }

    private static string AppendCheck(char[] body, string alphabet)
    {
        var compact = new string(body).Replace("-", string.Empty);
        return string.Concat(new string(body), "-", ComputeCheckChar(compact.AsSpan()).ToString());
    }

    private static char ComputeCheckChar(ReadOnlySpan<char> body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var idx = Alphabet.IndexOf(body[i]);
            if (idx < 0)
            {
                return Alphabet[0];
            }
            sum = (sum * 32 + idx) % 31;
        }
        return Alphabet[sum % Alphabet.Length];
    }
}
