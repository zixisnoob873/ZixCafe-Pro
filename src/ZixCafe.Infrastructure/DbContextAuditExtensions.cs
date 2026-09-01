using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Services;

namespace ZixCafe.Infrastructure;

/// <summary>
/// Centralized extension methods for recording tamper-evident cryptographic audit entries
/// directly on the EF Core database context.
/// </summary>
public static class DbContextAuditExtensions
{
    public static async Task<AuditEntry> AppendAuditAsync(
        this ZixCafeDbContext db,
        string action,
        string targetType,
        string? targetId,
        string? detailJson,
        string? cashierName,
        CancellationToken cancellationToken = default)
    {
        var lastAudit = await db.AuditEntries
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var prevHash = lastAudit?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, targetType, targetId, detailJson, cashierName, now);

        var entry = new AuditEntry
        {
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            DetailJson = detailJson,
            CashierName = string.IsNullOrWhiteSpace(cashierName) ? "System" : cashierName,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        };

        db.AuditEntries.Add(entry);
        return entry;
    }
}
