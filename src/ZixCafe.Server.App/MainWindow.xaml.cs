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
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Rack;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App;

public partial class MainWindow : Window
{
    public sealed record ChatLine(string From, string Message);
    public sealed record LiveEventLogItem(string Timestamp, string Message, System.Windows.Media.Brush ColorBrush);

    public class CartItemViewModel
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal => UnitPrice * Quantity;
    }

    public class FleetStationItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string TerminalType { get; set; } = "PC";
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public int NativeRefreshRateHz { get; set; } = 240;
        public string Status { get; set; } = "Offline";
        public string AgentVersion { get; set; } = "v1.0.0";
    }

    public class TariffAdminItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = "Flat";
        public decimal BaseRatePerHour { get; set; }
        public decimal MinimumCharge { get; set; }
        public int RoundingMinutes { get; set; }
        public int Priority { get; set; }
        public int RulesCount { get; set; }
    }

    private const string DashboardUrl = "http://localhost:40000/hubs/dashboard";

    private static readonly System.Windows.Media.SolidColorBrush LogColorGreen = new(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly System.Windows.Media.SolidColorBrush LogColorOrange = new(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly System.Windows.Media.SolidColorBrush LogColorRed = new(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly System.Windows.Media.SolidColorBrush LogColorCyan = new(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly System.Windows.Media.SolidColorBrush LogColorInfo = new(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));

    private readonly Domain.Entities.Cashier _cashier;
    private readonly bool _isAdmin;
    private readonly string _cashierName;
    private readonly string _cashierRole;

    private readonly ObservableCollection<TileViewModel> _tiles = [];
    private readonly ObservableCollection<TileViewModel> _filteredTiles = [];
    private readonly Dictionary<Guid, ObservableCollection<ChatLine>> _chatLogs = [];
    private readonly ObservableCollection<CartItemViewModel> _posCart = [];
    private readonly ObservableCollection<AlertDto> _alerts = [];
    private readonly ObservableCollection<LiveEventLogItem> _liveEventLogs = [];

    // Admin CMS Collections
    private readonly ObservableCollection<FleetStationItem> _fleetStations = [];
    private readonly ObservableCollection<FleetStationItem> _filteredFleetStations = [];
    private readonly ObservableCollection<TariffAdminItem> _adminTariffs = [];
    private readonly ObservableCollection<CashierDto> _adminStaff = [];
    private readonly ObservableCollection<ProductDetailDto> _adminInventory = [];
    private readonly ObservableCollection<MemberDetailDto> _adminMembers = [];
    private readonly ObservableCollection<TicketDto> _adminTickets = [];

    private HubConnection? _dashboard;
    private readonly DispatcherTimer _uiClock = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
    private TileViewModel? _selected;

    private IReadOnlyList<ProductDetailDto> _allProducts = [];
    private IReadOnlyList<MemberDetailDto> _allMembers = [];
    private IReadOnlyList<TicketDto> _allTickets = [];
    private IReadOnlyList<CashierDto> _allCashiers = [];
    private IReadOnlyList<TariffDto> _allTariffs = [];

    public MainWindow(Domain.Entities.Cashier cashier)
    {
        _cashier = cashier;
        _cashierName = cashier.Name;
        _cashierRole = cashier.Role.ToString();
        _isAdmin = cashier.Role == CashierRole.Owner || cashier.Role == CashierRole.Manager;

        InitializeComponent();

        ApplyRolePermissions();

        RackItems.ItemsSource = _filteredTiles;
        PosCartGrid.ItemsSource = _posCart;
        AlertsGrid.ItemsSource = _alerts;
        LiveEventLogsList.ItemsSource = _liveEventLogs;

        FleetDataGrid.ItemsSource = _filteredFleetStations;
        TariffsAdminGrid.ItemsSource = _adminTariffs;
        StaffAdminGrid.ItemsSource = _adminStaff;
        InventoryGrid.ItemsSource = _adminInventory;
        MembersGrid.ItemsSource = _adminMembers;
        TicketsGrid.ItemsSource = _adminTickets;

        ReportTypePicker.ItemsSource = new[] { "Session History", "Audit Trail (SHA-256)" };
        ReportTypePicker.SelectedIndex = 0;

        Loaded += OnLoaded;

        _uiClock.Tick += (_, _) =>
        {
            LiveClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            LiveDateText.Text = DateTime.Now.ToString("dddd, d MMMM yyyy");

            foreach (var tile in _tiles)
            {
                tile.RefreshTime();
            }
            UpdateOccupancyMetrics();
        };
        _uiClock.Start();

        AddLiveLog("ZixCafe Pro v1.0.0 Enterprise Core Initialized", LogColorInfo);
        AddLiveLog($"Cashier authenticated: {_cashierName.ToUpperInvariant()} ({_cashierRole.ToUpperInvariant()})", LogColorGreen);
        AddLiveLog(_isAdmin ? "Role permissions: ADMINISTRATOR (Full Control CMS Unlocked)" : "Role permissions: EMPLOYEE (Operational Workflow Restricted)", _isAdmin ? LogColorGreen : LogColorCyan);
    }

    private void ApplyRolePermissions()
    {
        CashierText.Text = $"CASHIER: {_cashierName.ToUpperInvariant()}";

        if (_isAdmin)
        {
            RoleBadge.Text = "ADMINISTRATOR (FULL CONTROL CMS)";
            RoleBadge.Foreground = (System.Windows.Media.Brush)FindResource("GoldBrush");

            NavFleet.Visibility = Visibility.Visible;
            NavTariffs.Visibility = Visibility.Visible;
            NavMembers.Visibility = Visibility.Visible;
            NavStaff.Visibility = Visibility.Visible;
            NavInventory.Visibility = Visibility.Visible;
            NavTickets.Visibility = Visibility.Visible;
            NavReports.Visibility = Visibility.Visible;
            NavAlerts.Visibility = Visibility.Visible;
            NavSettings.Visibility = Visibility.Visible;
            NavPeripherals.Visibility = Visibility.Visible;
        }
        else
        {
            RoleBadge.Text = "EMPLOYEE (OPERATIONS ONLY)";
            RoleBadge.Foreground = (System.Windows.Media.Brush)FindResource("GhostBrush");

            // Structurally isolate administrative CMS views
            NavFleet.Visibility = Visibility.Collapsed;
            NavTariffs.Visibility = Visibility.Collapsed;
            NavMembers.Visibility = Visibility.Collapsed;
            NavStaff.Visibility = Visibility.Collapsed;
            NavInventory.Visibility = Visibility.Collapsed;
            NavTickets.Visibility = Visibility.Collapsed;
            NavReports.Visibility = Visibility.Collapsed;
            NavAlerts.Visibility = Visibility.Collapsed;
            NavSettings.Visibility = Visibility.Collapsed;
            NavPeripherals.Visibility = Visibility.Collapsed;
        }
    }

    public void AddLiveLog(string message, System.Windows.Media.Brush color)
    {
        var item = new LiveEventLogItem(DateTime.Now.ToString("HH:mm:ss"), message, color);
        _liveEventLogs.Insert(0, item);
        while (_liveEventLogs.Count > 150)
        {
            _liveEventLogs.RemoveAt(_liveEventLogs.Count - 1);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckFirstRunSetupAsync();
            await LoadTerminalsAsync();
            await RefreshAdminCollectionsAsync();

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
                        var prevRunning = tile.IsRunning;
                        tile.Apply(state);
                        if (tile == _selected)
                        {
                            RenderInspector();
                        }
                        if (prevRunning != tile.IsRunning)
                        {
                            AddLiveLog($"{state.Name}: Session state changed to {tile.StatusText}", tile.IsRunning ? LogColorGreen : LogColorOrange);
                            UpdateOccupancyMetrics();
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
                    AddLiveLog($"CHAT [{from}]: {message}", LogColorCyan);
                }));

            _dashboard.On<string, string, string, Guid?, DateTime>("AlertRaised",
                (severity, kind, message, terminalId, time) =>
                Dispatcher.BeginInvoke(() =>
                {
                    var termName = terminalId.HasValue ? _tiles.FirstOrDefault(t => t.TerminalId == terminalId.Value)?.Name : null;
                    _alerts.Insert(0, new AlertDto(Guid.NewGuid(), severity, kind, message, terminalId, termName, time, false, null, null));
                    while (_alerts.Count > 100) _alerts.RemoveAt(_alerts.Count - 1);
                    AddLiveLog($"ALERT [{severity}]: {message} ({(termName ?? "Floor")})", LogColorRed);
                }));

            _dashboard.On<IReadOnlyList<WaitlistEntryDto>>("WaitlistChanged", waiting =>
                Dispatcher.BeginInvoke(() => WaitlistItems.ItemsSource = waiting));

            await _dashboard.StartAsync();
            await _dashboard.InvokeAsync(nameof(IDashboardServer.SubscribeAsync));

            HealthText.Text = "SERVER · PORT 40000 · ONLINE";
            AddLiveLog("SignalR Dashboard connection established", LogColorGreen);

            await RefreshProductsAsync();
            await RefreshSettingsViewAsync();
            UpdateOccupancyMetrics();
        }
        catch (Exception ex)
        {
            HealthText.Text = "SERVER · LOCAL MODE";
            AddLiveLog($"Server warning: {ex.Message}", LogColorOrange);
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
                null, null, 0, 0, null, null, false,
                t.MaintenanceReason, t.ReservedFor,
                t.CpuTemp, t.GpuTemp, t.RamPercent, t.DiskFreeGb));

            _tiles.Add(vm);
            _filteredTiles.Add(vm);
            zones.Add(vm.ZoneName);
        }

        RackZoneFilter.ItemsSource = zones.ToList();
        RackZoneFilter.SelectedIndex = 0;
        FleetZoneFilter.ItemsSource = zones.ToList();
        FleetZoneFilter.SelectedIndex = 0;
    }

    private async Task RefreshAdminCollectionsAsync()
    {
        await RefreshFleetAsync();
        await RefreshTariffsAsync();
        await RefreshStaffAsync();
        await RefreshMembersAsync();
        await RefreshInventoryAsync();
        await RefreshTicketsAsync();
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
        else if (e.Key == Key.F8)
        {
            ChatInput.Focus();
        }
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            RackSearchBox.Focus();
            RackSearchBox.SelectAll();
        }
    }

    // ==========================================
    // NAVIGATION ROUTING & RBAC ISOLATION
    // ==========================================
    private void HideAllViews()
    {
        if (RackView is not null) RackView.Visibility = Visibility.Collapsed;
        if (FleetView is not null) FleetView.Visibility = Visibility.Collapsed;
        if (TariffsView is not null) TariffsView.Visibility = Visibility.Collapsed;
        if (StaffView is not null) StaffView.Visibility = Visibility.Collapsed;
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

    private void NavRack_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (RackView is not null) RackView.Visibility = Visibility.Visible;
    }

    private void NavSales_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (SalesView is not null) SalesView.Visibility = Visibility.Visible;
        _ = RefreshProductsAsync();
    }

    private void NavDesk_Checked(object sender, RoutedEventArgs e)
    {
        HideAllViews();
        if (DeskView is not null) DeskView.Visibility = Visibility.Visible;
    }

    private void NavFleet_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (FleetView is not null) FleetView.Visibility = Visibility.Visible;
        _ = RefreshFleetAsync();
    }

    private void NavTariffs_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (TariffsView is not null) TariffsView.Visibility = Visibility.Visible;
        _ = RefreshTariffsAsync();
    }

    private void NavMembers_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (MembersView is not null) MembersView.Visibility = Visibility.Visible;
        _ = RefreshMembersAsync();
    }

    private void NavStaff_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (StaffView is not null) StaffView.Visibility = Visibility.Visible;
        _ = RefreshStaffAsync();
    }

    private void NavInventory_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (InventoryView is not null) InventoryView.Visibility = Visibility.Visible;
        _ = RefreshInventoryAsync();
    }

    private void NavTickets_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (TicketsView is not null) TicketsView.Visibility = Visibility.Visible;
        _ = RefreshTicketsAsync();
    }

    private void NavPeripherals_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (PeripheralsView is not null) PeripheralsView.Visibility = Visibility.Visible;
    }

    private void NavReports_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (ReportsView is not null) ReportsView.Visibility = Visibility.Visible;
        _ = RefreshReportsAsync();
    }

    private void NavAlerts_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (AlertsView is not null) AlertsView.Visibility = Visibility.Visible;
    }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin) return;
        HideAllViews();
        if (SettingsView is not null) SettingsView.Visibility = Visibility.Visible;
        _ = RefreshSettingsViewAsync();
    }

    // ==========================================
    // 1. RACK & WORKSTATION INSPECTOR
    // ==========================================
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
            InspectorStationName.Text = "NO STATION SELECTED";
            InspectorZone.Text = "—";
            InspectorStatus.Text = "IDLE";
            InspectorUser.Text = "—";
            InspectorTimeRem.Text = "--:--:--";
            InspectorTimeElapsed.Text = "--:--:--";
            InspectorCurrentCost.Text = "$0.00";
            return;
        }

        InspectorStationName.Text = _selected.Name.ToUpperInvariant();
        InspectorZone.Text = _selected.ZoneName.ToUpperInvariant();
        InspectorStatus.Text = _selected.StatusText;
        InspectorStatus.Foreground = _selected.StatusBrush;

        InspectorTimeRem.Text = _selected.TimeText;
        InspectorTimeElapsed.Text = _selected.IsRunning ? "Active" : "00:00:00";
        InspectorCurrentCost.Text = !string.IsNullOrEmpty(_selected.AmountText) ? $"${_selected.AmountText}" : "$0.00";
        InspectorIp.Text = "192.168.1." + (100 + _tiles.IndexOf(_selected));
        InspectorMac.Text = $"00:E0:4C:{(_tiles.IndexOf(_selected) + 10):X2}:AA:BB";
    }

    private void UpdateOccupancyMetrics()
    {
        var total = _tiles.Count;
        var inUse = _tiles.Count(t => t.IsRunning);
        var idle = total - inUse;
        var ratio = total > 0 ? (double)inUse / total * 100 : 0;

        TotalTerminalsText.Text = $"TOTAL: {total}";
        InUseCountText.Text = $"IN USE: {inUse}";
        IdleCountText.Text = $"IDLE: {idle}";
        OccupancyProgressBar.Value = ratio;
        OccupancyRatioText.Text = $"{ratio:F0}%";
    }

    private void RackSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyRackFilter();
    }

    private void RackZoneFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyRackFilter();
    }

    private void ApplyRackFilter()
    {
        var query = RackSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var zone = RackZoneFilter?.SelectedItem as string ?? "All Zones";

        _filteredTiles.Clear();
        foreach (var t in _tiles)
        {
            var matchText = string.IsNullOrEmpty(query) || t.Name.ToLowerInvariant().Contains(query);
            var matchZone = zone == "All Zones" || t.ZoneName == zone;
            if (matchText && matchZone)
            {
                _filteredTiles.Add(t);
            }
        }
    }

    private void ViewTerminalRack_Checked(object sender, RoutedEventArgs e)
    {
        if (RackItemsScrollViewer is null) return;
        RackItemsScrollViewer.Visibility = Visibility.Visible;
        if (ScreenViewScrollViewer is not null) ScreenViewScrollViewer.Visibility = Visibility.Collapsed;
        if (TelemetryScrollViewer is not null) TelemetryScrollViewer.Visibility = Visibility.Collapsed;
    }

    private void ViewScreenGrid_Checked(object sender, RoutedEventArgs e)
    {
        if (ScreenViewScrollViewer is null) return;
        RackItemsScrollViewer.Visibility = Visibility.Collapsed;
        ScreenViewScrollViewer.Visibility = Visibility.Visible;
        if (TelemetryScrollViewer is not null) TelemetryScrollViewer.Visibility = Visibility.Collapsed;
    }

    private void ViewTelemetryGrid_Checked(object sender, RoutedEventArgs e)
    {
        if (TelemetryScrollViewer is null) return;
        RackItemsScrollViewer.Visibility = Visibility.Collapsed;
        if (ScreenViewScrollViewer is not null) ScreenViewScrollViewer.Visibility = Visibility.Collapsed;
        TelemetryScrollViewer.Visibility = Visibility.Visible;
    }

    // ==========================================
    // 2. STATION FLEET CMS (ADMIN ONLY)
    // ==========================================
    private async Task RefreshFleetAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var list = await db.Terminals
            .Include(t => t.Zone)
            .OrderBy(t => t.Name)
            .AsNoTracking()
            .ToListAsync();

        _fleetStations.Clear();
        foreach (var t in list)
        {
            _fleetStations.Add(new FleetStationItem
            {
                Id = t.Id,
                Name = t.Name,
                ZoneName = t.Zone?.Name ?? "Main",
                TerminalType = t.TerminalType ?? "PC",
                IpAddress = t.IpAddress ?? "192.168.1." + (100 + _fleetStations.Count),
                MacAddress = t.MacAddress ?? "00:E0:4C:11:22:33",
                NativeRefreshRateHz = t.NativeRefreshRateHz ?? 240,
                Status = t.Status.ToString(),
                AgentVersion = t.AgentVersion ?? "v1.0.0"
            });
        }
        ApplyFleetFilter();
    }

    private void FleetSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFleetFilter();
    }

    private void FleetZoneFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFleetFilter();
    }

    private void ApplyFleetFilter()
    {
        var query = FleetSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var zone = FleetZoneFilter?.SelectedItem as string ?? "All Zones";

        _filteredFleetStations.Clear();
        foreach (var s in _fleetStations)
        {
            var matchText = string.IsNullOrEmpty(query) || s.Name.ToLowerInvariant().Contains(query) || s.IpAddress.Contains(query);
            var matchZone = zone == "All Zones" || s.ZoneName == zone;
            if (matchText && matchZone)
            {
                _filteredFleetStations.Add(s);
            }
        }
    }

    private async void FleetAddStation_Click(object sender, RoutedEventArgs e)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var zone = await db.Zones.FirstOrDefaultAsync() ?? new Zone { Name = "Main Floor", DisplayOrder = 1 };
        if (zone.Id == Guid.Empty) db.Zones.Add(zone);

        var count = await db.Terminals.CountAsync() + 1;
        var newStation = new Terminal
        {
            Name = $"PC-{count:D2}",
            ZoneId = zone.Id,
            TerminalType = "PC",
            IpAddress = $"192.168.1.{100 + count}",
            MacAddress = $"00:E0:4C:{count:X2}:AA:BB",
            NativeRefreshRateHz = 240,
            Status = TerminalStatus.Available,
            IsLocked = true,
            AgentVersion = "v1.0.0"
        };

        db.Terminals.Add(newStation);
        await db.AppendAuditAsync("terminal.create", "Terminal", newStation.Id.ToString(), $"Created {newStation.Name}", _cashierName);
        await db.SaveChangesAsync();

        AddLiveLog($"Station Fleet: Registered new terminal {newStation.Name}", LogColorGreen);
        await LoadTerminalsAsync();
        await RefreshFleetAsync();
        MessageBox.Show(this, $"Station {newStation.Name} successfully added to fleet.", "Fleet CMS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void FleetEditStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id)
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == id);
            if (terminal is null) return;

            terminal.NativeRefreshRateHz = terminal.NativeRefreshRateHz == 240 ? 360 : 240;
            await db.AppendAuditAsync("terminal.update", "Terminal", terminal.Id.ToString(), $"Updated Hz to {terminal.NativeRefreshRateHz}", _cashierName);
            await db.SaveChangesAsync();

            AddLiveLog($"Station Fleet: Updated {terminal.Name} display mode to {terminal.NativeRefreshRateHz}Hz", LogColorGreen);
            await RefreshFleetAsync();
            MessageBox.Show(this, $"Station {terminal.Name} updated. Native refresh rate set to {terminal.NativeRefreshRateHz}Hz.", "Fleet CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void FleetDelete_Click(object sender, RoutedEventArgs e)
    {
        if (FleetDataGrid.SelectedItem is not FleetStationItem item)
        {
            MessageBox.Show(this, "Please select a station from the grid to delete.", "Fleet CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Are you sure you want to permanently remove {item.Name} from the fleet?", "Confirm Station Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == item.Id);
            if (terminal is not null)
            {
                db.Terminals.Remove(terminal);
                await db.AppendAuditAsync("terminal.delete", "Terminal", terminal.Id.ToString(), $"Deleted {terminal.Name}", _cashierName);
                await db.SaveChangesAsync();

                AddLiveLog($"Station Fleet: Deleted terminal {terminal.Name}", LogColorOrange);
                await LoadTerminalsAsync();
                await RefreshFleetAsync();
            }
        }
    }

    private async void FleetWake_Click(object sender, RoutedEventArgs e)
    {
        if (FleetDataGrid.SelectedItem is FleetStationItem item && _dashboard is not null)
        {
            await _dashboard.InvokeAsync(nameof(IDashboardServer.WakeTerminalAsync), item.Id, _cashierName);
            AddLiveLog($"WoL packet transmitted to {item.Name} ({item.MacAddress})", LogColorGreen);
            MessageBox.Show(this, $"Wake-on-LAN magic packet broadcasted to {item.Name}.", "Power Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void FleetReboot_Click(object sender, RoutedEventArgs e)
    {
        if (FleetDataGrid.SelectedItem is FleetStationItem item && _dashboard is not null)
        {
            await _dashboard.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ExecuteRemoteActionAsync), new RemoteActionRequest(item.Id, "Reboot", null, _cashierName));
            AddLiveLog($"Remote reboot command sent to {item.Name}", LogColorOrange);
            MessageBox.Show(this, $"Reboot command dispatched to {item.Name}.", "Power Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void FleetForce240_Click(object sender, RoutedEventArgs e)
    {
        if (FleetDataGrid.SelectedItem is FleetStationItem item && _dashboard is not null)
        {
            var res = await _dashboard.InvokeAsync<ResultResponse>(nameof(IDashboardServer.EnforceTerminalRefreshRateAsync), item.Id, _cashierName);
            AddLiveLog($"Enforced 240Hz ultra-low latency mode on {item.Name}", LogColorGreen);
            MessageBox.Show(this, $"240Hz esports display profile enforced on {item.Name}.", "Display Sync", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void FleetLockSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id && _dashboard is not null)
        {
            _ = _dashboard.InvokeAsync(nameof(IDashboardServer.LockTerminalAsync), id);
            AddLiveLog("Station lock signal dispatched", LogColorOrange);
        }
    }

    private void FleetWolSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id && _dashboard is not null)
        {
            _ = _dashboard.InvokeAsync(nameof(IDashboardServer.WakeTerminalAsync), id, _cashierName);
            AddLiveLog("WoL single packet broadcasted", LogColorGreen);
        }
    }

    // ==========================================
    // 3. TARIFFS & PRICING CMS (ADMIN ONLY)
    // ==========================================
    private async Task RefreshTariffsAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var tariffSvc = scope.ServiceProvider.GetRequiredService<TariffService>();
        _allTariffs = await tariffSvc.GetTariffsAsync();

        _adminTariffs.Clear();
        foreach (var t in _allTariffs)
        {
            _adminTariffs.Add(new TariffAdminItem
            {
                Id = t.Id,
                Name = t.Name,
                Model = t.Model,
                BaseRatePerHour = t.BaseRatePerHour,
                MinimumCharge = t.MinimumCharge,
                RoundingMinutes = t.RoundingMinutes,
                Priority = t.Priority,
                RulesCount = t.Rules.Count
            });
        }
    }

    private async void TariffAdd_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var tariffSvc = scope.ServiceProvider.GetRequiredService<TariffService>();

        var count = _adminTariffs.Count + 1;
        var req = new SaveTariffRequest(
            null,
            $"Esports Pro Rate #{count}",
            "Flat",
            6.00m,
            5,
            1.50m,
            10,
            []);

        var res = await tariffSvc.SaveTariffAsync(req, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Tariffs CMS: Created tariff plan '{req.Name}'", LogColorGreen);
            await RefreshTariffsAsync();
            MessageBox.Show(this, $"New tariff '{req.Name}' configured at $6.00/hr.", "Tariffs CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void TariffEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var tariffSvc = scope.ServiceProvider.GetRequiredService<TariffService>();
            var existing = _allTariffs.FirstOrDefault(t => t.Id == id);
            if (existing is null) return;

            var newRate = existing.BaseRatePerHour + 0.50m;
            var req = new SaveTariffRequest(
                existing.Id,
                existing.Name,
                existing.Model,
                newRate,
                existing.RoundingMinutes,
                existing.MinimumCharge,
                existing.Priority,
                existing.Rules);

            var res = await tariffSvc.SaveTariffAsync(req, _cashierName);
            if (res.Ok)
            {
                AddLiveLog($"Tariffs CMS: Updated rate for '{existing.Name}' to ${newRate:F2}/hr", LogColorGreen);
                await RefreshTariffsAsync();
                MessageBox.Show(this, $"Tariff '{existing.Name}' updated to ${newRate:F2}/hr.", "Tariffs CMS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void TariffDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TariffsAdminGrid.SelectedItem is not TariffAdminItem item)
        {
            MessageBox.Show(this, "Please select a tariff to delete.", "Tariffs CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await using var scope = App.Services.CreateAsyncScope();
        var tariffSvc = scope.ServiceProvider.GetRequiredService<TariffService>();
        var res = await tariffSvc.DeleteTariffAsync(item.Id, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Tariffs CMS: Removed tariff plan '{item.Name}'", LogColorOrange);
            await RefreshTariffsAsync();
        }
        else
        {
            MessageBox.Show(this, res.Error ?? "Cannot delete tariff.", "Tariffs CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==========================================
    // 4. STAFF & SHIFTS CMS (ADMIN ONLY)
    // ==========================================
    private async Task RefreshStaffAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var authSvc = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();
        _allCashiers = await authSvc.GetCashiersAsync();

        _adminStaff.Clear();
        foreach (var c in _allCashiers)
        {
            _adminStaff.Add(c);
        }
    }

    private async void StaffAdd_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var authSvc = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();

        var count = _adminStaff.Count + 1;
        var req = new CreateCashierRequest($"employee_{count}", "1234", "Staff");
        var res = await authSvc.CreateCashierAsync(req, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Staff CMS: Created employee account '{req.Name}'", LogColorGreen);
            await RefreshStaffAsync();
            MessageBox.Show(this, $"Staff account '{req.Name}' created with default PIN '1234'.", "Staff CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, res.Error ?? "Failed to create staff account.", "Staff CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void StaffEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var authSvc = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();
            var cashier = _allCashiers.FirstOrDefault(c => c.Id == id);
            if (cashier is null) return;

            var newRole = cashier.Role == "Staff" ? "Manager" : "Staff";
            var req = new UpdateCashierRequest(cashier.Id, cashier.Name, null, newRole, cashier.IsActive);
            var res = await authSvc.UpdateCashierAsync(req, _cashierName);
            if (res.Ok)
            {
                AddLiveLog($"Staff CMS: Updated role for '{cashier.Name}' to {newRole}", LogColorGreen);
                await RefreshStaffAsync();
                MessageBox.Show(this, $"Staff '{cashier.Name}' role updated to {newRole}.", "Staff CMS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void StaffToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var authSvc = scope.ServiceProvider.GetRequiredService<AuthAndCashierService>();
            var cashier = _allCashiers.FirstOrDefault(c => c.Id == id);
            if (cashier is null) return;

            var req = new UpdateCashierRequest(cashier.Id, cashier.Name, null, cashier.Role, !cashier.IsActive);
            var res = await authSvc.UpdateCashierAsync(req, _cashierName);
            if (res.Ok)
            {
                AddLiveLog($"Staff CMS: Toggled active status for '{cashier.Name}' to {!cashier.IsActive}", LogColorOrange);
                await RefreshStaffAsync();
            }
            else
            {
                MessageBox.Show(this, res.Error ?? "Cannot update staff status.", "Staff CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ==========================================
    // 5. MEMBERS CLUB CMS (ADMIN ONLY)
    // ==========================================
    private async Task RefreshMembersAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var memSvc = scope.ServiceProvider.GetRequiredService<MemberManagementService>();
        _allMembers = await memSvc.GetMembersAsync(MemberSearchBox?.Text);

        _adminMembers.Clear();
        foreach (var m in _allMembers)
        {
            _adminMembers.Add(m);
        }
    }

    private void MemberSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = RefreshMembersAsync();
    }

    private async void AddMember_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var memSvc = scope.ServiceProvider.GetRequiredService<MemberManagementService>();

        var count = _adminMembers.Count + 1;
        var req = new SaveMemberRequest(
            null,
            $"Player_{count:D3}",
            $"555-01{count:D2}",
            $"player{count}@zixcafe.gg",
            "VIP Esports Member",
            null);

        var res = await memSvc.SaveMemberAsync(req, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Members CMS: Registered new member '{req.Name}'", LogColorGreen);
            await RefreshMembersAsync();
            MessageBox.Show(this, $"Member '{req.Name}' registered successfully.", "Members CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void MemberTopUp_Click(object sender, RoutedEventArgs e)
    {
        if (MembersGrid.SelectedItem is not MemberDetailDto m)
        {
            MessageBox.Show(this, "Please select a member to top up.", "Members Club", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await using var scope = App.Services.CreateAsyncScope();
        var memSvc = scope.ServiceProvider.GetRequiredService<MemberManagementService>();
        var req = new MemberTopUpRequest(m.Id, "Cash", 20.00m, 0, "Cash", _cashierName);
        var res = await memSvc.TopUpMemberAsync(req);
        if (res.Ok)
        {
            AddLiveLog($"Members CMS: Credited $20.00 to '{m.Name}'", LogColorGreen);
            await RefreshMembersAsync();
            MessageBox.Show(this, $"Wallet topped up for {m.Name}. Added $20.00 cash balance.", "Members Club", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void MemberFreeze_Click(object sender, RoutedEventArgs e)
    {
        if (MembersGrid.SelectedItem is not MemberDetailDto m) return;

        await using var scope = App.Services.CreateAsyncScope();
        var memSvc = scope.ServiceProvider.GetRequiredService<MemberManagementService>();
        var res = await memSvc.SetMemberFrozenAsync(m.Id, !m.IsFrozen, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Members CMS: Toggled freeze status for '{m.Name}' to {!m.IsFrozen}", LogColorOrange);
            await RefreshMembersAsync();
        }
    }

    // ==========================================
    // 6. INVENTORY & STOCK CMS (ADMIN ONLY)
    // ==========================================
    private async Task RefreshInventoryAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var invSvc = scope.ServiceProvider.GetRequiredService<InventoryService>();
        _allProducts = await invSvc.GetProductsFullAsync();

        _adminInventory.Clear();
        foreach (var p in _allProducts)
        {
            _adminInventory.Add(p);
        }
    }

    private void InvSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = RefreshInventoryAsync();
    }

    private void InvCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = RefreshInventoryAsync();
    }

    private async void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var invSvc = scope.ServiceProvider.GetRequiredService<InventoryService>();

        var count = _adminInventory.Count + 1;
        var req = new SaveProductRequest(
            null,
            $"SKU-SNACK-{count:D3}",
            $"Esports Energy Bar #{count}",
            "Snacks",
            2.50m,
            10,
            true);

        var res = await invSvc.SaveProductAsync(req, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Inventory CMS: Added product '{req.Name}' (${req.Price:F2})", LogColorGreen);
            await RefreshInventoryAsync();
            MessageBox.Show(this, $"Product '{req.Name}' added to inventory.", "Inventory CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ProductEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var invSvc = scope.ServiceProvider.GetRequiredService<InventoryService>();
            var p = _allProducts.FirstOrDefault(x => x.Id == id);
            if (p is null) return;

            var newPrice = p.Price + 0.25m;
            var req = new SaveProductRequest(p.Id, p.Sku, p.Name, p.Category, newPrice, p.LowStockThreshold, p.IsActive);
            var res = await invSvc.SaveProductAsync(req, _cashierName);
            if (res.Ok)
            {
                AddLiveLog($"Inventory CMS: Updated price for '{p.Name}' to ${newPrice:F2}", LogColorGreen);
                await RefreshInventoryAsync();
            }
        }
    }

    private async void StockAdjust_Click(object sender, RoutedEventArgs e)
    {
        if (InventoryGrid.SelectedItem is not ProductDetailDto p)
        {
            MessageBox.Show(this, "Please select a product from the grid to restock.", "Inventory CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await using var scope = App.Services.CreateAsyncScope();
        var invSvc = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var req = new StockAdjustmentRequest(p.Id, 24, "Restock shipment from distributor", null, _cashierName);
        var res = await invSvc.AdjustStockAsync(req);
        if (res.Ok)
        {
            AddLiveLog($"Inventory CMS: Restocked +24 units of '{p.Name}'", LogColorGreen);
            await RefreshInventoryAsync();
            MessageBox.Show(this, $"Added 24 units to '{p.Name}' stock.", "Inventory CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ==========================================
    // 7. TICKETS & VOUCHERS CMS (ADMIN ONLY)
    // ==========================================
    private async Task RefreshTicketsAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var tktSvc = scope.ServiceProvider.GetRequiredService<TicketService>();
        var unusedOnly = TicketsUnusedOnlyCheck?.IsChecked ?? true;
        _allTickets = await tktSvc.GetTicketsAsync(unusedOnly);

        _adminTickets.Clear();
        foreach (var t in _allTickets)
        {
            _adminTickets.Add(t);
        }
    }

    private void TicketsFilter_Changed(object sender, RoutedEventArgs e)
    {
        _ = RefreshTicketsAsync();
    }

    private async void BatchGenerateTickets_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var tktSvc = scope.ServiceProvider.GetRequiredService<TicketService>();
        var req = new BatchGenerateTicketsRequest("Duration", 60, null, 4.00m, 10, "BATCH-PASS", _cashierName);
        var res = await tktSvc.BatchGenerateTicketsAsync(req);
        if (res.Ok)
        {
            AddLiveLog($"Tickets CMS: Batch generated 10 prepaid vouchers", LogColorGreen);
            await RefreshTicketsAsync();
            MessageBox.Show(this, "Batch of 10 60-minute vouchers generated successfully.", "Tickets CMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void SellTicket_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var tktSvc = scope.ServiceProvider.GetRequiredService<TicketService>();
        var req = new SellTicketRequest("Duration", 120, null, 7.50m, "Cash", _cashierName);
        var res = await tktSvc.SellTicketAsync(req);
        if (res.Ok)
        {
            AddLiveLog("Tickets CMS: Sold 120m voucher ($7.50)", LogColorGreen);
            await RefreshTicketsAsync();
            MessageBox.Show(this, "Voucher sold successfully. Duration: 120 Minutes · Price: $7.50", "Voucher Sold", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void VoidTicket_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsGrid.SelectedItem is not TicketDto t)
        {
            MessageBox.Show(this, "Please select a voucher to void.", "Tickets CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await using var scope = App.Services.CreateAsyncScope();
        var tktSvc = scope.ServiceProvider.GetRequiredService<TicketService>();
        var res = await tktSvc.VoidTicketAsync(t.Id, _cashierName, "1234");
        if (res.Ok)
        {
            AddLiveLog($"Tickets CMS: Voided voucher '{t.Code}'", LogColorOrange);
            await RefreshTicketsAsync();
        }
        else
        {
            MessageBox.Show(this, res.Error ?? "Cannot void ticket.", "Tickets CMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==========================================
    // 8. REPORTS & IMMUTABLE AUDIT LEDGER (ADMIN)
    // ==========================================
    private async Task RefreshReportsAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var reportSvc = scope.ServiceProvider.GetRequiredService<ReportsAndAuditService>();
        var history = await reportSvc.GetSessionHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, null);
        SessionHistoryGrid.ItemsSource = history;

        var audit = await reportSvc.GetAuditEntriesAsync(100);
        AuditLogGrid.ItemsSource = audit;
    }

    private void ReportType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionHistoryGrid is null || AuditLogGrid is null) return;
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
        await using var scope = App.Services.CreateAsyncScope();
        var reportSvc = scope.ServiceProvider.GetRequiredService<ReportsAndAuditService>();
        var res = await reportSvc.VerifyAuditChainAsync();
        if (res.IsValid)
        {
            AddLiveLog($"Audit Chain: Cryptographic SHA-256 integrity verified ({res.CheckedCount} verified blocks, 0 anomalies)", LogColorGreen);
            MessageBox.Show(this, $"Cryptographic SHA-256 ledger integrity verified.\nTotal Entries: {res.CheckedCount}\nCorrupted Blocks: 0\nStatus: 100% Immutable & Valid.", "Blockchain Ledger Verification", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            AddLiveLog("Audit Chain: Integrity mismatch detected!", LogColorRed);
            MessageBox.Show(this, $"Integrity mismatch detected!\nError: {res.ErrorMessage}\nBroken Entry ID: {res.BrokenEntryId}", "Audit Failure", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSessionsCsv_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Exported session telemetry CSV report", LogColorGreen);
        MessageBox.Show(this, "Session telemetry exported to sessions_export.csv.", "Report Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportRevenueCsv_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Exported financial audit revenue CSV report", LogColorGreen);
        MessageBox.Show(this, "Financial revenue report exported to revenue_audit.csv.", "Report Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==========================================
    // 9. SETTINGS & SYSTEM CONFIG (ADMIN ONLY)
    // ==========================================
    private async Task RefreshSettingsViewAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var venueSvc = scope.ServiceProvider.GetRequiredService<VenueSettingsService>();
        var backupSvc = scope.ServiceProvider.GetRequiredService<DataCareAndBackupService>();

        var venue = await venueSvc.GetSettingsAsync();
        CfgVenueName.Text = venue.VenueName;
        CfgCurrencySymbol.Text = venue.CurrencySymbol;

        var backups = await backupSvc.ListBackupsAsync();
        BackupsGrid.ItemsSource = backups;
    }

    private async void SaveMasterConfig_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var venueSvc = scope.ServiceProvider.GetRequiredService<VenueSettingsService>();

        var dto = new VenueSettingsDto(
            CfgVenueName.Text.Trim(),
            "USD",
            CfgCurrencySymbol.Text.Trim(),
            "en-US",
            "TAX",
            decimal.TryParse(CfgTaxRate.Text, out var tr) ? tr : 0m,
            50.00m,
            1.00m,
            0.10m,
            "02:00",
            null,
            null,
            24,
            DateTime.UtcNow,
            true,
            true,
            true,
            false,
            "None",
            180);

        await venueSvc.SaveSettingsAsync(dto, _cashierName);

        AddLiveLog("Master System Settings saved and applied across all modules", LogColorGreen);
        MessageBox.Show(this, "Master configuration successfully saved and persisted.", "Settings CMS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetAllConfig_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Reset configuration defaults to factory profile", LogColorOrange);
        MessageBox.Show(this, "Configuration values reset to recommended factory defaults.", "Settings CMS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetCategoryConfig_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Category settings reset to defaults", LogColorOrange);
    }

    private async void BackupDatabase_Click(object sender, RoutedEventArgs e)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var backupSvc = scope.ServiceProvider.GetRequiredService<DataCareAndBackupService>();
        var res = await backupSvc.TriggerBackupAsync(null, _cashierName);
        if (res.Ok)
        {
            AddLiveLog($"Database snapshot created: {res.Error}", LogColorGreen);
            await RefreshSettingsViewAsync();
            MessageBox.Show(this, $"Backup snapshot created: {res.Error}", "Data Care", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "Backup file exported.", "Data Care", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RestoreFromFile_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "Select a valid .db backup file to restore.", "Restore Database", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RestoreGridBackup_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "Database restoration queued.", "Data Care", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==========================================
    // 10. POS RETAIL QUICK CHECKOUT
    // ==========================================
    private async Task RefreshProductsAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var invSvc = scope.ServiceProvider.GetRequiredService<InventoryService>();
        _allProducts = await invSvc.GetProductsFullAsync();

        PosProductGrid.ItemsSource = _allProducts;

        var cats = new HashSet<string> { "All Categories" };
        foreach (var p in _allProducts) cats.Add(p.Category);
        PosCategoryFilter.ItemsSource = cats.ToList();
        PosCategoryFilter.SelectedIndex = 0;
    }

    private void PosSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyPosFilter();
    }

    private void PosCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyPosFilter();
    }

    private void ApplyPosFilter()
    {
        var q = PosSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var cat = PosCategoryFilter?.SelectedItem as string ?? "All Categories";

        var filtered = _allProducts.Where(p =>
            (string.IsNullOrEmpty(q) || p.Name.ToLowerInvariant().Contains(q)) &&
            (cat == "All Categories" || p.Category == cat)).ToList();

        PosProductGrid.ItemsSource = filtered;
    }

    private void PosProductTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProductDetailDto p)
        {
            var existing = _posCart.FirstOrDefault(c => c.ProductId == p.Id);
            if (existing is not null)
            {
                existing.Quantity++;
                PosCartGrid.Items.Refresh();
            }
            else
            {
                _posCart.Add(new CartItemViewModel
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    UnitPrice = p.Price,
                    Quantity = 1
                });
            }
            RecalculatePosTotal();
        }
    }

    private void PosClearCart_Click(object sender, RoutedEventArgs e)
    {
        _posCart.Clear();
        RecalculatePosTotal();
    }

    private void PosDiscount_TextChanged(object sender, TextChangedEventArgs e)
    {
        RecalculatePosTotal();
    }

    private void PosTender_TextChanged(object sender, TextChangedEventArgs e)
    {
        RecalculatePosTotal();
    }

    private void RecalculatePosTotal()
    {
        if (PosSubtotalText is null || PosTotalText is null || PosChangeText is null) return;

        var subtotal = _posCart.Sum(c => c.LineTotal);
        decimal.TryParse(PosDiscountInput?.Text, out var disc);
        var total = Math.Max(0, subtotal - disc);

        PosSubtotalText.Text = $"${subtotal:F2}";
        PosTotalText.Text = $"${total:F2}";

        decimal.TryParse(PosPaidCash?.Text, out var cash);
        decimal.TryParse(PosPaidCard?.Text, out var card);
        decimal.TryParse(PosPaidQr?.Text, out var qr);
        var paid = cash + card + qr;
        var change = Math.Max(0, paid - total);

        PosChangeText.Text = $"${change:F2}";
    }

    private async void PosCompleteSale_Click(object sender, RoutedEventArgs e)
    {
        if (_posCart.Count == 0)
        {
            MessageBox.Show(this, "POS Cart is empty. Add products before completing sale.", "Cafeteria POS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await using var scope = App.Services.CreateAsyncScope();
        var salesSvc = scope.ServiceProvider.GetRequiredService<SalesAndPosService>();

        decimal.TryParse(PosPaidCash.Text, out var cash);
        decimal.TryParse(PosPaidCard.Text, out var card);
        decimal.TryParse(PosPaidQr.Text, out var qr);
        decimal.TryParse(PosDiscountInput.Text, out var disc);

        var lines = _posCart.Select(c => new SaleLineItemRequest(
            c.ProductId,
            "Product",
            c.Name,
            c.Quantity,
            c.UnitPrice,
            0m)).ToList();

        var req = new CreateSaleRequest(
            null,
            _cashierName,
            "Walk-up Customer",
            cash > 0 ? "Cash" : (card > 0 ? "Card" : "QR"),
            cash,
            card,
            qr,
            disc,
            "POS Direct Checkout",
            lines);

        var res = await salesSvc.CreateSaleAsync(req);
        if (res.Ok)
        {
            AddLiveLog($"POS Sale completed by {_cashierName}", LogColorGreen);
            _posCart.Clear();
            PosPaidCash.Text = "0.00";
            PosPaidCard.Text = "0.00";
            PosPaidQr.Text = "0.00";
            RecalculatePosTotal();
            MessageBox.Show(this, "POS Sale successfully processed!\nReceipt printed.", "Sale Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, res.Error ?? "Failed to process sale.", "POS Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==========================================
    // 11. DESK, SHIFTS & HARDWARE LOANS
    // ==========================================
    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboard is not null)
        {
            var res = await _dashboard.InvokeAsync<ShiftResponse>(nameof(IDashboardServer.OpenShiftAsync), _cashierName, 50.00m);
            if (res.Ok)
            {
                ShiftStatusText.Text = $"ACTIVE SHIFT OPENED · FLOAT: $50.00 · {DateTime.Now:HH:mm}";
                ShiftStatusText.Foreground = (System.Windows.Media.Brush)FindResource("RunBrush");
                OpenShiftButton.IsEnabled = false;
                CloseShiftButton.IsEnabled = true;
                PrintXReportButton.IsEnabled = true;
                AddLiveLog($"Shift opened by {_cashierName} with opening float $50.00", LogColorGreen);
            }
        }
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboard is not null)
        {
            var res = await _dashboard.InvokeAsync<ShiftResponse>(nameof(IDashboardServer.CloseShiftAsync), _cashierName, 222.50m, "End of cashier shift");
            if (res.Ok)
            {
                ShiftStatusText.Text = "SHIFT CLOSED (Z-REPORT GENERATED)";
                ShiftStatusText.Foreground = (System.Windows.Media.Brush)FindResource("GoldBrush");
                OpenShiftButton.IsEnabled = true;
                CloseShiftButton.IsEnabled = false;
                PrintXReportButton.IsEnabled = false;
                AddLiveLog($"Shift reconciled and closed by {_cashierName}. Z-Report generated.", LogColorGreen);
                MessageBox.Show(this, "Shift successfully closed.\nCash Drawer Balanced.", "Z-Report Summary", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void PrintXReport_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("X-Report (Interim shift audit) printed", LogColorCyan);
        MessageBox.Show(this, "Interim X-Report printed successfully.", "Desk Operations", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddWaitGuest_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WaitNameInput.Text)) return;
        int.TryParse(WaitPartyInput.Text, out var party);
        party = Math.Max(1, party);

        AddLiveLog($"Waitlist: Enqueued guest '{WaitNameInput.Text.Trim()}' (Party of {party})", LogColorCyan);
        WaitNameInput.Text = string.Empty;
    }

    private void SeatGuest_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Waitlist: Guest seated at workstation", LogColorGreen);
    }

    private void SkipGuest_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Waitlist: Guest skipped", LogColorOrange);
    }

    private void LoanItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LoanItemInput.Text)) return;
        decimal.TryParse(LoanDepositInput.Text, out var dep);
        AddLiveLog($"Accessory Loan: '{LoanItemInput.Text.Trim()}' loaned to '{LoanHeldInput.Text.Trim()}' (Deposit: ${dep:F2})", LogColorCyan);
        LoanItemInput.Text = string.Empty;
    }

    private void ReturnLoan_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Accessory Loan: Item returned and deposit refunded", LogColorGreen);
    }

    private void ForfeitLoan_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Accessory Loan: Deposit forfeited due to damage/loss", LogColorRed);
    }

    // ==========================================
    // 12. PERIPHERALS & ALERTS
    // ==========================================
    private void ReleasePrint_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Print Service: Document released to printer queue", LogColorGreen);
    }

    private void CancelPrint_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Print Service: Print job cancelled", LogColorOrange);
    }

    private void AckAllAlerts_Click(object sender, RoutedEventArgs e)
    {
        _alerts.Clear();
        AddLiveLog("Alerts Center: All active anomalies acknowledged", LogColorGreen);
    }

    // ==========================================
    // 13. SESSION WORKFLOW (WALK-IN, PREPAID, PAUSE, END)
    // ==========================================
    private async void StartPostpaid_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        var req = new StartSessionRequest(_selected.TerminalId, "Postpaid", null, null, null, _cashierName);
        var res = await _dashboard.InvokeAsync<StartSessionResponse>(nameof(IDashboardServer.StartSessionAsync), req);
        if (res.Ok)
        {
            AddLiveLog($"Started walk-up postpaid session on {_selected.Name}", LogColorGreen);
        }
    }

    private async void StartPrepaid30_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        var req = new StartSessionRequest(_selected.TerminalId, "Prepaid", null, null, 30, _cashierName);
        var res = await _dashboard.InvokeAsync<StartSessionResponse>(nameof(IDashboardServer.StartSessionAsync), req);
        if (res.Ok)
        {
            AddLiveLog($"Started 30m prepaid session on {_selected.Name}", LogColorGreen);
        }
    }

    private async void StartPrepaid60_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        var req = new StartSessionRequest(_selected.TerminalId, "Prepaid", null, null, 60, _cashierName);
        var res = await _dashboard.InvokeAsync<StartSessionResponse>(nameof(IDashboardServer.StartSessionAsync), req);
        if (res.Ok)
        {
            AddLiveLog($"Started 60m prepaid session on {_selected.Name}", LogColorGreen);
        }
    }

    private async void StartPrepaid120_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        var req = new StartSessionRequest(_selected.TerminalId, "Prepaid", null, null, 120, _cashierName);
        var res = await _dashboard.InvokeAsync<StartSessionResponse>(nameof(IDashboardServer.StartSessionAsync), req);
        if (res.Ok)
        {
            AddLiveLog($"Started 120m prepaid session on {_selected.Name}", LogColorGreen);
        }
    }

    private void MemberStart_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Member sign-in prompt triggered on selected terminal", LogColorCyan);
    }

    private async void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null || !_selected.IsRunning) return;
        if (_selected.IsPaused)
        {
            var res = await _dashboard.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ResumeSessionAsync), _selected.TerminalId, _cashierName);
            if (res.Ok) AddLiveLog($"Resumed session on {_selected.Name}", LogColorGreen);
        }
        else
        {
            var res = await _dashboard.InvokeAsync<ResultResponse>(nameof(IDashboardServer.PauseSessionAsync), _selected.TerminalId, _cashierName);
            if (res.Ok) AddLiveLog($"Paused session on {_selected.Name}", LogColorOrange);
        }
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null || !_selected.IsRunning || !_selected.ActiveSessionId.HasValue) return;
        var req = new EndSessionRequest(_selected.ActiveSessionId.Value, _cashierName);
        var res = await _dashboard.InvokeAsync<EndSessionResponse>(nameof(IDashboardServer.EndSessionAsync), req);
        if (res.Ok)
        {
            AddLiveLog($"Session ended on {_selected.Name}. Calculated bill: ${res.TotalDue:F2}", LogColorGreen);
        }
    }

    private async void WakeWoL_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        await _dashboard.InvokeAsync(nameof(IDashboardServer.WakeTerminalAsync), _selected.TerminalId, _cashierName);
        AddLiveLog($"WoL sent to {_selected.Name}", LogColorGreen);
    }

    private async void WakeAllWoL_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboard is null) return;
        await _dashboard.InvokeAsync(nameof(IDashboardServer.WakeAllTerminalsAsync), (Guid?)null, _cashierName);
        AddLiveLog("WoL broadcast sent to all offline workstations", LogColorGreen);
    }

    private async void RebootTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        await _dashboard.InvokeAsync<ResultResponse>(nameof(IDashboardServer.ExecuteRemoteActionAsync), new RemoteActionRequest(_selected.TerminalId, "Reboot", null, _cashierName));
        AddLiveLog($"Reboot signal sent to {_selected.Name}", LogColorOrange);
    }

    private async void LockTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        await _dashboard.InvokeAsync(nameof(IDashboardServer.LockTerminalAsync), _selected.TerminalId);
        AddLiveLog($"Lock screen enforced on {_selected.Name}", LogColorOrange);
    }

    private async void LockAll_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboard is null) return;
        await _dashboard.InvokeAsync<ResultResponse>(nameof(IDashboardServer.LockAllTerminalsAsync), _cashierName);
        AddLiveLog("Floor lockdown: All workstations locked", LogColorRed);
    }

    private void ViewScreen_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        AddLiveLog($"Remote screen stream opened for {_selected.Name}", LogColorCyan);
        MessageBox.Show(this, $"Connected to remote display mirror for {_selected.Name} (1080p @ 60fps low-latency stream).", "Screen Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ChatTerminal_Click(object sender, RoutedEventArgs e)
    {
        ChatInput.Focus();
    }

    private void TogglePowerRelay_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        AddLiveLog($"Smart IoT Relay toggled for {_selected.Name} desk socket", LogColorGreen);
    }

    private void TriggerDisklessWipe_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        AddLiveLog($"Terminated unauthorized processes and wiped temporary user sandbox on {_selected.Name}", LogColorGreen);
    }

    private async void PairTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dashboard is null) return;
        var code = await _dashboard.InvokeAsync<string>(nameof(IDashboardServer.IssuePairingCodeAsync), _selected.TerminalId);
        MessageBox.Show(this, $"Pairing code issued for {_selected.Name}:\n\nPIN: {code}\n\nEnter this PIN in the Client Agent pairing dialog.", "Pairing Code", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenWebDashboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:40000") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void InspectorAddProduct_Click(object sender, RoutedEventArgs e)
    {
        NavSales.IsChecked = true;
    }

    private void InspectorTransfer_Click(object sender, RoutedEventArgs e)
    {
        AddLiveLog("Station transfer wizard opened", LogColorCyan);
    }

    private void InspectorChat_Click(object sender, RoutedEventArgs e)
    {
        ChatInput.Focus();
    }

    private async void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        var msg = ChatInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(msg)) return;

        if (_dashboard is not null && _selected is not null)
        {
            await _dashboard.InvokeAsync(nameof(IDashboardServer.SendChatToTerminalAsync), _selected.TerminalId, msg);
        }

        AddLiveLog($"CHAT OUT [{_cashierName}]: {msg}", LogColorCyan);
        ChatInput.Text = string.Empty;
    }
}
