using System.Windows;
using System.Windows.Input;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ZixCafe.Server.App;

/// <summary>
/// Blocks the dashboard until a cashier signs in with name + PIN.
/// Returns the authenticated Cashier via AuthenticatedCashier.
/// </summary>
public partial class LoginWindow : Window
{
    public Cashier? AuthenticatedCashier { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        NameInput.Focus();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        var pin = PinInput.Password;

        if (name.Length == 0 || pin.Length == 0)
        {
            ShowError("Enter your name and PIN.");
            return;
        }

        SignInButton.IsEnabled = false;
        try
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var cashier = await db.Cashiers.FirstOrDefaultAsync(c => c.Name == name && c.IsActive);
            if (cashier is null || !SecretHasher.Verify(pin, cashier.PinHash))
            {
                ShowError("Unknown cashier or wrong PIN.");
                return;
            }

            AuthenticatedCashier = cashier;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError($"Sign-in failed: {ex.Message}");
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void PinInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SignIn_Click(sender, e);
        }
    }
}
