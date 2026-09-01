using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;
using System.Globalization;

namespace ZixCafe.Server.App.Services;

public class VenueSettingsService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private VenueSettings? _cachedSettings;

    public VenueSettingsService(IDbContextFactory<ZixCafeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<VenueSettings> GetSettingsAsync()
    {
        if (_cachedSettings is not null)
        {
            return _cachedSettings;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var settings = await db.VenueSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new VenueSettings
            {
                VenueName = "ZixCafe Venue",
                CurrencyCode = "USD",
                CurrencySymbol = "$",
                Locale = "en-US",
                TaxLabel = "TAX",
                TaxRatePercent = 0m,
                DefaultOpeningFloat = 50.00m,
                UsbRatePerGb = 1.00m,
                PrintCostPerPage = 0.10m,
                ClosingTime = "02:00",
                IsConfigured = false
            };
            db.VenueSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        _cachedSettings = settings;
        return settings;
    }

    public async Task<VenueSettingsDto> GetSettingsDtoAsync()
    {
        var s = await GetSettingsAsync();
        return new VenueSettingsDto(
            s.VenueName,
            s.CurrencyCode,
            s.CurrencySymbol,
            s.Locale,
            s.TaxLabel,
            s.TaxRatePercent,
            s.DefaultOpeningFloat,
            s.UsbRatePerGb,
            s.PrintCostPerPage,
            s.ClosingTime,
            s.LicenseKey,
            s.AutoBackupPath,
            s.AutoBackupIntervalHours,
            s.LastBackupAtUtc,
            s.IsConfigured,
            s.EnableHardwareAntiTheftWatchdog,
            s.EnforceNativeRefreshRate,
            s.EnableRebootOnSessionEnd,
            s.DisklessProvider,
            s.OfflineGracePeriodSeconds);
    }

    public async Task<ResultResponse> SaveSettingsAsync(VenueSettingsDto dto, string requestingCashier)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var s = await db.VenueSettings.FirstOrDefaultAsync();
        if (s is null)
        {
            s = new VenueSettings();
            db.VenueSettings.Add(s);
        }

        s.VenueName = string.IsNullOrWhiteSpace(dto.VenueName) ? "ZixCafe Venue" : dto.VenueName.Trim();
        s.CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? "USD" : dto.CurrencyCode.Trim().ToUpperInvariant();
        s.CurrencySymbol = string.IsNullOrWhiteSpace(dto.CurrencySymbol) ? "$" : dto.CurrencySymbol.Trim();
        s.Locale = string.IsNullOrWhiteSpace(dto.Locale) ? "en-US" : dto.Locale.Trim();
        s.TaxLabel = string.IsNullOrWhiteSpace(dto.TaxLabel) ? "TAX" : dto.TaxLabel.Trim();
        s.TaxRatePercent = Math.Clamp(dto.TaxRatePercent, 0m, 100m);
        s.DefaultOpeningFloat = Math.Max(0m, dto.DefaultOpeningFloat);
        s.UsbRatePerGb = Math.Max(0m, dto.UsbRatePerGb);
        s.PrintCostPerPage = Math.Max(0m, dto.PrintCostPerPage);
        s.ClosingTime = string.IsNullOrWhiteSpace(dto.ClosingTime) ? "02:00" : dto.ClosingTime.Trim();
        s.LicenseKey = dto.LicenseKey?.Trim();
        s.AutoBackupPath = dto.AutoBackupPath?.Trim();
        s.AutoBackupIntervalHours = Math.Clamp(dto.AutoBackupIntervalHours, 1, 168);
        s.EnableHardwareAntiTheftWatchdog = dto.EnableHardwareAntiTheftWatchdog;
        s.EnforceNativeRefreshRate = dto.EnforceNativeRefreshRate;
        s.EnableRebootOnSessionEnd = dto.EnableRebootOnSessionEnd;
        s.DisklessProvider = string.IsNullOrWhiteSpace(dto.DisklessProvider) ? "None" : dto.DisklessProvider.Trim();
        s.OfflineGracePeriodSeconds = Math.Clamp(dto.OfflineGracePeriodSeconds, 10, 3600);
        s.IsConfigured = true;

        await db.SaveChangesAsync();
        _cachedSettings = s;

        return new ResultResponse(true, null);
    }

    public string FormatMoney(decimal amount)
    {
        var s = _cachedSettings;
        var symbol = s?.CurrencySymbol ?? "$";
        return $"{symbol}{amount:N2}";
    }
}
