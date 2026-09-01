using System.Windows;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App;

public partial class SetupWizardWindow : Window
{
    private readonly VenueSettingsService _settingsService;
    private readonly AuthAndCashierService _authService;

    public SetupWizardWindow(VenueSettingsService settingsService, AuthAndCashierService authService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _authService = authService;
    }

    private async void CompleteSetup_Click(object sender, RoutedEventArgs e)
    {
        var venueName = VenueNameBox.Text.Trim();
        var currencyCode = CurrencyCodeBox.Text.Trim().ToUpperInvariant();
        var currencySymbol = CurrencySymbolBox.Text.Trim();
        var locale = LocaleBox.Text.Trim();
        var adminUser = AdminUserBox.Text.Trim();
        var adminPin = AdminPinBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(venueName))
        {
            ShowError("Venue name is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(currencySymbol))
        {
            ShowError("Currency symbol is required.");
            return;
        }

        if (!decimal.TryParse(OpeningFloatBox.Text.Trim(), out var openingFloat) || openingFloat < 0)
        {
            ShowError("Please enter a valid non-negative opening float.");
            return;
        }

        if (!decimal.TryParse(TaxRateBox.Text.Trim(), out var taxRate) || taxRate < 0)
        {
            ShowError("Please enter a valid non-negative tax rate.");
            return;
        }

        if (string.IsNullOrWhiteSpace(adminUser))
        {
            ShowError("Admin username is required.");
            return;
        }

        if (adminPin.Length < 4)
        {
            ShowError("Admin PIN must be at least 4 digits.");
            return;
        }

        CompleteButton.IsEnabled = false;

        try
        {
            // Save venue settings
            var settings = new VenueSettingsDto(
                venueName,
                currencyCode,
                currencySymbol,
                locale,
                "Tax",
                taxRate,
                openingFloat,
                0.05m,
                0.15m,
                "00:00",
                null,
                "backups",
                24,
                null,
                true,
                true,
                true,
                false,
                "None",
                180
            );

            await _settingsService.SaveSettingsAsync(settings, adminUser);

            // Ensure Admin cashier exists
            var cashiers = await _authService.GetCashiersAsync();
            var existingAdmin = cashiers.FirstOrDefault(c => c.Name.Equals(adminUser, StringComparison.OrdinalIgnoreCase));
            if (existingAdmin is null)
            {
                await _authService.CreateCashierAsync(new CreateCashierRequest(
                    adminUser,
                    adminPin,
                    "Admin"
                ), adminUser);
            }
            else
            {
                await _authService.UpdateCashierAsync(new UpdateCashierRequest(
                    existingAdmin.Id,
                    existingAdmin.Name,
                    adminPin,
                    "Admin",
                    true
                ), adminUser);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Setup failed: {ex.Message}");
            CompleteButton.IsEnabled = true;
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
