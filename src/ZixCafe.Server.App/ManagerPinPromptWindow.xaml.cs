using System.Windows;
using System.Windows.Input;
using ZixCafe.Server.App.Services;

namespace ZixCafe.Server.App;

public partial class ManagerPinPromptWindow : Window
{
    private readonly AuthAndCashierService _authService;

    public string? EnteredPin { get; private set; }

    public ManagerPinPromptWindow(AuthAndCashierService authService, string? reason = null)
    {
        InitializeComponent();
        _authService = authService;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            ReasonText.Text = reason;
        }
        Loaded += (_, _) => PinBox.Focus();
    }

    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        await ValidateAndCloseAsync();
    }

    private async void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await ValidateAndCloseAsync();
        }
    }

    private async Task ValidateAndCloseAsync()
    {
        var pin = PinBox.Password.Trim();
        if (pin.Length == 0)
        {
            ShowError("Please enter a PIN.");
            return;
        }

        var valid = await _authService.VerifyManagerPinAsync(pin);
        if (!valid)
        {
            ShowError("Invalid Manager/Admin PIN.");
            PinBox.SelectAll();
            return;
        }

        EnteredPin = pin;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
