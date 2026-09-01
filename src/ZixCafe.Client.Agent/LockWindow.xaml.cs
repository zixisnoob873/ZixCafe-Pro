using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ZixCafe.Client.Agent;

public sealed record ChatLine(string From, string Message);

public partial class LockWindow : Window
{
    private static readonly TimeSpan CallStaffCooldown = TimeSpan.FromSeconds(10);

    private readonly ObservableCollection<ChatLine> _chat = [];
    private readonly Func<string, Task> _sendChat;
    private DateTime _lastCallStaffAt = DateTime.MinValue;

    private readonly DispatcherTimer _countdown = new(DispatcherPriority.Normal)
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private DateTime? _plannedEnd;
    private bool _paused;

    public LockWindow(string terminalName, Func<string, Task> sendChat)
    {
        InitializeComponent();
        TerminalNameText.Text = terminalName.ToUpperInvariant();
        _sendChat = sendChat;
        ChatLog.ItemsSource = _chat;
        _countdown.Tick += (_, _) => RenderCountdown();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE);
        KioskGuard.Install();
    }

    public void BeginSession(int? minutesGranted, DateTime? plannedEndUtc)
    {
        _plannedEnd = plannedEndUtc
            ?? (minutesGranted is null ? null : DateTime.UtcNow.AddMinutes(minutesGranted.Value));
        CountdownText.Text = minutesGranted is null ? "RUNNING" : CountdownText.Text;
        ChargeText.Visibility = Visibility.Collapsed;
        ChargeText.Text = string.Empty;
        StatusText.Text = minutesGranted is null
            ? "Session running. Time is unlimited — see the desk to settle."
            : "Session running. Your time is below.";
        _countdown.Start();
        RenderCountdown();
    }

    /// <summary>
    /// Server-authoritative correction pushed at 1 Hz while a session is live.
    /// plannedEndUtc is absolute, so local clock drift self-corrects.
    /// </summary>
    public void TimeSync(DateTime? plannedEndUtc, decimal currentAmount)
    {
        if (_paused)
        {
            return;
        }
        if (plannedEndUtc is { } end)
        {
            _plannedEnd = end;
        }
        if (currentAmount > 0)
        {
            ChargeText.Text = $"CURRENT CHARGE  {currentAmount:0.00}";
            ChargeText.Visibility = Visibility.Visible;
        }
    }

    public void Pause()
    {
        _paused = true;
        _countdown.Stop();
        _plannedEnd = null;
        CountdownText.Text = "PAUSED";
        ChargeText.Visibility = Visibility.Collapsed;
        StatusText.Text = "Paused by the front desk. Your remaining time is held — ask staff to resume.";
    }

    public void Resume(DateTime? plannedEndUtc)
    {
        _paused = false;
        _plannedEnd = plannedEndUtc;
        StatusText.Text = "Session running. Your time is below.";
        _countdown.Start();
        RenderCountdown();
    }

    public void EndSession(string reason)
    {
        _countdown.Stop();
        _plannedEnd = null;
        _paused = false;
        CountdownText.Text = "00:00:00";
        ChargeText.Text = string.Empty;
        ChargeText.Visibility = Visibility.Collapsed;
        StatusText.Text = string.IsNullOrEmpty(reason)
            ? "Session ended. Ask the front desk to start a new one."
            : reason;
    }

    public void ShowBanner(string severity, string message)
    {
        Banner.Visibility = Visibility.Visible;
        BannerText.Text = message;
    }

    public void ShowChat(string fromName, string message)
    {
        AppendChat(fromName, message);
    }

    private void AppendChat(string fromName, string message)
    {
        _chat.Add(new ChatLine(fromName, message));
        while (_chat.Count > 8)
        {
            _chat.RemoveAt(0);
        }
        ChatDrawer.Visibility = Visibility.Visible;
        ChatInput.Focus();
    }

    private async void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        var message = ChatInput.Text.Trim();
        if (message.Length == 0)
        {
            return;
        }
        ChatInput.Clear();
        AppendChat("You", message);
        await _sendChat(message);
    }

    private async void CallStaff_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastCallStaffAt < CallStaffCooldown)
        {
            ShowBanner("info", "The desk has already been called. A staff member is on the way.");
            return;
        }
        _lastCallStaffAt = now;
        AppendChat("You", "Calling for staff assistance.");
        await _sendChat("Calling for staff assistance.");
        ShowBanner("info", "The desk has been notified. A staff member is on the way.");
    }

    private void RenderCountdown()
    {
        if (_plannedEnd is not { } end)
        {
            return;
        }
        var left = end - DateTime.UtcNow;
        CountdownText.Text = left > TimeSpan.Zero
            ? $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}"
            : "00:00:00";
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
