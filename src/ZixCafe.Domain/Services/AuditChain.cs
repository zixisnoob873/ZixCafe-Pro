using System.Security.Cryptography;
using System.Text;

namespace ZixCafe.Domain.Services;

/// <summary>
/// Tamper-evident audit chain: each entry hashes its content together with
/// the previous entry's hash. Verification replays the chain and reports
/// the first divergence — silent edits are never possible, only detectable.
/// </summary>
public static class AuditChain
{
    public static (string prevHash, string hash) Link(
        string prevHash, string action, string targetType,
        string? targetId, string? detailJson, string? cashierName, DateTime createdAtUtc)
    {
        var payload = string.Concat(
            prevHash, '|', action, '|', targetType, '|',
            targetId ?? string.Empty, '|',
            detailJson ?? string.Empty, '|',
            cashierName ?? string.Empty, '|',
            createdAtUtc.ToString("O"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return (prevHash, Convert.ToHexString(bytes));
    }
}
