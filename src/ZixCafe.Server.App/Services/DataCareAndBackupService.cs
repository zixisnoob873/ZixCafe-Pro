using Microsoft.EntityFrameworkCore;
using System.IO;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class DataCareAndBackupService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly VenueSettingsService _venueSettings;

    public DataCareAndBackupService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        VenueSettingsService venueSettings)
    {
        _dbFactory = dbFactory;
        _venueSettings = venueSettings;
    }

    public async Task<ResultResponse> TriggerBackupAsync(string? targetDirectory, string cashierName)
    {
        var settings = await _venueSettings.GetSettingsAsync();
        var backupDir = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : (!string.IsNullOrWhiteSpace(settings.AutoBackupPath)
                ? settings.AutoBackupPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZixCafe", "Backups"));

        try
        {
            Directory.CreateDirectory(backupDir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupFile = Path.Combine(backupDir, $"zixcafe_backup_{timestamp}.db");

            await using var db = await _dbFactory.CreateDbContextAsync();
            var sql = $"VACUUM INTO '{backupFile.Replace("'", "''")}';";
            await db.Database.ExecuteSqlRawAsync(sql);

            // Update settings LastBackupAtUtc
            settings.LastBackupAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await AppendAuditAsync(db, "db.backup", null, $"file={backupFile}", cashierName);
            await db.SaveChangesAsync();

            return new ResultResponse(true, null);
        }
        catch (Exception ex)
        {
            return new ResultResponse(false, $"Backup failed: {ex.Message}");
        }
    }

    public async Task<string> GetDatabaseInfoAsync()
    {
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZixCafe", "zixcafe.db");
        var walPath = dbPath + "-wal";

        var dbSize = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;
        var walSize = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var sessionCount = await db.Sessions.CountAsync();
        var auditCount = await db.AuditEntries.CountAsync();
        var memberCount = await db.Members.CountAsync();

        return $"Database Size: {dbSize / 1024.0 / 1024.0:F2} MB (WAL: {walSize / 1024.0 / 1024.0:F2} MB) | Sessions: {sessionCount}, Audits: {auditCount}, Members: {memberCount}";
    }

    private static async Task AppendAuditAsync(ZixCafeDbContext db, string action, string? targetId, string? detail, string cashier)
    {
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, "Database", targetId, detail, cashier, now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            TargetType = "Database",
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashier,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
    }
}
