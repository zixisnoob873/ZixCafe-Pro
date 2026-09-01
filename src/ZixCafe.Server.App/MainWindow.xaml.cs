using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Rack;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App;

public partial class MainWindow : Window
{
    public sealed record ChatLine(string From, string Message);

    private const string DashboardUrl = "http://localhost:40000/hubs/dashboard";

    private readonly ObservableCollection<TileViewModel> _tiles = [];
    private readonly Dictionary<Guid, ObservableCollection<ChatLine>> _chatLogs = [];
    private HubConnection? _dashboard;
    private readonly DispatcherTimer _uiClock = new(DispatcherPriority.Normal)
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private TileViewModel? _selected;
    private readonly string _cashierName;

    public MainWindow(Domain.Entities.Cashier cashier)
    {
        _cashierName = cashier.Name;
        InitializeComponent();
        CashierText.Text = $"{cashier.Name.ToUpperInvariant()} · {cashier.Role.ToString().ToUpperInvariant()}";
        RackItems.ItemsSource = _tiles;
        Loaded += OnLoaded;
        _uiClock.Tick += (_, _) =>
        {
            foreach (var tile in _tiles)
            {
                tile.RefreshTime();
            }
        };
        _uiClock.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await using var db = App.Services.GetRequiredService<ZixCafeDbContext>();
            var terminals = await db.Terminals
                .Include(t => t.Zone)
                .OrderBy(t => t.Zone.DisplayOrder).ThenBy(t => t.Name)
                .AsNoTracking()
                .ToListAsync();

            foreach (var t in terminals)
            {
                var vm = new TileViewModel
                {
                    TerminalId = t.Id,
                    Name = t.Name,
                    ZoneName = t.Zone.Name
                };
                vm.Apply(new TerminalStateDto(
                    t.Id, t.Name, t.Zone.Name,
                    (TerminalStatusDto)t.Status, t.IsLocked, t.AgentVersion,
                    t.LastSeenAt, null, 0, 0, null, null, false));
                _tiles.Add(vm);
            }

            _dashboard = new HubConnectionBuilder()
                .WithUrl(DashboardUrl)
                .WithAutomaticReconnect()
                .Build();

            _dashboard.On<TerminalStateDto>("TerminalStateChanged", state =>
                Dispatcher.BeginInvoke(() =>
                {
                    var tile = _tiles.FirstOrDefault(x => x.TerminalId == state.TerminalId);
                    if (tile is null)
                    {
                        return;
                    }
                    tile.Apply(state);
                    if (tile == _selected)
                    {
                        RenderInspector();
                    }
                }));

            _dashboard.On<Guid, string, string, DateTime>("ChatMessage", (terminalId, from, message, _) =>
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_chatLogs.TryGetValue(terminalId, out var log))
                    {
                        log = [];
                        _chatLogs[terminalId] = log;
                    }
                    log.Add(new ChatLine(from, message));
                    while (log.Count > 50)
                    {
                        log.RemoveAt(0);
                    }
                }));

            _dashboard.On<string, string, string, Guid?, DateTime>("AlertRaised",
                (severity, kind, message, _, _) =>
                Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(this, message, $"Alert · {kind} ({severity})",
                        MessageBoxButton.OK, severity == "Critical"
                            ? MessageBoxImage.Error : MessageBoxImage.Warning);
                }));

            _dashboard.On<IReadOnlyList<WaitlistEntryDto>>("WaitlistChanged", waiting =>
                Dispatcher.BeginInvoke(() => WaitlistItems.ItemsSource = waiting));

            await _dashboard.StartAsync();
            await _dashboard.InvokeAsync(nameof(IDashboardServer.SubscribeAsync));
            HealthText.Text = "ONLINE";
            HealthText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "RunBrush");

            var products = await _dashboard.InvokeAsync<IReadOnlyList<ProductDto>>(
                nameof(IDashboardServer.GetProductsAsync));
            ProductPicker.ItemsSource = products;
        }
        catch (Exception ex)
        {
            HealthText.Text = "ERROR";
            MessageBox.Show(this, ex.Message, "ZixCafe Server", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not TileViewModel tile)
        {
            return;
        }
        foreach (var t in _tiles)
        {
            t.IsSelected = false;
        }
        tile.IsSelected = true;
        _selected = tile;
        if (!_chatLogs.TryGetValue(tile.TerminalId, out var log))
        {
            log = [];
            _chatLogs[tile.TerminalId] = log;
        }
        ChatItems.ItemsSource = log;
        RenderInspector();
    }

    private void RenderInspector()
    {
        if (_selected is null)
        {
            InspectorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        InspectorPanel.Visibility = Visibility.Visible;
        InspectorName.Text = _selected.Name;
        InspectorZone.Text = _selected.ZoneName.ToUpperInvariant();
        InspectorStatus.Text = _selected.StatusText;
        InspectorSession.Text = _selected.IsRunning
            ? $"Session {_selected.ActiveSessionId?.ToString()[..8]} · {_selected.TimeLabel.ToLowerInvariant()} {_selected.TimeText}"
            : "No active session.";

        var running = _selected.IsRunning;
        EndSessionButton.IsEnabled = running;
        PauseResumeButton.IsEnabled = running;
        PauseResumeButton.Content = _selected.IsPaused ? "Resume" : "Pause";
        ProductPicker.IsEnabled = running;
    }

    private async void StartPostpaid_Click(object sender, RoutedEventArgs e)
        => await StartSessionAsync("postpaid", null);

    private async void StartPrepaid30_Click(object sender, RoutedEventArgs e)
        => await StartSessionAsync("prepaid", 30);

    private async void StartPrepaid60_Click(object sender, RoutedEventArgs e)
        => await StartSessionAsync("prepaid", 60);

    private async void StartPrepaid120_Click(object sender, RoutedEventArgs e)
        => await StartSessionAsync("prepaid", 120);

    private async void RedeemCode_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var code = PromptTicketCode();
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var response = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.StartSessionAsync),
            new StartSessionRequest(_selected.TerminalId, "ticket", null, code, null, _cashierName));
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Redeem ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(
            this,
            response.DepositDue is { } credit
                ? $"Credit ticket applied. {credit:F2} credit loaded on this session."
                : $"Ticket accepted. {(response.MinutesGranted ?? 0)} minutes granted.",
            "Redeem ticket", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string? PromptTicketCode()
    {
        var dialog = new Window
        {
            Title = "Redeem ticket",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (System.Windows.Media.Brush)FindResource("VoidBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("BodyFont")
        };
        var input = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DataFont"),
            FontSize = 16
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = "Redeem",
            IsDefault = true,
            Style = (Style)FindResource("GoldButton"),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6)
        };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            IsCancel = true,
            Style = (Style)FindResource("GhostButton"),
            Margin = new Thickness(8, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6)
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Enter or scan the ticket code:",
            Foreground = (System.Windows.Media.Brush)FindResource("InkBrush")
        });
        panel.Children.Add(input);
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }
        if (_selected.IsPaused)
        {
            await _dashboard!.InvokeAsync<ResultResponse>(
                nameof(IDashboardServer.ResumeSessionAsync), _selected.TerminalId, _cashierName);
        }
        else
        {
            await _dashboard!.InvokeAsync<ResultResponse>(
                nameof(IDashboardServer.PauseSessionAsync), _selected.TerminalId, _cashierName);
        }
    }

    private async void MemberStart_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }
        var query = MemberQueryInput.Text.Trim();
        if (query.Length == 0)
        {
            MessageBox.Show(this, "Enter a member code or name first.", "Member session",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var find = await _dashboard!.InvokeAsync<FindMemberResponse>(
            nameof(IDashboardServer.FindMemberAsync), query);
        if (!find.Ok || find.Member is null)
        {
            MessageBox.Show(this, find.Error ?? "Member not found.", "Member session",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"{find.Member.Name} · time balance {find.Member.TimeBalanceMinutes} min · cash balance {find.Member.MoneyBalance:F2}\n\nStart a member session on {_selected.Name}?",
            "Member session", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var response = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.StartSessionAsync),
            new StartSessionRequest(_selected.TerminalId, "member", find.Member.Id, null, null, _cashierName));
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Member session", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MemberQueryInput.Clear();
    }

    private async void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.ActiveSessionId is not { } sessionId)
        {
            return;
        }
        if (ProductPicker.SelectedValue is not { } productId)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<AddLineResponse>(
            nameof(IDashboardServer.AddProductLineAsync),
            sessionId, productId, 1, _cashierName);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Sell item", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NavRack_Checked(object sender, RoutedEventArgs e)
    {
        if (RackView is not null)
        {
            RackView.Visibility = Visibility.Visible;
        }
        if (DeskView is not null)
        {
            DeskView.Visibility = Visibility.Collapsed;
        }
    }

    private async void NavDesk_Checked(object sender, RoutedEventArgs e)
    {
        if (RackView is not null)
        {
            RackView.Visibility = Visibility.Collapsed;
        }
        if (DeskView is not null)
        {
            DeskView.Visibility = Visibility.Visible;
            await RefreshDeskAsync();
        }
    }

    private async Task RefreshDeskAsync()
    {
        var shift = await _dashboard!.InvokeAsync<ShiftDto?>(nameof(IDashboardServer.GetCurrentShiftAsync));
        RenderShift(shift);

        var waiting = await _dashboard!.InvokeAsync<IReadOnlyList<WaitlistEntryDto>>(
            nameof(IDashboardServer.GetWaitlistAsync));
        WaitlistItems.ItemsSource = waiting;

        var loans = await _dashboard!.InvokeAsync<IReadOnlyList<LoanDto>>(nameof(IDashboardServer.GetLoansAsync));
        LoanItems.ItemsSource = loans;
    }

    private void RenderShift(ShiftDto? shift)
    {
        if (shift is null)
        {
            ShiftStatusText.Text = "NO SHIFT OPEN";
            OpenShiftButton.IsEnabled = true;
            CloseShiftButton.IsEnabled = false;
            return;
        }
        ShiftStatusText.Text = shift.IsOpen
            ? $"OPEN · {shift.CashierName.ToUpperInvariant()} · FLOAT {shift.OpeningFloat:F2} · SINCE {shift.StartedAt:HH:mm}"
            : $"LAST · {shift.CashierName.ToUpperInvariant()} · VARIANCE {(shift.Variance ?? 0):F2}";
        OpenShiftButton.IsEnabled = false;
        CloseShiftButton.IsEnabled = shift.IsOpen;
    }

    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptNumber("Open shift", "Opening float in the drawer:", 0m);
        if (input is null)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<ShiftResponse>(
            nameof(IDashboardServer.OpenShiftAsync), _cashierName, input.Value);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Open shift", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await RefreshDeskAsync();
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        var counted = PromptNumber("Close shift", "Counted drawer total:", 0m);
        if (counted is null)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<ShiftResponse>(
            nameof(IDashboardServer.CloseShiftAsync), _cashierName, counted.Value, null);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Close shift", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (response.Shift is { Variance: { } v })
        {
            MessageBox.Show(
                this,
                $"Shift closed. Expected {response.Shift.ExpectedDrawer:F2}, counted {response.Shift.CountedDrawer:F2}.\nVariance {(v >= 0 ? "+" : "")}{v:F2}",
                "Shift closed", MessageBoxButton.OK,
                v == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        await RefreshDeskAsync();
    }

    private decimal? PromptNumber(string title, string label, decimal fallback)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (System.Windows.Media.Brush)FindResource("VoidBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("BodyFont")
        };
        var input = new System.Windows.Controls.TextBox
        {
            Text = fallback.ToString("0.##"),
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DataFont"),
            FontSize = 16
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = "Confirm",
            IsDefault = true,
            Style = (Style)FindResource("GoldButton"),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6)
        };
        var result = (decimal?)null;
        ok.Click += (_, _) =>
        {
            if (decimal.TryParse(input.Text.Trim(), out var value))
            {
                result = value;
                DialogResult = true;
            }
        };
        var cancel = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            IsCancel = true,
            Style = (Style)FindResource("GhostButton"),
            Margin = new Thickness(8, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6)
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label,
            Foreground = (System.Windows.Media.Brush)FindResource("InkBrush")
        });
        panel.Children.Add(input);
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        input.Focus();
        input.SelectAll();
        return dialog.ShowDialog() == true ? result : null;
    }

    private async void AddWaitGuest_Click(object sender, RoutedEventArgs e)
    {
        var name = WaitNameInput.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Enter the guest's name.", "Waitlist", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var size = int.TryParse(WaitPartyInput.Text.Trim(), out var s) ? s : 1;
        var response = await _dashboard!.InvokeAsync<WaitlistResponse>(
            nameof(IDashboardServer.AddToWaitlistAsync), name, size,
            string.IsNullOrWhiteSpace(WaitContactInput.Text) ? null : WaitContactInput.Text.Trim());
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Waitlist", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        WaitNameInput.Clear();
        WaitContactInput.Clear();
    }

    private async void SeatGuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not WaitlistEntryDto entry)
        {
            return;
        }
        if (_selected is null)
        {
            MessageBox.Show(this, "Select an Available terminal on the Rack first, then seat the guest.",
                "Seat guest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var response = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.SeatWaitlistGuestAsync), entry.Id, _selected.TerminalId, _cashierName);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Seat guest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SkipGuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not WaitlistEntryDto entry)
        {
            return;
        }
        await _dashboard!.InvokeAsync<WaitlistResponse>(
            nameof(IDashboardServer.SkipWaitlistEntryAsync), entry.Id, _cashierName);
    }

    private async void LoanItem_Click(object sender, RoutedEventArgs e)
    {
        var item = LoanItemInput.Text.Trim();
        if (item.Length == 0)
        {
            MessageBox.Show(this, "Enter the item being loaned (e.g. Headset #3).", "Loan out",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var deposit = decimal.TryParse(LoanDepositInput.Text.Trim(), out var d) ? d : 0m;
        var response = await _dashboard!.InvokeAsync<LoanResponse>(
            nameof(IDashboardServer.LoanItemAsync), item, deposit, _cashierName, null);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Loan out", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LoanItemInput.Clear();
        LoanDepositInput.Text = "0";
        await RefreshDeskAsync();
    }

    private async void ReturnLoan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not LoanDto loan)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<LoanResponse>(
            nameof(IDashboardServer.ReturnLoanAsync), loan.Id, _cashierName, _cashierName, false);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Return loan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await RefreshDeskAsync();
    }

    private async void ForfeitLoan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not LoanDto loan)
        {
            return;
        }
        var confirm = MessageBox.Show(
            this, $"Mark '{loan.ItemName}' as forfeited? Deposit {loan.DepositAmount:F2} stays with the house.",
            "Forfeit loan", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<LoanResponse>(
            nameof(IDashboardServer.ReturnLoanAsync), loan.Id, _cashierName, _cashierName, true);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Forfeit loan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await RefreshDeskAsync();
    }

    private async void LockAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this, "Send the lock screen to every idle terminal? Machines running sessions are skipped.",
            "Lock all", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }
        await _dashboard!.InvokeAsync(nameof(IDashboardServer.LockAllTerminalsAsync), _cashierName);
    }

    private async Task StartSessionAsync(string mode, int? minutes)
    {
        if (_selected is null)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.StartSessionAsync),
            new StartSessionRequest(_selected.TerminalId, mode, null, null, minutes, _cashierName));
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Start session", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.ActiveSessionId is not { } sessionId)
        {
            return;
        }
        var confirm = MessageBox.Show(
            this, $"End the session on {_selected.Name} and charge the running total?",
            "End session", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<EndSessionResponse>(
            nameof(IDashboardServer.EndSessionAsync),
            new EndSessionRequest(sessionId, _cashierName));
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "End session", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(
            this,
            $"Time {response.TimeCharge:F2} + extras {response.ExtrasTotal:F2} = {response.TotalDue:F2} due.",
            "Session closed", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void LockTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }
        await _dashboard!.InvokeAsync(nameof(IDashboardServer.LockTerminalAsync), _selected.TerminalId);
    }

    private async void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || ChatInput.Text.Trim().Length == 0)
        {
            return;
        }
        var message = ChatInput.Text.Trim();
        ChatInput.Clear();
        await _dashboard!.InvokeAsync(
            nameof(IDashboardServer.SendChatToTerminalAsync), _selected.TerminalId, message);
    }

    private async void PairTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show(this, "Select a terminal tile first, then issue its pairing code.",
                "Pair terminal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var code = await _dashboard!.InvokeAsync<string>(
            nameof(IDashboardServer.IssuePairingCodeAsync), _selected.TerminalId);
        MessageBox.Show(
            this,
            $"Pairing code for {_selected.Name}:\n\n{code}\n\nValid for 10 minutes, single use. Enter it on the terminal's pair screen.",
            "Pair terminal", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
