using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ZixCafe.Server.App.Services;

namespace ZixCafe.Server.App;

public partial class RemoteScreenViewerWindow : Window
{
    private readonly RemoteOpsService _remoteOps;
    private readonly Guid _terminalId;
    private readonly string _terminalName;
    private readonly string _cashierName;

    public RemoteScreenViewerWindow(RemoteOpsService remoteOps, Guid terminalId, string terminalName, string cashierName)
    {
        InitializeComponent();
        _remoteOps = remoteOps;
        _terminalId = terminalId;
        _terminalName = terminalName;
        _cashierName = cashierName;

        Title = $"Live Screen View — {terminalName}";
        TitleText.Text = $"SCREEN VIEW: {terminalName.ToUpperInvariant()}";

        Loaded += async (_, _) =>
        {
            _remoteOps.FrameRelayed += OnFrameRelayed;
            await RequestFrameAsync();
        };

        Closed += (_, _) =>
        {
            _remoteOps.FrameRelayed -= OnFrameRelayed;
        };
    }

    private void OnFrameRelayed(Guid terminalId, byte[] jpegBytes)
    {
        if (terminalId != _terminalId || jpegBytes.Length == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new MemoryStream(jpegBytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();

                ScreenImage.Source = image;
                StatusText.Visibility = Visibility.Collapsed;
                FooterText.Text = $"Last frame captured at {DateTime.Now:HH:mm:ss} (Announced)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error rendering frame: {ex.Message}";
            }
        });
    }

    private async Task RequestFrameAsync()
    {
        StatusText.Text = "Requesting announced frame from client...";
        StatusText.Visibility = Visibility.Visible;

        var result = await _remoteOps.RequestScreenViewAsync(_terminalId, _cashierName);
        if (!result.Ok)
        {
            StatusText.Text = result.Error ?? "Failed to request screen frame from terminal.";
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RequestFrameAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
