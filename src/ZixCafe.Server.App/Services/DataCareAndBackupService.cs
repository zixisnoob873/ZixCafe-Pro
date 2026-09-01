using Microsoft.Data.Sqlite;
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
                : GetDefaultBackupDirectory());

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

            await AppendAuditAsync(db, "db.backup", null, $"file={Path.GetFileName(backupFile)}", cashierName);
            await db.SaveChangesAsync();

            return new ResultResponse(true, backupFile);
        }
        catch (Exception ex)
        {
            return new ResultResponse(false, $"Backup failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<BackupFileInfoDto>> ListBackupsAsync(string? directory = null)
    {
        var settings = await _venueSettings.GetSettingsAsync();
        var backupDir = !string.IsNullOrWhiteSpace(directory)
            ? directory
            : (!string.IsNullOrWhiteSpace(settings.AutoBackupPath)
                ? settings.AutoBackupPath
                : GetDefaultBackupDirectory());

        if (!Directory.Exists(backupDir))
        {
            return Array.Empty<BackupFileInfoDto>();
        }

        var dirInfo = new DirectoryInfo(backupDir);
        var files = dirInfo.GetFiles("*.db")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileInfoDto(f.Name, f.FullName, f.Length, f.CreationTimeUtc))
            .ToList();

        return files;
    }

    public async Task<ResultResponse> RestoreBackupAsync(string backupFilePath, string cashierName)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
        {
            return new ResultResponse(false, "The specified backup file does not exist.");
        }

        if (!IsValidSqliteDatabase(backupFilePath))
        {
            return new ResultResponse(false, "The selected file is not a valid SQLite database.");
        }

        var liveDbPath = DatabasePaths.DefaultDatabaseFile;
        var walPath = liveDbPath + "-wal";
        var shmPath = liveDbPath + "-shm";

        try
        {
            // 1. Create a safety pre-restore backup of the live database
            var backupDir = GetDefaultBackupDirectory();
            Directory.CreateDirectory(backupDir);
            var preRestoreFile = Path.Combine(backupDir, $"zixcafe_pre_restore_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");

            if (File.Exists(liveDbPath))
            {
                try
                {
                    await using var currentDb = await _dbFactory.CreateDbContextAsync();
                    await currentDb.Database.ExecuteSqlRawAsync($"VACUUM INTO '{preRestoreFile.Replace("'", "''")}';");
                }
                catch
                {
                    File.Copy(liveDbPath, preRestoreFile, overwrite: true);
                }
            }

            // 2. Clear all active SQLite connection pools so file locks are released
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // 3. Overwrite live database file with the selected backup
            File.Copy(backupFilePath, liveDbPath, overwrite: true);
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            // 4. Run EF Core migrations on the restored database to ensure schema alignment
            await using var newDb = await _dbFactory.CreateDbContextAsync();
            await newDb.Database.MigrateAsync();

            // 5. Append restore audit log record
            await AppendAuditAsync(newDb, "db.restore", null, $"source={Path.GetFileName(backupFilePath)}", cashierName);
            await newDb.SaveChangesAsync();

            return new ResultResponse(true, null);
        }
        catch (Exception ex)
        {
            return new ResultResponse(false, $"Restore failed: {ex.Message}");
        }
    }

    public static bool IsValidSqliteDatabase(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var fi = new FileInfo(filePath);
            if (fi.Length < 100) return false;

            using var stream = File.OpenRead(filePath);
            var header = new byte[16];
            var read = stream.Read(header, 0, 16);
            if (read < 16) return false;

            var headerStr = System.Text.Encoding.ASCII.GetString(header);
            return headerStr.StartsWith("SQLite format 3");
        }
        catch
        {
            return false;
        }
    }

    public static string GetDefaultBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ZixCafe",
            "Backups");
    }

    public async Task<string> GetDatabaseInfoAsync()
    {
        var dbPath = DatabasePaths.DefaultDatabaseFile;
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
