using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;

namespace ZixCafe.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ZixCafeDbContext>
{
    public ZixCafeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ZixCafeDbContext>()
            .UseSqlite($"Data Source={DatabasePaths.DefaultDatabaseFile}")
            .Options;
        return new ZixCafeDbContext(options);
    }
}

public static class DatabasePaths
{
    public static string DefaultDatabaseFile
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ZixCafe");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "zixcafe.db");
        }
    }
}
