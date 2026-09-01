using ZixCafe.Domain.Entities;

namespace ZixCafe.Server.App.Services;

public class LicenseService
{
    private readonly VenueSettingsService _venueSettings;

    public LicenseService(VenueSettingsService venueSettings)
    {
        _venueSettings = venueSettings;
    }

    public async Task<(bool IsValid, string StatusText)> GetLicenseStatusAsync()
    {
        var settings = await _venueSettings.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.LicenseKey))
        {
            return (false, "Unlicensed (Community Mode) — full operational features enabled.");
        }

        var key = settings.LicenseKey.Trim().ToUpperInvariant();
        if (IsValidKey(key))
        {
            return (true, "Licensed Pro Edition (Offline Validated)");
        }

        return (false, "Invalid License Key — Running in Grace Mode.");
    }

    public static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split('-');
        if (parts.Length != 4) return false;
        if (parts[0] != "ZIX") return false;
        return parts.All(p => p.Length >= 3 && p.All(char.IsLetterOrDigit));
    }
}
