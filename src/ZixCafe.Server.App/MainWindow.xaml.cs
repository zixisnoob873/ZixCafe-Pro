using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Rack;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App;

public partial class MainWindow : Window
{
    public sealed record ChatLine(string From, string Message);

    public class CartItemViewModel
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal => UnitPrice * Quantity;
    }

    private const string DashboardUrl = "http://localhost:40000/hubs/dashboard";

    private readonly ObservableCollection<TileViewModel> _tiles = [];
    private readonly ObservableCollection<TileViewModel> _filteredTiles = [];
    private readonly Dictionary<Guid, ObservableCollection<ChatLine>> _chatLogs = [];
    private readonly ObservableCollection<CartItemViewModel> _posCart = [];
    private readonly ObservableCollection<AlertDto> _alerts = [];

    private HubConnection? _dashboard;
    private readonly DispatcherTimer _uiClock = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
    private TileViewModel? _selected;
    private readonly string _cashierName;
    private readonly string _cashierRole;

    private IReadOnlyList<ProductDto> _sessionProducts = [];
    private IReadOnlyList<ProductDetailDto> _allProducts = [];
    private IReadOnlyList<MemberDetailDto> _allMembers = [];
    private IReadOnlyList<TicketDto> _allTickets = [];
    private IReadOnlyList<CashierDto> _allCashiers = [];
    private IReadOnlyList<TariffDto> _allTariffs = [];

    public MainWindow(Domain.Entities.Cashier cashier)
    {
        _cashierName = cashier.Name;
        _cashierRole = cashier.Role.ToString();

        InitializeComponent();

        CashierText.Text = $"CASHIER: {cashier.Name.ToUpperInvariant()} · {cashier.Role.ToString().ToUpperInvariant()}";
        RackItems.ItemsSource = _filteredTiles;
        PosCartGrid.ItemsSource = _posCart;
        AlertsGrid.ItemsSource = _alerts;

        ReportTypePicker.ItemsSource = new[] { "Session History", "Audit Trail (SHA-256)" };
        ReportTypePicker.SelectedIndex = 0;

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
            await CheckFirstRunSetupAsync();
            await LoadTerminalsAsync();

            _dashboard = new HubConnectionBuilder()
                .WithUrl(DashboardUrl)
                .WithAutomaticReconnect()
                .Build();

            _dashboard.On<TerminalStateDto>("TerminalStateChanged", state =>
                Dispatcher.BeginInvoke(() =>
                {
                    var tile = _tiles.FirstOrDefault(x => x.TerminalId == state.TerminalId);
                    if (tile is not null)
                    {
                        tile.Apply(state);
                        if (tile == _selected)
                        {
                            RenderInspector();
                        }
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
                (severity, kind, message, terminalId, time) =>
                Dispatcher.BeginInvoke(() =>
                {
                    var termName = terminalId.HasValue ? _tiles.FirstOrDefault(t => t.TerminalId == terminalId.Value)?.Name : null;
                    _alerts.Insert(0, new AlertDto(Guid.NewGuid(), severity, kind, message, terminalId, termName, time, false, null, null));
                    while (_alerts.Count > 100) _alerts.RemoveAt(_alerts.Count - 1);
                }));

            _dashboard.On<IReadOnlyList<WaitlistEntryDto>>("WaitlistChanged", waiting =>
                Dispatcher.BeginInvoke(() => WaitlistItems.ItemsSource = waiting));

            await _dashboard.StartAsync();
            await _dashboard.InvokeAsync(nameof(IDashboardServer.SubscribeAsync));

            HealthText.Text = "SERVER · PORT 40000 · ONLINE";

            await RefreshProductsAsync();
            await RefreshSettingsViewAsync();
        }
        catch (Exception ex)
        {
            HealthText.Text = "SERVER · ERROR";
            MessageBox.Show(this, ex.Message, "ZixCafe Server Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CheckFirstRunSetupAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var venueSvc = scope.ServiceProvider.GetRequiredService<VenueSettingsService>();
        var authSvc = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();

        var settings = await venueSvc.GetSettingsAsync();
        if (!settings.IsConfigured)
        {
            var wizard = new SetupWizardWindow(venueSvc, authSvc);
            wizard.ShowDialog();
        }
    }

    private async Task LoadTerminalsAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var terminals = await db.Terminals
            .Include(t => t.Zone)
            .OrderBy(t => t.Zone.DisplayOrder).ThenBy(t => t.Name)
            .AsNoTracking()
            .ToListAsync();

        _tiles.Clear();
        _filteredTiles.Clear();

        var zones = new HashSet<string> { "All Zones" };

        foreach (var t in terminals)
        {
            var vm = new TileViewModel
            {
                TerminalId = t.Id,
                Name = t.Name,
                ZoneName = t.Zone?.Name ?? "Main"
            };
            vm.Apply(new TerminalStateDto(
                t.Id, t.Name, t.Zone?.Name ?? "Main",
                (TerminalStatusDto)t.Status, t.IsLocked, t.AgentVersion,
                t.LastSeenAt, null, 0, 0, null, null, false,
                t.MaintenanceReason, t.ReservedFor,
                t.CpuTemp, t.GpuTemp, t.RamPercent, t.DiskFreeGb));

            _tiles.Add(vm);
            _filteredTiles.Add(vm);
            zones.Add(vm.ZoneName);
        }

        RackZoneFilter.ItemsSource = zones.ToList();
        RackZoneFilter.SelectedIndex = 0;
    }

    // ==========================================
    // KEYBOARD ACCELERATORS (F1-F8, Esc, Ctrl+F)
    // ==========================================
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            NavRack.IsChecked = true;
        }
        else if (e.Key == Key.F2)
        {
            if (_selected is not null && !_selected.IsRunning)
            {
                StartPostpaid_Click(this, new RoutedEventArgs());
            }
        }
        else if (e.Key == Key.F3)
        {
            if (_selected is not null && _selected.IsRunning)
            {
                PauseResume_Click(this, new RoutedEventArgs());
            }
        }
        else if (e.Key == Key.F4)
        {
            if (_selected is not null && _selected.IsRunning)
            {
                EndSession_Click(this, new RoutedEventArgs());
            }
        }
        else if (e.Key == Key.F5)
        {
            NavSales.IsChecked = true;
        }
        else if (e.Key == Key.F6)
        {
            ProductPicker.Focus();
        }
        else if (e.Key == Key.F8)
        {
            ChatInput.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            InspectorPanel.Visibility = Visibility.Collapsed;
        }
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            RackSearchBox.Focus();
            RackSearchBox.SelectAll();
        }
    }

    // ==========================================
    // 1. RACK & INSPECTOR PANEL
    // ==========================================
    private void NavRack_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (RackView is not null) RackView.Visibility = Visibility.Visible;
    }

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TileViewModel tile)
        {
            return;
        }
        foreach (var t in _tiles) t.IsSelected = false;
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
        InspectorName.Text = _selected.Name.ToUpperInvariant();
        InspectorZone.Text = $"ZONE: {_selected.ZoneName.ToUpperInvariant()}";
        InspectorStatus.Text = _selected.StatusText;
        InspectorSession.Text = _selected.IsRunning
            ? $"Session {_selected.ActiveSessionId?.ToString()[..8]} · {_selected.TimeLabel} {_selected.TimeText}"
            : "No active session.";

        InspectorTelemetry.Text = $"CPU: {_selected.CpuTemp ?? 15}% · RAM: {_selected.RamPercent ?? 35}% · DISK: {_selected.DiskFreeGb ?? 100}GB";

        var running = _selected.IsRunning;
        EndSessionButton.IsEnabled = running;
        PauseResumeButton.IsEnabled = running;
        PauseResumeButton.Content = _selected.IsPaused ? "Resume (F3)" : "Pause (F3)";
        ProductPicker.IsEnabled = running;

        _ = RefreshInspectorHardwareAsync(_selected.TerminalId);
    }

    private async Task RefreshInspectorHardwareAsync(Guid terminalId)
    {
        if (_dashboard is null) return;
        try
        {
            var hw = await _dashboard.InvokeAsync<HardwareBaselineDto?>(nameof(IDashboardServer.GetTerminalHardwareAsync), terminalId);
            if (hw is not null)
            {
                InspectorHwCpuGpu.Text = $"CPU: {hw.CpuName}\nGPU: {hw.GpuName}";
                InspectorHwRamDisk.Text = $"RAM: {hw.TotalRamMb / 1024} GB · Disk: {hw.DiskModel ?? "System NVMe"}";
                InspectorHwDisplay.Text = $"Display: {hw.DisplayResolution ?? "1920x1080"} @ {hw.NativeRefreshRateHz ?? 240}Hz (Native)";
                InspectorHwPeripherals.Text = $"Peripherals: {hw.UsbDevices.Count} USB devices attached";
            }
            else
            {
                InspectorHwCpuGpu.Text = "CPU/GPU: Waiting for agent report...";
                InspectorHwRamDisk.Text = "RAM/Disk: Waiting for agent report...";
                InspectorHwDisplay.Text = "Display: 1920x1080 @ 240Hz";
                InspectorHwPeripherals.Text = "Peripherals: 0 USB devices attached";
            }
        }
        catch
        {
        }
    }

    private async void SetHardwareBaseline_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SetTerminalHardwareBaselineAsync), _selected.TerminalId, _cashierName);
        if (res.Ok)
        {
            MessageBox.Show(this, $"Official hardware baseline established for {_selected.Name}.", "Hardware Watchdog", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshInspectorHardwareAsync(_selected.TerminalId);
        }
        else
        {
            MessageBox.Show(this, res.Error ?? "Failed to set baseline.", "Hardware Watchdog", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EnforceRefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.EnforceTerminalRefreshRateAsync), _selected.TerminalId, _cashierName);
        if (res.Ok)
        {
            MessageBox.Show(this, $"Native maximum refresh rate command sent to {_selected.Name}.", "Display Enforcer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void TriggerDisklessWipe_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (MessageBox.Show(this, $"Are you sure you want to trigger a diskless clean wipe and reboot on {_selected.Name}?", "Diskless Coordination", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.TriggerDisklessWipeAsync), _selected.TerminalId, _cashierName);
            if (res.Ok)
            {
                MessageBox.Show(this, $"Clean wipe and reboot triggered on {_selected.Name}.", "Diskless Coordination", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e)
    {
        InspectorPanel.Visibility = Visibility.Collapsed;
    }

    private void RackSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterRackTiles();
    private void RackZoneFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterRackTiles();

    private void FilterRackTiles()
    {
        var q = RackSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var zone = RackZoneFilter?.SelectedItem as string ?? "All Zones";

        _filteredTiles.Clear();
        foreach (var t in _tiles)
        {
            var matchZone = zone == "All Zones" || t.ZoneName.Equals(zone, StringComparison.OrdinalIgnoreCase);
            var matchQuery = string.IsNullOrEmpty(q) || t.Name.ToLowerInvariant().Contains(q) || t.ZoneName.ToLowerInvariant().Contains(q);
            if (matchZone && matchQuery)
            {
                _filteredTiles.Add(t);
            }
        }
    }

    private async void StartPostpaid_Click(object sender, RoutedEventArgs e) => await StartSessionAsync("postpaid", null);
    private async void StartPrepaid30_Click(object sender, RoutedEventArgs e) => await StartSessionAsync("prepaid", 30);
    private async void StartPrepaid60_Click(object sender, RoutedEventArgs e) => await StartSessionAsync("prepaid", 60);
    private async void StartPrepaid120_Click(object sender, RoutedEventArgs e) => await StartSessionAsync("prepaid", 120);

    private async Task StartSessionAsync(string mode, int? minutes)
    {
        if (_selected is null) return;
        var response = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.StartSessionAsync),
            new StartSessionRequest(_selected.TerminalId, mode, null, null, minutes, _cashierName));
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Start session", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RedeemCode_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var code = PromptString("Redeem voucher code", "Enter or scan ticket voucher code:");
        if (string.IsNullOrWhiteSpace(code)) return;

        var response = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.StartSessionAsync),
            new StartSessionRequest(_selected.TerminalId, "ticket", null, code, null, _cashierName));
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Redeem ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(this, $"Ticket voucher accepted! Session started on {_selected.Name}.", "Redeem ticket", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void MemberStart_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var query = MemberQueryInput.Text.Trim();
        if (query.Length == 0)
        {
            MessageBox.Show(this, "Enter member phone, code, or name.", "Member session", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var find = await _dashboard!.InvokeAsync<FindMemberResponse>(nameof(IDashboardServer.FindMemberAsync), query);
        if (!find.Ok || find.Member is null)
        {
            MessageBox.Show(this, find.Error ?? "Member not found.", "Member session", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Member: {find.Member.Name}\nCash Balance: {find.Member.MoneyBalance:C}\nTime Balance: {find.Member.TimeBalanceMinutes} min\n\nStart member session on {_selected.Name}?",
            "Start Member Session", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var res = await _dashboard!.InvokeAsync<StartSessionResponse>(
            nameof(IDashboardServer.StartSessionAsync),
            new StartSessionRequest(_selected.TerminalId, "member", find.Member.Id, null, null, _cashierName));

        if (!res.Ok) MessageBox.Show(this, res.Error, "Member session", MessageBoxButton.OK, MessageBoxImage.Warning);
        else MemberQueryInput.Clear();
    }

    private async void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (_selected.IsPaused)
            await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ResumeSessionAsync), _selected.TerminalId, _cashierName);
        else
            await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.PauseSessionAsync), _selected.TerminalId, _cashierName);
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.ActiveSessionId is not { } sessionId) return;

        var confirm = MessageBox.Show(this, $"Close session on {_selected.Name} and calculate final charge?", "End session", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var response = await _dashboard!.InvokeAsync<EndSessionResponse>(
            nameof(IDashboardServer.EndSessionAsync),
            new EndSessionRequest(sessionId, _cashierName));

        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "End session", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this,
            $"Session Closed for {_selected.Name}\n\nTime Charge: {response.TimeCharge:C}\nExtras / POS: {response.ExtrasTotal:C}\nTotal Due: {response.TotalDue:C}",
            "Session Summary", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void LockTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        await _dashboard!.InvokeAsync(nameof(IDashboardServer.LockTerminalAsync), _selected.TerminalId);
    }

    private async void LockAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "Lock all idle terminals? Terminals with running sessions will continue uninterrupted.", "Lock All", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            await _dashboard!.InvokeAsync(nameof(IDashboardServer.LockAllTerminalsAsync), _cashierName);
        }
    }

    private async void WakeAllWoL_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "Broadcast Wake-on-LAN magic packets to wake all terminals on the LAN?", "Wake All Terminals", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.WakeAllTerminalsAsync), (Guid?)null, _cashierName);
        MessageBox.Show(this, res.Ok ? res.Error ?? "WoL magic packets sent." : res.Error, "Wake-on-LAN", MessageBoxButton.OK, res.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void WakeWoL_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.WakeTerminalAsync), _selected.TerminalId, _cashierName);
        MessageBox.Show(this, res.Ok ? res.Error ?? $"WoL magic packet sent to {_selected.Name}." : res.Error, "Wake-on-LAN", MessageBoxButton.OK, res.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void TogglePowerRelay_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var confirm = MessageBox.Show(this, $"Toggle smart relay power for '{_selected.Name}'?\n\nYes = Turn ON\nNo = Turn OFF\nCancel = Abort", "Smart Relay Control", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Cancel) return;

        var powerOn = confirm == MessageBoxResult.Yes;
        var req = new SmartRelayTriggerRequest(_selected.TerminalId, powerOn, _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.TriggerSmartRelayAsync), req);
        MessageBox.Show(this, res.Ok ? res.Error ?? $"Power relay for {_selected.Name} set to {(powerOn ? "ON" : "OFF")}." : res.Error, "Smart Relay", MessageBoxButton.OK, res.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void SwitchStation_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.ActiveSessionId is null)
        {
            MessageBox.Show(this, "Select a terminal with an active running session to transfer.", "Switch Station", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var available = _tiles.Where(t => t.TerminalId != _selected.TerminalId && t.Status == TerminalStatusDto.Available).ToList();
        if (available.Count == 0)
        {
            MessageBox.Show(this, "No available destination stations found on the rack.", "Switch Station", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var stationNames = string.Join(", ", available.Select(a => a.Name));
        var targetInput = PromptString("Switch Station / Transfer", $"Transfer active session from '{_selected.Name}' to which station?\n\nAvailable stations: {stationNames}\n\nEnter destination station name:");
        if (string.IsNullOrWhiteSpace(targetInput)) return;

        var target = available.FirstOrDefault(a => a.Name.Equals(targetInput.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            MessageBox.Show(this, $"Station '{targetInput}' not found among available stations.", "Switch Station", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var reason = PromptString("Switch Station Reason", "Reason for switch (e.g. Guest requested VIP area):") ?? "Guest request";

        var req = new SwitchStationRequest(_selected.TerminalId, target.TerminalId, _cashierName, reason);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SwitchStationAsync), req);

        if (res.Ok)
        {
            MessageBox.Show(this, $"Session successfully transferred from {_selected.Name} to {target.Name}!\n\nAll session time, balance, and open charges have been seamlessly moved.", "Station Transferred", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, res.Error, "Switch Station Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewScreen_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        using var scope = App.Services.CreateScope();
        var remoteOps = scope.ServiceProvider.GetRequiredService<RemoteOpsService>();
        var viewer = new RemoteScreenViewerWindow(remoteOps, _selected.TerminalId, _selected.Name, _cashierName);
        viewer.Show();
    }

    private async void ToggleMaintenance_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var reason = PromptString("Maintenance Mode", "Reason for maintenance (e.g. GPU Driver repair):");
        if (reason is null) return;

        var req = new SetTerminalMaintenanceRequest(_selected.TerminalId, true, reason, _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SetTerminalMaintenanceAsync), req);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void ReserveTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var guest = PromptString("Reserve Terminal", "Guest name for reservation:");
        if (string.IsNullOrWhiteSpace(guest)) return;

        var req = new ReserveTerminalRequest(_selected.TerminalId, guest, DateTime.UtcNow.AddHours(2), _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ReserveTerminalAsync), req);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Reservation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void RebootTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var req = new RemoteActionRequest(_selected.TerminalId, "reboot", null, _cashierName);
        await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ExecuteRemoteActionAsync), req);
    }

    private async void ShutdownTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var req = new RemoteActionRequest(_selected.TerminalId, "shutdown", null, _cashierName);
        await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ExecuteRemoteActionAsync), req);
    }

    private async void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.ActiveSessionId is not { } sessionId || ProductPicker.SelectedValue is not Guid productId)
        {
            return;
        }
        var response = await _dashboard!.InvokeAsync<AddLineResponse>(
            nameof(IDashboardServer.AddProductLineAsync), sessionId, productId, 1, _cashierName);
        if (!response.Ok)
        {
            MessageBox.Show(this, response.Error, "Add Extra", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || string.IsNullOrWhiteSpace(ChatInput.Text)) return;
        var msg = ChatInput.Text.Trim();
        ChatInput.Clear();
        await _dashboard!.InvokeAsync(nameof(IDashboardServer.SendChatToTerminalAsync), _selected.TerminalId, msg);
    }

    private async void PairTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show(this, "Select a terminal tile first.", "Pair terminal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var code = await _dashboard!.InvokeAsync<string>(nameof(IDashboardServer.IssuePairingCodeAsync), _selected.TerminalId);
        MessageBox.Show(this, $"Single-use Pairing Code for {_selected.Name}:\n\n{code}\n\nEnter this code on the terminal screen to pair.", "Pairing Code", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenWebDashboard_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("http://localhost:40000/dashboard") { UseShellExecute = true });
    }

    // ==========================================
    // 2. DESK & SHIFTS
    // ==========================================
    private async void NavDesk_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (DeskView is not null) DeskView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshDeskAsync();
    }

    private async Task RefreshDeskAsync()
    {
        if (_dashboard is null) return;
        var shift = await _dashboard.InvokeAsync<ShiftDto?>(nameof(IDashboardServer.GetCurrentShiftAsync));
        if (shift is null)
        {
            ShiftStatusText.Text = "NO ACTIVE SHIFT";
            OpenShiftButton.IsEnabled = true;
            CloseShiftButton.IsEnabled = false;
            PrintXReportButton.IsEnabled = false;
        }
        else
        {
            ShiftStatusText.Text = shift.IsOpen
                ? $"SHIFT OPEN · CASHIER: {shift.CashierName.ToUpperInvariant()} · OPENING FLOAT: {shift.OpeningFloat:C} · SINCE {shift.StartedAt:HH:mm}"
                : $"LAST SHIFT CLOSED · VARIANCE: {shift.Variance ?? 0:C}";
            OpenShiftButton.IsEnabled = !shift.IsOpen;
            CloseShiftButton.IsEnabled = shift.IsOpen;
            PrintXReportButton.IsEnabled = shift.IsOpen;
        }

        var waiting = await _dashboard!.InvokeAsync<IReadOnlyList<WaitlistEntryDto>>(nameof(IDashboardServer.GetWaitlistAsync));
        WaitlistItems.ItemsSource = waiting;

        var loans = await _dashboard!.InvokeAsync<IReadOnlyList<LoanDto>>(nameof(IDashboardServer.GetLoansAsync));
        LoanItems.ItemsSource = loans;
    }

    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        var floatVal = PromptDecimal("Open Shift", "Enter cash drawer opening float:", 50m);
        if (floatVal is null) return;

        var res = await _dashboard!.InvokeAsync<ShiftResponse>(nameof(IDashboardServer.OpenShiftAsync), _cashierName, floatVal.Value);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Open Shift", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshDeskAsync();
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        var counted = PromptDecimal("Close Shift (Z-Report)", "Enter total counted cash in drawer:", 0m);
        if (counted is null) return;

        var res = await _dashboard!.InvokeAsync<ShiftResponse>(nameof(IDashboardServer.CloseShiftAsync), _cashierName, counted.Value, null);
        if (!res.Ok)
        {
            MessageBox.Show(this, res.Error, "Close Shift", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = res.Shift;
        MessageBox.Show(this,
            $"Z-Report Shift Summary:\n\nExpected Cash: {s?.ExpectedDrawer:C}\nCounted Cash: {s?.CountedDrawer:C}\nDrawer Variance: {s?.Variance:C}",
            "Shift Closed (Z-Report)", MessageBoxButton.OK, MessageBoxImage.Information);

        await RefreshDeskAsync();
    }

    private async void PrintXReport_Click(object sender, RoutedEventArgs e)
    {
        var shift = await _dashboard!.InvokeAsync<ShiftDto?>(nameof(IDashboardServer.GetCurrentShiftAsync));
        if (shift is null) return;
        var rpt = await _dashboard!.InvokeAsync<ShiftReportDto?>(nameof(IDashboardServer.GetShiftReportAsync), shift.Id);
        if (rpt is not null)
        {
            MessageBox.Show(this,
                $"X-Report (Interim Reading)\n\nCashier: {rpt.CashierName}\nTime Sales: {rpt.TimeRevenue:C}\nRetail Sales: {rpt.ProductRevenue:C}\nPrint/USB: {rpt.PrintUsbRevenue:C}\nDiscounts: {rpt.DiscountsTotal:C}\nExpected Cash: {rpt.ExpectedDrawer:C}",
                "Interim X-Report", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void AddWaitGuest_Click(object sender, RoutedEventArgs e)
    {
        var name = WaitNameInput.Text.Trim();
        if (name.Length == 0) return;
        var size = int.TryParse(WaitPartyInput.Text.Trim(), out var s) ? s : 1;
        await _dashboard!.InvokeAsync<WaitlistResponse>(nameof(IDashboardServer.AddToWaitlistAsync), name, size, WaitContactInput.Text.Trim());
        WaitNameInput.Clear();
        WaitContactInput.Clear();
    }

    private async void SeatGuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not WaitlistEntryDto entry) return;
        if (_selected is null)
        {
            MessageBox.Show(this, "Select an Available terminal on the Rack first.", "Seat Guest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await _dashboard!.InvokeAsync<StartSessionResponse>(nameof(IDashboardServer.SeatWaitlistGuestAsync), entry.Id, _selected.TerminalId, _cashierName);
    }

    private async void SkipGuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not WaitlistEntryDto entry) return;
        await _dashboard!.InvokeAsync<WaitlistResponse>(nameof(IDashboardServer.SkipWaitlistEntryAsync), entry.Id, _cashierName);
    }

    private async void LoanItem_Click(object sender, RoutedEventArgs e)
    {
        var item = LoanItemInput.Text.Trim();
        if (item.Length == 0) return;
        var deposit = decimal.TryParse(LoanDepositInput.Text.Trim(), out var d) ? d : 0m;
        await _dashboard!.InvokeAsync<LoanResponse>(nameof(IDashboardServer.LoanItemAsync), item, deposit, LoanHeldInput.Text.Trim(), null);
        LoanItemInput.Clear();
        LoanHeldInput.Clear();
        await RefreshDeskAsync();
    }

    private async void ReturnLoan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LoanDto loan) return;
        await _dashboard!.InvokeAsync<LoanResponse>(nameof(IDashboardServer.ReturnLoanAsync), loan.Id, _cashierName, _cashierName, false);
        await RefreshDeskAsync();
    }

    private async void ForfeitLoan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LoanDto loan) return;
        await _dashboard!.InvokeAsync<LoanResponse>(nameof(IDashboardServer.ReturnLoanAsync), loan.Id, _cashierName, _cashierName, true);
        await RefreshDeskAsync();
    }

    // ==========================================
    // 3. POS & SALES VIEW
    // ==========================================
    private async void NavSales_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (SalesView is not null) SalesView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshProductsAsync();
    }

    private async Task RefreshProductsAsync()
    {
        if (_dashboard is null) return;
        _sessionProducts = await _dashboard.InvokeAsync<IReadOnlyList<ProductDto>>(nameof(IDashboardServer.GetProductsAsync));
        if (ProductPicker is not null) ProductPicker.ItemsSource = _sessionProducts;

        _allProducts = await _dashboard.InvokeAsync<IReadOnlyList<ProductDetailDto>>(nameof(IDashboardServer.GetProductsFullAsync));
        FilterPosProducts();
        FilterInventory();
    }

    private void FilterPosProducts()
    {
        if (_allProducts is null) return;
        var q = PosSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var cat = PosCategoryFilter?.SelectedItem as string ?? "All";

        var filtered = _allProducts.Where(p =>
            (cat == "All" || p.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(q) || p.Name.ToLowerInvariant().Contains(q) || (p.Sku != null && p.Sku.Contains(q)))
        ).ToList();

        if (PosProductGrid is not null) PosProductGrid.ItemsSource = filtered;

        var categories = new HashSet<string> { "All" };
        foreach (var p in _allProducts) categories.Add(p.Category);
        if (PosCategoryFilter is not null)
        {
            PosCategoryFilter.ItemsSource = categories.ToList();
        }
    }

    private void PosSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterPosProducts();
    private void PosCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterPosProducts();

    private void PosProductTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ProductDetailDto prod) return;

        var existing = _posCart.FirstOrDefault(c => c.ProductId == prod.Id);
        if (existing is not null)
        {
            existing.Quantity++;
        }
        else
        {
            _posCart.Add(new CartItemViewModel
            {
                ProductId = prod.Id,
                Name = prod.Name,
                UnitPrice = prod.Price,
                Quantity = 1
            });
        }
        PosCartGrid.Items.Refresh();
        RecalculatePosTotals();
    }

    private void PosClearCart_Click(object sender, RoutedEventArgs e)
    {
        _posCart.Clear();
        RecalculatePosTotals();
    }

    private void PosDiscount_TextChanged(object sender, TextChangedEventArgs e) => RecalculatePosTotals();
    private void PosTender_TextChanged(object sender, TextChangedEventArgs e) => RecalculatePosTotals();

    private void RecalculatePosTotals()
    {
        var subtotal = _posCart.Sum(c => c.LineTotal);
        var discount = decimal.TryParse(PosDiscountInput?.Text?.Trim(), out var disc) ? disc : 0m;
        var total = Math.Max(0m, subtotal - discount);

        var cash = decimal.TryParse(PosPaidCash?.Text?.Trim(), out var csh) ? csh : 0m;
        var card = decimal.TryParse(PosPaidCard?.Text?.Trim(), out var crd) ? crd : 0m;
        var qr = decimal.TryParse(PosPaidQr?.Text?.Trim(), out var q) ? q : 0m;

        var change = Math.Max(0m, cash - Math.Max(0m, total - card - qr));

        if (PosSubtotalText is not null) PosSubtotalText.Text = subtotal.ToString("C");
        if (PosTotalText is not null) PosTotalText.Text = total.ToString("C");
        if (PosChangeText is not null) PosChangeText.Text = change.ToString("C");
    }

    private async void PosCompleteSale_Click(object sender, RoutedEventArgs e)
    {
        if (_posCart.Count == 0)
        {
            MessageBox.Show(this, "The cart is empty.", "POS Checkout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var discount = decimal.TryParse(PosDiscountInput.Text.Trim(), out var disc) ? disc : 0m;
        var cash = decimal.TryParse(PosPaidCash.Text.Trim(), out var csh) ? csh : 0m;
        var card = decimal.TryParse(PosPaidCard.Text.Trim(), out var crd) ? crd : 0m;
        var qr = decimal.TryParse(PosPaidQr.Text.Trim(), out var q) ? q : 0m;

        var lines = _posCart.Select(c => new SaleLineItemRequest(c.ProductId, "Product", c.Name, c.Quantity, c.UnitPrice, 0m)).ToList();
        var req = new CreateSaleRequest(null, _cashierName, null, "Cash", cash, card, qr, discount, "POS Retail Sale", lines);

        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.CreateSaleAsync), req);
        if (!res.Ok)
        {
            MessageBox.Show(this, res.Error ?? "Failed to complete POS sale.", "POS Checkout", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this,
            $"Sale Completed Successfully!\nChange Due: {PosChangeText.Text}\n\n[ESC/POS Thermal Receipt Dispatched to Printer]",
            "Sale Receipt", MessageBoxButton.OK, MessageBoxImage.Information);

        _posCart.Clear();
        PosDiscountInput.Text = "0.00";
        PosPaidCash.Text = "0.00";
        PosPaidCard.Text = "0.00";
        PosPaidQr.Text = "0.00";
        RecalculatePosTotals();
        await RefreshProductsAsync();
    }

    // ==========================================
    // 4. VOUCHERS / TICKETS VIEW
    // ==========================================
    private async void NavTickets_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (TicketsView is not null) TicketsView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshTicketsAsync();
    }

    private async Task RefreshTicketsAsync()
    {
        if (_dashboard is null) return;
        var unusedOnly = TicketsUnusedOnlyCheck?.IsChecked ?? true;
        _allTickets = await _dashboard.InvokeAsync<IReadOnlyList<TicketDto>>(nameof(IDashboardServer.GetTicketsAsync), unusedOnly);
        if (TicketsGrid is not null) TicketsGrid.ItemsSource = _allTickets;
    }

    private async void TicketsFilter_Changed(object sender, RoutedEventArgs e) => await RefreshTicketsAsync();

    private async void SellTicket_Click(object sender, RoutedEventArgs e)
    {
        var mins = PromptInt("Sell Duration Voucher", "Duration in minutes (e.g. 60):", 60);
        if (mins is null) return;
        var price = PromptDecimal("Sell Duration Voucher", "Price to charge customer:", 5.00m);
        if (price is null) return;

        var req = new SellTicketRequest("Duration", mins.Value, null, price.Value, "Cash", _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SellTicketAsync), req);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Voucher Sale", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshTicketsAsync();
    }

    private async void BatchGenerateTickets_Click(object sender, RoutedEventArgs e)
    {
        var count = PromptInt("Batch Generate Vouchers", "Number of vouchers to generate (1-100):", 10);
        if (count is null) return;
        var mins = PromptInt("Batch Generate Vouchers", "Duration per voucher (minutes):", 60);
        if (mins is null) return;
        var price = PromptDecimal("Batch Generate Vouchers", "Price per voucher:", 5.00m);
        if (price is null) return;

        var req = new BatchGenerateTicketsRequest("Duration", mins.Value, null, price.Value, count.Value, $"BATCH-{DateTime.Now:MMdd}", _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.BatchGenerateTicketsAsync), req);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Batch Generate", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshTicketsAsync();
    }

    private async void VoidTicket_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsGrid.SelectedItem is not TicketDto ticket)
        {
            MessageBox.Show(this, "Select a ticket from the table first.", "Void Ticket", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var scope = App.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();
        var prompt = new ManagerPinPromptWindow(auth, $"Authorize voiding voucher code '{ticket.Code}'");
        if (prompt.ShowDialog() == true && prompt.EnteredPin is not null)
        {
            var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.VoidTicketAsync), ticket.Id, _cashierName, prompt.EnteredPin);
            if (!res.Ok) MessageBox.Show(this, res.Error, "Void Ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshTicketsAsync();
        }
    }

    // ==========================================
    // 5. MEMBERS CLUB VIEW
    // ==========================================
    private async void NavMembers_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (MembersView is not null) MembersView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshMembersAsync();
    }

    private async Task RefreshMembersAsync()
    {
        if (_dashboard is null) return;
        _allMembers = await _dashboard.InvokeAsync<IReadOnlyList<MemberDetailDto>>(nameof(IDashboardServer.GetMembersAsync), (string?)null);
        FilterMembers();
    }

    private void MemberSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterMembers();

    private void FilterMembers()
    {
        if (_allMembers is null) return;
        var q = MemberSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var filtered = _allMembers.Where(m =>
            string.IsNullOrEmpty(q) || m.Name.ToLowerInvariant().Contains(q) || m.Code.ToLowerInvariant().Contains(q) || (m.Phone != null && m.Phone.Contains(q))
        ).ToList();

        if (MembersGrid is not null) MembersGrid.ItemsSource = filtered;
    }

    private async void AddMember_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptString("New Member", "Member Full Name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var phone = PromptString("New Member", "Member Phone Number:");

        var req = new SaveMemberRequest(null, name, phone, null, null, null);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SaveMemberAsync), req, _cashierName);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Create Member", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshMembersAsync();
    }

    private async void MemberTopUp_Click(object sender, RoutedEventArgs e)
    {
        if (MembersGrid.SelectedItem is not MemberDetailDto member)
        {
            MessageBox.Show(this, "Select a member from the table first.", "Member Top-up", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var amount = PromptDecimal("Member Top-up", $"Top-up Cash Balance for {member.Name}:", 20.00m);
        if (amount is null) return;

        var req = new MemberTopUpRequest(member.Id, "Money", amount.Value, 0, "Cash", _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.TopUpMemberAsync), req);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Member Top-up", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshMembersAsync();
    }

    private async void MemberFreeze_Click(object sender, RoutedEventArgs e)
    {
        if (MembersGrid.SelectedItem is not MemberDetailDto member) return;
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SetMemberFrozenAsync), member.Id, !member.IsFrozen, _cashierName);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Freeze Member", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshMembersAsync();
    }

    // ==========================================
    // 6. INVENTORY & STOCK VIEW
    // ==========================================
    private async void NavInventory_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (InventoryView is not null) InventoryView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshProductsAsync();
    }

    private void InvSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterInventory();
    private void InvCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterInventory();

    private void FilterInventory()
    {
        if (_allProducts is null) return;
        var q = InvSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var cat = InvCategoryFilter?.SelectedItem as string ?? "All";

        var filtered = _allProducts.Where(p =>
            (cat == "All" || p.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(q) || p.Name.ToLowerInvariant().Contains(q) || (p.Sku != null && p.Sku.Contains(q)))
        ).ToList();

        if (InventoryGrid is not null) InventoryGrid.ItemsSource = filtered;

        var categories = new HashSet<string> { "All" };
        foreach (var p in _allProducts) categories.Add(p.Category);
        if (InvCategoryFilter is not null)
        {
            InvCategoryFilter.ItemsSource = categories.ToList();
        }
    }

    private async void StockAdjust_Click(object sender, RoutedEventArgs e)
    {
        if (InventoryGrid.SelectedItem is not ProductDetailDto prod)
        {
            MessageBox.Show(this, "Select a product from the inventory table first.", "Stock Adjustment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var delta = PromptInt("Stock Adjustment", $"Enter quantity change for '{prod.Name}' (positive for restock, negative for waste):", 10);
        if (delta is null) return;

        var req = new StockAdjustmentRequest(prod.Id, delta.Value, delta.Value >= 0 ? "Restock" : "Waste", "Manual inventory adjustment", _cashierName);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.AdjustStockAsync), req);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Stock Adjustment", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshProductsAsync();
    }

    // ==========================================
    // 7. PRINT & USB SERVICES VIEW
    // ==========================================
    private async void NavPeripherals_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (PeripheralsView is not null) PeripheralsView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshPeripheralsAsync();
    }

    private async Task RefreshPeripheralsAsync()
    {
        if (_dashboard is null) return;
        var printJobs = await _dashboard.InvokeAsync<IReadOnlyList<PrintJobDto>>(nameof(IDashboardServer.GetPrintJobsAsync));
        if (PrintJobsGrid is not null) PrintJobsGrid.ItemsSource = printJobs;
    }

    private async void ReleasePrint_Click(object sender, RoutedEventArgs e)
    {
        if (PrintJobsGrid.SelectedItem is not PrintJobDto job) return;
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ReleasePrintJobAsync), job.Id, "Cash", _cashierName);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Release Print Job", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshPeripheralsAsync();
    }

    private async void CancelPrint_Click(object sender, RoutedEventArgs e)
    {
        if (PrintJobsGrid.SelectedItem is not PrintJobDto job) return;
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.CancelPrintJobAsync), job.Id, "Cancelled by cashier", _cashierName);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Cancel Print Job", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshPeripheralsAsync();
    }

    // ==========================================
    // 8. REPORTS & AUDIT VIEW
    // ==========================================
    private async void NavReports_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (ReportsView is not null) ReportsView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshReportsAsync();
    }

    private async Task RefreshReportsAsync()
    {
        if (_dashboard is null) return;
        var fromDate = DateTime.UtcNow.Date.AddDays(-7);
        var toDate = DateTime.UtcNow;

        var sessions = await _dashboard.InvokeAsync<IReadOnlyList<SessionHistoryDto>>(
            nameof(IDashboardServer.GetSessionHistoryAsync), fromDate, toDate, (Guid?)null);
        if (SessionHistoryGrid is not null) SessionHistoryGrid.ItemsSource = sessions;

        var audits = await _dashboard.InvokeAsync<IReadOnlyList<AuditEntryDto>>(
            nameof(IDashboardServer.GetAuditEntriesAsync), 100);
        if (AuditLogGrid is not null) AuditLogGrid.ItemsSource = audits;
    }

    private void ReportType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportTypePicker.SelectedIndex == 0)
        {
            SessionHistoryGrid.Visibility = Visibility.Visible;
            AuditLogGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            SessionHistoryGrid.Visibility = Visibility.Collapsed;
            AuditLogGrid.Visibility = Visibility.Visible;
        }
    }

    private async void VerifyAuditChain_Click(object sender, RoutedEventArgs e)
    {
        var result = await _dashboard!.InvokeAsync<AuditVerificationResult>(nameof(IDashboardServer.VerifyAuditChainAsync));
        if (result.IsValid)
        {
            MessageBox.Show(this, $"Audit Chain Cryptographically Verified!\n\nAll {result.CheckedCount} audit records verified against their linked SHA-256 signatures with 0 tampering detected.", "Cryptographic Audit", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, $"AUDIT CORRUPTION DETECTED!\n\n{result.ErrorMessage}", "Security Violation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportSessionsCsv_Click(object sender, RoutedEventArgs e)
    {
        var fromDate = DateTime.UtcNow.Date.AddDays(-30);
        var toDate = DateTime.UtcNow;
        var sessions = await _dashboard!.InvokeAsync<IReadOnlyList<SessionHistoryDto>>(
            nameof(IDashboardServer.GetSessionHistoryAsync), fromDate, toDate, (Guid?)null);

        using var scope = App.Services.CreateScope();
        var reportsSvc = scope.ServiceProvider.GetRequiredService<ReportsAndAuditService>();
        var csv = reportsSvc.ExportSessionHistoryToCsv(sessions);

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"ZixCafe_Sessions_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        await File.WriteAllTextAsync(path, csv);
        MessageBox.Show(this, $"Exported session history to:\n{path}", "CSV Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ExportRevenueCsv_Click(object sender, RoutedEventArgs e)
    {
        var fromDate = DateTime.UtcNow.Date.AddDays(-30);
        var toDate = DateTime.UtcNow;
        var rev = await _dashboard!.InvokeAsync<IReadOnlyList<DailyRevenueDto>>(
            nameof(IDashboardServer.GetDailyRevenueReportAsync), fromDate, toDate);

        using var scope = App.Services.CreateScope();
        var reportsSvc = scope.ServiceProvider.GetRequiredService<ReportsAndAuditService>();
        var csv = reportsSvc.ExportRevenueToCsv(rev);

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"ZixCafe_Revenue_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        await File.WriteAllTextAsync(path, csv);
        MessageBox.Show(this, $"Exported revenue report to:\n{path}", "CSV Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==========================================
    // 9. ALERTS CENTER VIEW
    // ==========================================
    private void NavAlerts_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (AlertsView is not null) AlertsView.Visibility = Visibility.Visible;
    }

    private void AckAllAlerts_Click(object sender, RoutedEventArgs e)
    {
        _alerts.Clear();
    }

    // ==========================================
    // 10. SETTINGS & TARIFFS VIEW
    // ==========================================
    private async void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (SettingsView is not null) SettingsView.Visibility = Visibility.Visible;
        if (_dashboard is not null) await RefreshSettingsViewAsync();
    }

    private void PopulateConfigFields(MasterSystemSettingsDto cfg)
    {
        ConfigMetaText.Text = $"SCHEMA: {cfg.SchemaVersion} · LAST UPDATED: {cfg.LastUpdatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC BY {cfg.LastUpdatedBy.ToUpperInvariant()} · WAL ATOMIC WRITES";

        // 1. Rack
        CfgInactivityMinutes.Text = cfg.InactivityStandbyMinutes.ToString();
        SelectComboContent(CfgInactivityMode, cfg.InactivityStandbyMode);
        SelectComboContent(CfgUsbStoragePolicy, cfg.UsbStoragePolicy);
        CfgEnableInactivityStandby.IsChecked = cfg.EnableInactivityStandby;
        CfgAutoKillProhibited.IsChecked = cfg.AutoKillProhibitedProcesses;
        CfgProhibitedCsv.Text = cfg.ProhibitedProcessesCsv;
        CfgBlockWinKey.IsChecked = cfg.ShellLockBlockWinKey;
        CfgBlockAltTab.IsChecked = cfg.ShellLockBlockAltTab;
        CfgBlockCtrlShiftEsc.IsChecked = cfg.ShellLockBlockCtrlShiftEsc;
        CfgBlockTaskMgr.IsChecked = cfg.ShellLockBlockTaskManager;

        // 2. Privacy & Lifecycle
        CfgKillUserProcesses.IsChecked = cfg.CleanupKillUserProcessesOnSessionEnd;
        CfgClearBrowserCaches.IsChecked = cfg.CleanupClearBrowserCachesOnSessionEnd;
        CfgWipeDownloadsDesktop.IsChecked = cfg.CleanupWipeDownloadsAndDesktop;
        CfgResetVolume.IsChecked = cfg.CleanupResetMasterVolume;
        CfgResetMouse.IsChecked = cfg.CleanupResetMouseSensitivity;
        CfgRebootRestoreOnEnd.IsChecked = cfg.EnableRebootToRestoreOnSessionEnd;
        CfgGracePeriodSec.Text = cfg.NetworkDropGracePeriodSeconds.ToString();
        CfgWarningMinutes.Text = cfg.SessionExtensionWarningMinutes.ToString();
        SelectComboContent(CfgDisklessProvider, cfg.DisklessProvider);

        // 3. Dynamic Tariff
        CfgMinSessionCharge.Text = cfg.MinimumSessionCharge.ToString("F2");
        SelectComboContent(CfgRoundingRule, cfg.CurrencyRoundingRule);
        CfgFixedWindowPasses.IsChecked = cfg.EnableFixedWindowPasses;
        CfgDynamicOccupancy.IsChecked = cfg.EnableDynamicOccupancyMultipliers;
        CfgOccLowThresh.Text = cfg.OccupancyLowThresholdPercent.ToString();
        CfgOccDiscount.Text = cfg.LowOccupancyDiscountPercent.ToString("F2");
        CfgOccHighThresh.Text = cfg.OccupancyHighThresholdPercent.ToString();
        CfgOccSurcharge.Text = cfg.HighOccupancySurchargePercent.ToString("F2");

        // 4. POS & Receipts
        CfgVenueName.Text = cfg.VenueName;
        CfgCurrencySymbol.Text = cfg.CurrencySymbol;
        CfgCurrencyCode.Text = cfg.CurrencyCode;
        CfgDecimalPlaces.Text = cfg.CurrencyDecimalPlaces.ToString();
        CfgTaxLabel.Text = cfg.TaxLabel;
        CfgTaxRate.Text = cfg.TaxRatePercent.ToString("F2");
        CfgOpeningFloat.Text = cfg.DefaultOpeningFloat.ToString("F2");
        CfgReceiptHeader.Text = cfg.ReceiptHeaderText;
        CfgReceiptFooter.Text = cfg.ReceiptFooterNotes;
        SelectComboContent(CfgPrinterWidth, cfg.ReceiptPrinterWidthMm == 58 ? "58mm" : "80mm");
        CfgDrawerPulse.Text = cfg.CashDrawerKickPulseCode;
        CfgMandatoryLoanReturn.IsChecked = cfg.EnforceMandatoryHardwareLoanReturnOnCheckout;

        // 5. RBAC
        CfgPinManualTime.IsChecked = cfg.RequireSupervisorPinForManualTimeAdd;
        CfgPinBillVoid.IsChecked = cfg.RequireSupervisorPinForBillVoid;
        CfgPinDrawerKick.IsChecked = cfg.RequireSupervisorPinForManualDrawerKick;
        CfgPinStockAdjust.IsChecked = cfg.RequireSupervisorPinForStockAdjustment;
        CfgBlindDrawerClose.IsChecked = cfg.EnforceBlindCashDrawerClose;

        // 6. Network / IoT
        CfgSignalRPort.Text = cfg.SignalRServerPort.ToString();
        CfgWolSubnet.Text = cfg.WakeOnLanBroadcastSubnet;
        CfgWolPort.Text = cfg.WakeOnLanPort.ToString();
        SelectComboContent(CfgRouterType, cfg.RouterType);
        CfgRouterIp.Text = cfg.RouterIpAddress;
        CfgBandwidthLimit.Text = cfg.GuestDefaultBandwidthLimitMbps.ToString();
        CfgMqttHost.Text = cfg.MqttBrokerAddress;
        CfgMqttPort.Text = cfg.MqttBrokerPort.ToString();
        CfgMqttUser.Text = cfg.MqttUsername;
    }

    private static void SelectComboContent(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (ComboBoxItem item in combo.Items)
        {
            if ((item.Content as string)?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private MasterSystemSettingsDto CollectConfigDto(MasterSystemSettingsDto original)
    {
        return original with
        {
            // 1. Rack
            InactivityStandbyMinutes = int.TryParse(CfgInactivityMinutes.Text.Trim(), out var im) ? im : 10,
            InactivityStandbyMode = (CfgInactivityMode.SelectedItem as ComboBoxItem)?.Content as string ?? "Sleep",
            UsbStoragePolicy = (CfgUsbStoragePolicy.SelectedItem as ComboBoxItem)?.Content as string ?? "WhitelistHidOnly",
            EnableInactivityStandby = CfgEnableInactivityStandby.IsChecked == true,
            AutoKillProhibitedProcesses = CfgAutoKillProhibited.IsChecked == true,
            ProhibitedProcessesCsv = CfgProhibitedCsv.Text.Trim(),
            ShellLockBlockWinKey = CfgBlockWinKey.IsChecked == true,
            ShellLockBlockAltTab = CfgBlockAltTab.IsChecked == true,
            ShellLockBlockCtrlShiftEsc = CfgBlockCtrlShiftEsc.IsChecked == true,
            ShellLockBlockTaskManager = CfgBlockTaskMgr.IsChecked == true,

            // 2. Privacy
            CleanupKillUserProcessesOnSessionEnd = CfgKillUserProcesses.IsChecked == true,
            CleanupClearBrowserCachesOnSessionEnd = CfgClearBrowserCaches.IsChecked == true,
            CleanupWipeDownloadsAndDesktop = CfgWipeDownloadsDesktop.IsChecked == true,
            CleanupResetMasterVolume = CfgResetVolume.IsChecked == true,
            CleanupResetMouseSensitivity = CfgResetMouse.IsChecked == true,
            EnableRebootToRestoreOnSessionEnd = CfgRebootRestoreOnEnd.IsChecked == true,
            NetworkDropGracePeriodSeconds = int.TryParse(CfgGracePeriodSec.Text.Trim(), out var gps) ? gps : 180,
            SessionExtensionWarningMinutes = int.TryParse(CfgWarningMinutes.Text.Trim(), out var wm) ? wm : 5,
            DisklessProvider = (CfgDisklessProvider.SelectedItem as ComboBoxItem)?.Content as string ?? "None",

            // 3. Dynamic Tariff
            MinimumSessionCharge = decimal.TryParse(CfgMinSessionCharge.Text.Trim(), out var msc) ? msc : 1.00m,
            CurrencyRoundingRule = (CfgRoundingRule.SelectedItem as ComboBoxItem)?.Content as string ?? "None",
            EnableFixedWindowPasses = CfgFixedWindowPasses.IsChecked == true,
            EnableDynamicOccupancyMultipliers = CfgDynamicOccupancy.IsChecked == true,
            OccupancyLowThresholdPercent = int.TryParse(CfgOccLowThresh.Text.Trim(), out var olt) ? olt : 30,
            LowOccupancyDiscountPercent = decimal.TryParse(CfgOccDiscount.Text.Trim(), out var lod) ? lod : 10m,
            OccupancyHighThresholdPercent = int.TryParse(CfgOccHighThresh.Text.Trim(), out var oht) ? oht : 85,
            HighOccupancySurchargePercent = decimal.TryParse(CfgOccSurcharge.Text.Trim(), out var hos) ? hos : 15m,

            // 4. POS & Receipts
            VenueName = CfgVenueName.Text.Trim(),
            CurrencySymbol = CfgCurrencySymbol.Text.Trim(),
            CurrencyCode = CfgCurrencyCode.Text.Trim(),
            CurrencyDecimalPlaces = int.TryParse(CfgDecimalPlaces.Text.Trim(), out var dp) ? dp : 2,
            TaxLabel = CfgTaxLabel.Text.Trim(),
            TaxRatePercent = decimal.TryParse(CfgTaxRate.Text.Trim(), out var tr) ? tr : 0m,
            DefaultOpeningFloat = decimal.TryParse(CfgOpeningFloat.Text.Trim(), out var of) ? of : 50m,
            ReceiptHeaderText = CfgReceiptHeader.Text,
            ReceiptFooterNotes = CfgReceiptFooter.Text,
            ReceiptPrinterWidthMm = (CfgPrinterWidth.SelectedItem as ComboBoxItem)?.Content as string == "58mm" ? 58 : 80,
            CashDrawerKickPulseCode = CfgDrawerPulse.Text.Trim(),
            EnforceMandatoryHardwareLoanReturnOnCheckout = CfgMandatoryLoanReturn.IsChecked == true,

            // 5. RBAC
            RequireSupervisorPinForManualTimeAdd = CfgPinManualTime.IsChecked == true,
            RequireSupervisorPinForBillVoid = CfgPinBillVoid.IsChecked == true,
            RequireSupervisorPinForManualDrawerKick = CfgPinDrawerKick.IsChecked == true,
            RequireSupervisorPinForStockAdjustment = CfgPinStockAdjust.IsChecked == true,
            EnforceBlindCashDrawerClose = CfgBlindDrawerClose.IsChecked == true,

            // 6. Network / IoT
            SignalRServerPort = int.TryParse(CfgSignalRPort.Text.Trim(), out var sp) ? sp : 40000,
            WakeOnLanBroadcastSubnet = CfgWolSubnet.Text.Trim(),
            WakeOnLanPort = int.TryParse(CfgWolPort.Text.Trim(), out var wp) ? wp : 9,
            RouterType = (CfgRouterType.SelectedItem as ComboBoxItem)?.Content as string ?? "None",
            RouterIpAddress = CfgRouterIp.Text.Trim(),
            GuestDefaultBandwidthLimitMbps = int.TryParse(CfgBandwidthLimit.Text.Trim(), out var bw) ? bw : 50,
            MqttBrokerAddress = CfgMqttHost.Text.Trim(),
            MqttBrokerPort = int.TryParse(CfgMqttPort.Text.Trim(), out var mp) ? mp : 1883,
            MqttUsername = CfgMqttUser.Text.Trim()
        };
    }

    private async Task RefreshSettingsViewAsync()
    {
        if (_dashboard is null) return;
        try
        {
            var masterCfg = await _dashboard.InvokeAsync<MasterSystemSettingsDto>(nameof(IDashboardServer.GetMasterSettingsAsync));
            if (masterCfg is not null)
            {
                PopulateConfigFields(masterCfg);
            }
        }
        catch
        {
        }

        _allCashiers = await _dashboard.InvokeAsync<IReadOnlyList<CashierDto>>(nameof(IDashboardServer.GetCashiersAsync));
        if (CashiersGrid is not null) CashiersGrid.ItemsSource = _allCashiers;

        _allTariffs = await _dashboard.InvokeAsync<IReadOnlyList<TariffDto>>(nameof(IDashboardServer.GetTariffsAsync));
        if (TariffsGrid is not null) TariffsGrid.ItemsSource = _allTariffs;

        await RefreshBackupsListAsync();
    }

    private async Task RefreshBackupsListAsync()
    {
        if (_dashboard is null) return;
        try
        {
            if (_dashboard is null) return;
            var backups = await _dashboard.InvokeAsync<IReadOnlyList<BackupFileInfoDto>>(nameof(IDashboardServer.ListBackupsAsync));
            BackupsGrid.ItemsSource = backups;

            var dbInfo = await _dashboard.InvokeAsync<string>(nameof(IDashboardServer.GetDatabaseInfoAsync));
            DatabaseInfoText.Text = dbInfo;
        }
        catch
        {
        }
    }

    private async void SaveMasterConfig_Click(object sender, RoutedEventArgs e)
    {
        var current = await _dashboard!.InvokeAsync<MasterSystemSettingsDto>(nameof(IDashboardServer.GetMasterSettingsAsync));
        var updated = CollectConfigDto(current);

        var res = await _dashboard!.InvokeAsync<ResultResponse>(
            nameof(IDashboardServer.SaveMasterSettingsAsync), updated, "Admin Studio configuration update", _cashierName);

        if (res.Ok)
        {
            MessageBox.Show(this, "Master configuration saved and broadcast to all live client agents successfully.", "Configuration Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshSettingsViewAsync();
        }
        else
        {
            MessageBox.Show(this, res.Error, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ResetCategoryConfig_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category }) return;

        var confirm = MessageBox.Show(this, $"Reset all settings in category '{category.ToUpperInvariant()}' to recommended defaults?", "Reset Category Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var res = await _dashboard!.InvokeAsync<MasterSystemSettingsDto>(
            nameof(IDashboardServer.ResetSettingsCategoryAsync), category, _cashierName);

        if (res is not null)
        {
            PopulateConfigFields(res);
            MessageBox.Show(this, $"Category '{category}' reset to defaults.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ResetAllConfig_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "Reset ALL system settings, policies, tariffs, and integrations to factory recommended defaults?\n\nThis will apply immediately and broadcast to all connected agents.", "Reset All System Defaults", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var res = await _dashboard!.InvokeAsync<MasterSystemSettingsDto>(
            nameof(IDashboardServer.ResetSettingsCategoryAsync), "all", _cashierName);

        if (res is not null)
        {
            PopulateConfigFields(res);
            MessageBox.Show(this, "All system configuration settings have been reset to factory defaults.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BackupDatabase_Click(object sender, RoutedEventArgs e)
    {
        BackupStatusText.Text = "Creating online SQLite backup snapshot...";
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.TriggerBackupAsync), (string?)null, _cashierName);
        BackupStatusText.Text = res.Ok ? $"Backup generated at {DateTime.Now:HH:mm:ss}" : $"Backup failed: {res.Error}";
        await RefreshBackupsListAsync();
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Database Backup",
            Filter = "SQLite Database (*.db)|*.db",
            FileName = $"zixcafe_export_{DateTime.Now:yyyyMMdd_HHmmss}.db"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var targetDir = System.IO.Path.GetDirectoryName(dialog.FileName);
            BackupStatusText.Text = "Exporting database backup...";
            var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.TriggerBackupAsync), targetDir, _cashierName);
            if (res.Ok)
            {
                MessageBox.Show(this, $"Backup exported successfully to:\n{res.Error ?? dialog.FileName}", "Export Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                BackupStatusText.Text = $"Exported at {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                MessageBox.Show(this, res.Error, "Export Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                BackupStatusText.Text = "Export failed.";
            }
            await RefreshBackupsListAsync();
        }
    }

    private async void RestoreFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Backup File to Restore",
            Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await PerformDatabaseRestoreAsync(dialog.FileName);
        }
    }

    private async void RestoreGridBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath } && !string.IsNullOrWhiteSpace(filePath))
        {
            await PerformDatabaseRestoreAsync(filePath);
        }
    }

    private async Task PerformDatabaseRestoreAsync(string backupFilePath)
    {
        var confirm = MessageBox.Show(this,
            $"WARNING: Restoring from a backup will replace your current live database.\n\nSource: {System.IO.Path.GetFileName(backupFilePath)}\n\nA pre-restore safety copy of your current database will be saved automatically.\n\nDo you want to continue?",
            "Confirm Database Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        // Challenge for Manager PIN authorization
        using var scope = App.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();
        var pinModal = new ManagerPinPromptWindow(authService, "Authorize Database Restore");
        if (pinModal.ShowDialog() != true) return;

        BackupStatusText.Text = "Restoring database from backup...";
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.RestoreBackupAsync), backupFilePath, _cashierName);

        if (res.Ok)
        {
            MessageBox.Show(this,
                $"Database restored successfully from {System.IO.Path.GetFileName(backupFilePath)}!\n\nAll views and cached records have been refreshed.",
                "Database Restored",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            BackupStatusText.Text = $"Restored at {DateTime.Now:HH:mm:ss}";

            // Refresh all views to reflect restored data
            await RefreshSettingsViewAsync();
            await RefreshProductsAsync();
            await RefreshMembersAsync();
            await RefreshTicketsAsync();
            await RefreshReportsAsync();
            await RefreshDeskAsync();
        }
        else
        {
            MessageBox.Show(this, $"Restore failed: {res.Error}", "Database Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            BackupStatusText.Text = "Restore failed.";
        }
    }

    private async void AddCashier_Click(object sender, RoutedEventArgs e)
    {
        var user = PromptString("Add Cashier", "Cashier Username:");
        if (string.IsNullOrWhiteSpace(user)) return;
        var pin = PromptString("Add Cashier", "4+ Digit PIN:");
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4) return;

        var req = new CreateCashierRequest(user, pin, "Cashier");
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.CreateCashierAsync), req, _cashierName);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Add Cashier", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshSettingsViewAsync();
    }

    private async void AddTariff_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptString("Add Tariff", "Tariff Name (e.g. VIP Gamer Hourly):");
        if (string.IsNullOrWhiteSpace(name)) return;
        var rate = PromptDecimal("Add Tariff", "Hourly Rate:", 3.50m);
        if (rate is null) return;

        var req = new SaveTariffRequest(null, name, "Flat", rate.Value, 5, 1.00m, 0, []);
        var res = await _dashboard!.InvokeAsync<ResultResponse>(nameof(IDashboardServer.SaveTariffAsync), req, _cashierName);
        if (!res.Ok) MessageBox.Show(this, res.Error, "Add Tariff", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshSettingsViewAsync();
    }

    private void HideAllViews()
    {
        if (RackView is not null) RackView.Visibility = Visibility.Collapsed;
        if (DeskView is not null) DeskView.Visibility = Visibility.Collapsed;
        if (SalesView is not null) SalesView.Visibility = Visibility.Collapsed;
        if (TicketsView is not null) TicketsView.Visibility = Visibility.Collapsed;
        if (MembersView is not null) MembersView.Visibility = Visibility.Collapsed;
        if (InventoryView is not null) InventoryView.Visibility = Visibility.Collapsed;
        if (PeripheralsView is not null) PeripheralsView.Visibility = Visibility.Collapsed;
        if (ReportsView is not null) ReportsView.Visibility = Visibility.Collapsed;
        if (AlertsView is not null) AlertsView.Visibility = Visibility.Collapsed;
        if (SettingsView is not null) SettingsView.Visibility = Visibility.Collapsed;
    }

    private string? PromptString(string title, string label)
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
        var input = new TextBox { Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 6, 8, 6), FontSize = 14, Style = (Style)FindResource("AppTextBox") };
        var ok = new Button { Content = "Confirm", IsDefault = true, Style = (Style)FindResource("GoldButton"), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 6, 16, 6) };
        string? result = null;
        ok.Click += (_, _) => { result = input.Text.Trim(); dialog.DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(8, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 6, 16, 6) };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = (System.Windows.Media.Brush)FindResource("InkBrush"), Style = (Style)FindResource("BodyText") });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        input.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private decimal? PromptDecimal(string title, string label, decimal fallback)
    {
        var str = PromptString(title, label);
        return decimal.TryParse(str, out var val) ? val : (str is null ? null : fallback);
    }

    private int? PromptInt(string title, string label, int fallback)
    {
        var str = PromptString(title, label);
        return int.TryParse(str, out var val) ? val : (str is null ? null : fallback);
    }
}
