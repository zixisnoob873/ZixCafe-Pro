using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Cryptography;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;

namespace ZixCafe.Infrastructure;

public static class DbInitializer
{
    public static async Task InitializeAsync(ZixCafeDbContext db)
    {
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception)
        {
            await db.Database.EnsureCreatedAsync();
        }

        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;");
        await db.Database.CloseConnectionAsync();

        if (!await db.Settings.AnyAsync())
        {
            await SeedAsync(db);
        }
    }

    private static async Task SeedAsync(ZixCafeDbContext db)
    {
        db.VenueSettings.Add(new VenueSettings
        {
            VenueName = "ZixCafe Venue [SAMPLE]",
            CurrencyCode = "USD",
            CurrencySymbol = "$",
            Locale = "en-US",
            TaxLabel = "TAX",
            TaxRatePercent = 0m,
            DefaultOpeningFloat = 50.00m,
            UsbRatePerGb = 1.00m,
            PrintCostPerPage = 0.10m,
            ClosingTime = "02:00",
            IsConfigured = true
        });

        db.Settings.AddRange(
            new Setting { Key = "venue.name", Value = "ZixCafe Venue [SAMPLE]" },
            new Setting { Key = "venue.currency", Value = "USD" },
            new Setting { Key = "venue.locale", Value = "en-US" },
            new Setting { Key = "server.port", Value = "40000" },
            new Setting { Key = "usb.rate.per.gb", Value = "1.00" },
            new Setting { Key = "print.cost.per.page", Value = "0.10" },
            new Setting { Key = "closing.time", Value = "02:00" });

        var zones = new[]
        {
            new Zone { Name = "Main Floor", DisplayOrder = 1 },
            new Zone { Name = "VIP Room", DisplayOrder = 2 },
            new Zone { Name = "Console Corner", DisplayOrder = 3 }
        };
        db.Zones.AddRange(zones);

        var terminals = new List<Terminal>();
        for (var i = 1; i <= 48; i++)
        {
            terminals.Add(new Terminal
            {
                Name = $"PC-{i:00}",
                Zone = zones[0],
                Status = TerminalStatus.Available
            });
        }
        for (var i = 1; i <= 8; i++)
        {
            terminals.Add(new Terminal
            {
                Name = $"VIP-{i:00}",
                Zone = zones[1],
                Status = TerminalStatus.Available
            });
        }
        for (var i = 1; i <= 8; i++)
        {
            terminals.Add(new Terminal
            {
                Name = "CON-" + (char)('A' + i - 1),
                Zone = zones[2],
                Status = TerminalStatus.Available
            });
        }
        db.Terminals.AddRange(terminals);

        var flatTariff = new Tariff
        {
            Name = "Standard Hourly [SAMPLE]",
            Model = TariffModel.Flat,
            BaseRatePerHour = 2.00m,
            RoundingMinutes = 5,
            MinimumCharge = 0.50m,
            Priority = 10
        };
        var peakTariff = new Tariff
        {
            Name = "Peak Hours [SAMPLE]",
            Model = TariffModel.DaySchedule,
            BaseRatePerHour = 2.00m,
            RoundingMinutes = 5,
            MinimumCharge = 0.50m,
            Priority = 20,
            Rules =
            {
                new TariffRule { DaysMask = 0b0111110, StartMinute = 17 * 60, EndMinute = 24 * 60, RatePerHour = 3.00m },
                new TariffRule { DaysMask = 0b1000001, StartMinute = 10 * 60, EndMinute = 24 * 60, RatePerHour = 3.00m }
            }
        };
        db.Tariffs.AddRange(flatTariff, peakTariff);

        var tiers = new[]
        {
            new MemberTier { Name = "Bronze [SAMPLE]", DiscountPercent = 0m, Priority = 1 },
            new MemberTier { Name = "Silver [SAMPLE]", DiscountPercent = 5m, MinTopUpAmount = 20m, Priority = 2 },
            new MemberTier { Name = "Gold [SAMPLE]", DiscountPercent = 10m, MinTopUpAmount = 50m, Priority = 3 }
        };
        db.MemberTiers.AddRange(tiers);

        db.Cashiers.AddRange(
            new Cashier { Name = "admin", PinHash = SecretHasher.Hash("1234"), Role = CashierRole.Owner },
            new Cashier { Name = "manager", PinHash = SecretHasher.Hash("2222"), Role = CashierRole.Manager },
            new Cashier { Name = "demo", PinHash = SecretHasher.Hash("0000"), Role = CashierRole.Staff });

        db.Members.Add(new Member
        {
            Code = "M-0001",
            Name = "Sample Member [SAMPLE]",
            TimeBalanceMinutes = 300,
            MoneyBalance = 25.00m
        });

        db.Products.AddRange(
            new Product { Sku = "BEV-001", Name = "Energy Drink 250ml [SAMPLE]", Category = "Drinks", Price = 2.50m, StockQty = 120, LowStockThreshold = 24 },
            new Product { Sku = "BEV-002", Name = "Bottled Water 500ml [SAMPLE]", Category = "Drinks", Price = 1.00m, StockQty = 200, LowStockThreshold = 40 },
            new Product { Sku = "SNK-001", Name = "Chips Regular [SAMPLE]", Category = "Snacks", Price = 1.50m, StockQty = 80, LowStockThreshold = 16 },
            new Product { Sku = "SNK-002", Name = "Instant Noodles [SAMPLE]", Category = "Snacks", Price = 1.25m, StockQty = 60, LowStockThreshold = 12 },
            new Product { Sku = "PER-001", Name = "Gaming Headset (loan) [SAMPLE]", Category = "Accessories", Price = 0m, StockQty = 10, LowStockThreshold = 2 });

        db.ProhibitedApps.AddRange(
            new ProhibitedApp { Match = "cheatengine", MatchKind = "ProcessName", KillOnSight = true },
            new ProhibitedApp { Match = "torrent", MatchKind = "ProcessName", KillOnSight = true });

        // Sample ticket vouchers, stored in compact form (no dashes) so the
        // redeem lookup can normalize whatever the desk types.
        db.Tickets.AddRange(
            new TicketVoucher
            {
                Code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create()).Replace("-", string.Empty),
                Type = TicketType.Duration,
                DurationMinutes = 90,
                Price = 4.00m,
                IssuedBy = "sample"
            },
            new TicketVoucher
            {
                Code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create()).Replace("-", string.Empty),
                Type = TicketType.Credit,
                CreditAmount = 50.00m,
                Price = 50.00m,
                IssuedBy = "sample"
            });

        db.AuditEntries.Add(NewAudit("system.bootstrap", "Venue", null, "Venue initialized", "system"));

        await db.SaveChangesAsync();
    }

    public static AuditEntry NewAudit(
        string action, string targetType, string? targetId, string detail, string cashierName, string? prevHash = null)
    {
        var prev = prevHash ?? string.Empty;
        var (p, hash) = AuditChain.Link(prev, action, targetType, targetId, detail, cashierName, DateTime.UtcNow);
        return new AuditEntry
        {
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashierName,
            PrevHash = p,
            Hash = hash
        };
    }
}
