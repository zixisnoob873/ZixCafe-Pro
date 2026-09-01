using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Hubs;

public class DashboardHub : Hub<IDashboardClient>, IDashboardServer
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly TerminalRegistry _registry;
    private readonly SessionService _sessions;
    private readonly DeskService _desk;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminals;
    private readonly AuthAndCashierService _auth;
    private readonly VenueSettingsService _venueSettings;
    private readonly TariffService _tariffs;
    private readonly SalesAndPosService _sales;
    private readonly TicketService _tickets;
    private readonly MemberManagementService _members;
    private readonly InventoryService _inventory;
    private readonly PeripheralMeteringService _peripherals;
    private readonly ReportsAndAuditService _reports;
    private readonly AlertsCenterService _alerts;
    private readonly RemoteOpsService _remoteOps;
    private readonly MaintenanceAndReservationService _maintenance;
    private readonly ChatHistoryService _chatHistory;
    private readonly DataCareAndBackupService _backup;
    private readonly HardwareIntegrityService _hardware;
    private readonly MasterConfigurationService _masterConfig;
    private readonly EnergyAndIoTHostService _energyIot;

    public DashboardHub(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        TerminalRegistry registry,
        SessionService sessions,
        DeskService desk,
        IHubContext<TerminalHub, ITerminalClient> terminals,
        AuthAndCashierService auth,
        VenueSettingsService venueSettings,
        TariffService tariffs,
        SalesAndPosService sales,
        TicketService tickets,
        MemberManagementService members,
        InventoryService inventory,
        PeripheralMeteringService peripherals,
        ReportsAndAuditService reports,
        AlertsCenterService alerts,
        RemoteOpsService remoteOps,
        MaintenanceAndReservationService maintenance,
        ChatHistoryService chatHistory,
        DataCareAndBackupService backup,
        HardwareIntegrityService hardware,
        MasterConfigurationService masterConfig,
        EnergyAndIoTHostService energyIot)
    {
        _dbFactory = dbFactory;
        _registry = registry;
        _sessions = sessions;
        _desk = desk;
        _terminals = terminals;
        _auth = auth;
        _venueSettings = venueSettings;
        _tariffs = tariffs;
        _sales = sales;
        _tickets = tickets;
        _members = members;
        _inventory = inventory;
        _peripherals = peripherals;
        _reports = reports;
        _alerts = alerts;
        _remoteOps = remoteOps;
        _maintenance = maintenance;
        _chatHistory = chatHistory;
        _backup = backup;
        _hardware = hardware;
        _masterConfig = masterConfig;
        _energyIot = energyIot;
    }

    public async Task SubscribeAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
        await PushRackSnapshotAsync();
    }

    public async Task RequestRackSnapshotAsync()
    {
        await PushRackSnapshotAsync();
    }

    public Task<string> IssuePairingCodeAsync(Guid terminalId)
        => Task.FromResult(_registry.IssuePairingCode(terminalId));

    public async Task<StartSessionResponse> StartSessionAsync(StartSessionRequest request)
        => await _sessions.StartAsync(request);

    public async Task<EndSessionResponse> EndSessionAsync(EndSessionRequest request)
        => await _sessions.EndAsync(request);

    public async Task LockTerminalAsync(Guid terminalId)
    {
        await _terminals.Clients.Group(TerminalGroups.Terminal(terminalId)).ShowLockScreen(DateTime.UtcNow);
        await _sessions.BroadcastStateAsync(terminalId);
    }

    public async Task SendChatToTerminalAsync(Guid terminalId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        var sentAt = DateTime.UtcNow;
        await _chatHistory.SaveChatAsync(terminalId, null, "Front desk", message, false);
        await _terminals.Clients.Group(TerminalGroups.Terminal(terminalId)).ChatMessage("Front desk", message, sentAt);
        await Clients.Others.ChatMessage(terminalId, "Front desk", message, sentAt);
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request)
        => _auth.LoginAsync(request);

    public Task<ResultResponse> PauseSessionAsync(Guid terminalId, string cashierName)
        => _sessions.PauseAsync(terminalId, cashierName);

    public Task<ResultResponse> ResumeSessionAsync(Guid terminalId, string cashierName)
        => _sessions.ResumeAsync(terminalId, cashierName);

    public Task<FindMemberResponse> FindMemberAsync(string query)
        => _sessions.FindMemberAsync(query);

    public Task<IReadOnlyList<ProductDto>> GetProductsAsync()
        => _sessions.GetProductsAsync();

    public Task<AddLineResponse> AddProductLineAsync(Guid sessionId, Guid productId, decimal quantity, string cashierName)
        => _sessions.AddProductLineAsync(sessionId, productId, quantity, cashierName);

    public Task<ShiftResponse> OpenShiftAsync(string cashierName, decimal openingFloat)
        => _desk.OpenShiftAsync(cashierName, openingFloat);

    public Task<ShiftDto?> GetCurrentShiftAsync()
        => _desk.GetCurrentShiftAsync();

    public Task<ShiftResponse> CloseShiftAsync(string cashierName, decimal countedDrawer, string? note)
        => _desk.CloseShiftAsync(cashierName, countedDrawer, note);

    public Task<IReadOnlyList<WaitlistEntryDto>> GetWaitlistAsync()
        => _desk.GetWaitlistAsync();

    public Task<WaitlistResponse> AddToWaitlistAsync(string guestName, int partySize, string? contact)
        => _desk.AddToWaitlistAsync(guestName, partySize, contact);

    public Task<StartSessionResponse> SeatWaitlistGuestAsync(Guid entryId, Guid terminalId, string cashierName)
        => _desk.SeatWaitlistGuestAsync(entryId, terminalId, cashierName);

    public Task<WaitlistResponse> SkipWaitlistEntryAsync(Guid entryId, string cashierName)
        => _desk.SkipWaitlistEntryAsync(entryId, cashierName);

    public Task<IReadOnlyList<LoanDto>> GetLoansAsync()
        => _desk.GetLoansAsync();

    public Task<LoanResponse> LoanItemAsync(string itemName, decimal deposit, string heldBy, Guid? sessionId)
        => _desk.LoanItemAsync(itemName, deposit, heldBy, sessionId);

    public Task<LoanResponse> ReturnLoanAsync(Guid loanId, string returnedTo, string cashierName, bool forfeited)
        => _desk.ReturnLoanAsync(loanId, returnedTo, cashierName, forfeited);

    public Task<ResultResponse> LockAllTerminalsAsync(string cashierName)
        => _desk.LockAllTerminalsAsync(cashierName);

    // Cashiers
    public Task<IReadOnlyList<CashierDto>> GetCashiersAsync()
        => _auth.GetCashiersAsync();

    public Task<ResultResponse> CreateCashierAsync(CreateCashierRequest request, string requestingCashier)
        => _auth.CreateCashierAsync(request, requestingCashier);

    public Task<ResultResponse> UpdateCashierAsync(UpdateCashierRequest request, string requestingCashier)
        => _auth.UpdateCashierAsync(request, requestingCashier);

    public async Task<ResultResponse> VerifyManagerPinAsync(string pin)
    {
        var ok = await _auth.VerifyManagerPinAsync(pin);
        return new ResultResponse(ok, ok ? null : "Invalid Manager PIN.");
    }

    // Venue Settings
    public Task<VenueSettingsDto> GetVenueSettingsAsync()
        => _venueSettings.GetSettingsDtoAsync();

    public Task<ResultResponse> SaveVenueSettingsAsync(VenueSettingsDto settings, string requestingCashier)
        => _venueSettings.SaveSettingsAsync(settings, requestingCashier);

    // Tariffs
    public Task<IReadOnlyList<TariffDto>> GetTariffsAsync()
        => _tariffs.GetTariffsAsync();

    public Task<ResultResponse> SaveTariffAsync(SaveTariffRequest request, string requestingCashier)
        => _tariffs.SaveTariffAsync(request, requestingCashier);

    public Task<ResultResponse> DeleteTariffAsync(Guid tariffId, string requestingCashier)
        => _tariffs.DeleteTariffAsync(tariffId, requestingCashier);

    // POS & Retail Sales
    public Task<ResultResponse> CreateSaleAsync(CreateSaleRequest request)
        => _sales.CreateSaleAsync(request);

    public Task<IReadOnlyList<SaleSummaryDto>> GetRecentSalesAsync(int limit)
        => _sales.GetRecentSalesAsync(limit);

    public Task<SaleDetailDto?> GetSaleDetailAsync(Guid saleId)
        => _sales.GetSaleDetailAsync(saleId);

    // Tickets
    public Task<IReadOnlyList<TicketDto>> GetTicketsAsync(bool unusedOnly)
        => _tickets.GetTicketsAsync(unusedOnly);

    public Task<ResultResponse> SellTicketAsync(SellTicketRequest request)
        => _tickets.SellTicketAsync(request);

    public Task<ResultResponse> BatchGenerateTicketsAsync(BatchGenerateTicketsRequest request)
        => _tickets.BatchGenerateTicketsAsync(request);

    public Task<ResultResponse> VoidTicketAsync(Guid ticketId, string cashierName, string managerPin)
        => _tickets.VoidTicketAsync(ticketId, cashierName, managerPin);

    // Members
    public Task<IReadOnlyList<MemberDetailDto>> GetMembersAsync(string? search)
        => _members.GetMembersAsync(search);

    public Task<MemberDetailDto?> GetMemberDetailAsync(Guid memberId)
        => _members.GetMemberDetailAsync(memberId);

    public Task<ResultResponse> SaveMemberAsync(SaveMemberRequest request, string requestingCashier)
        => _members.SaveMemberAsync(request, requestingCashier);

    public Task<ResultResponse> TopUpMemberAsync(MemberTopUpRequest request)
        => _members.TopUpMemberAsync(request);

    public Task<IReadOnlyList<MemberTransactionDto>> GetMemberTransactionsAsync(Guid memberId)
        => _members.GetMemberTransactionsAsync(memberId);

    public Task<IReadOnlyList<MemberTierDto>> GetMemberTiersAsync()
        => _members.GetMemberTiersAsync();

    public Task<ResultResponse> SetMemberFrozenAsync(Guid memberId, bool isFrozen, string requestingCashier)
        => _members.SetMemberFrozenAsync(memberId, isFrozen, requestingCashier);

    // Inventory
    public Task<IReadOnlyList<ProductDetailDto>> GetProductsFullAsync()
        => _inventory.GetProductsFullAsync();

    public Task<ResultResponse> SaveProductAsync(SaveProductRequest request, string requestingCashier)
        => _inventory.SaveProductAsync(request, requestingCashier);

    public Task<ResultResponse> AdjustStockAsync(StockAdjustmentRequest request)
        => _inventory.AdjustStockAsync(request);

    public Task<IReadOnlyList<StockMovementDto>> GetStockMovementsAsync(Guid? productId, int limit)
        => _inventory.GetStockMovementsAsync(productId, limit);

    // Print & USB
    public Task<IReadOnlyList<PrintJobDto>> GetPrintJobsAsync()
        => _peripherals.GetPrintJobsAsync();

    public Task<ResultResponse> ReleasePrintJobAsync(Guid printJobId, string paymentMethod, string cashierName)
        => _peripherals.ReleasePrintJobAsync(printJobId, paymentMethod, cashierName);

    public Task<ResultResponse> CancelPrintJobAsync(Guid printJobId, string reason, string cashierName)
        => _peripherals.CancelPrintJobAsync(printJobId, reason, cashierName);

    // Reports & Audit
    public Task<ShiftReportDto?> GetShiftReportAsync(Guid shiftId)
        => _reports.GetShiftReportAsync(shiftId);

    public Task<IReadOnlyList<DailyRevenueDto>> GetDailyRevenueReportAsync(DateTime fromDateUtc, DateTime toDateUtc)
        => _reports.GetDailyRevenueReportAsync(fromDateUtc, toDateUtc);

    public Task<IReadOnlyList<SessionHistoryDto>> GetSessionHistoryAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? terminalId)
        => _reports.GetSessionHistoryAsync(fromDateUtc, toDateUtc, terminalId);

    public Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync(int limit)
        => _reports.GetAuditEntriesAsync(limit);

    public Task<AuditVerificationResult> VerifyAuditChainAsync()
        => _reports.VerifyAuditChainAsync();

    // Alerts
    public Task<IReadOnlyList<AlertDto>> GetAlertsAsync()
        => _alerts.GetAlertsAsync();

    public Task<ResultResponse> AcknowledgeAlertAsync(Guid alertId, string cashierName)
        => _alerts.AcknowledgeAlertAsync(alertId, cashierName);

    public Task<ResultResponse> MuteAlertKindAsync(string kind, int minutes)
        => _alerts.MuteAlertKindAsync(kind, minutes);

    // Remote Ops
    public Task<ResultResponse> RequestScreenViewAsync(Guid terminalId, string requestingCashier)
        => _remoteOps.RequestScreenViewAsync(terminalId, requestingCashier);

    public Task<ResultResponse> ExecuteRemoteActionAsync(RemoteActionRequest request)
        => _remoteOps.ExecuteRemoteActionAsync(request);

    public Task<IReadOnlyList<ProhibitedAppDto>> GetProhibitedAppsAsync()
        => _remoteOps.GetProhibitedAppsAsync();

    public Task<ResultResponse> SaveProhibitedAppAsync(string match, string matchKind, bool killOnSight, string requestingCashier)
        => _remoteOps.SaveProhibitedAppAsync(match, matchKind, killOnSight, requestingCashier);

    public Task<ResultResponse> DeleteProhibitedAppAsync(Guid id, string requestingCashier)
        => _remoteOps.DeleteProhibitedAppAsync(id, requestingCashier);

    // Maintenance & Reservations
    public Task<ResultResponse> SetTerminalMaintenanceAsync(SetTerminalMaintenanceRequest request)
        => _maintenance.SetTerminalMaintenanceAsync(request);

    public Task<ResultResponse> ReserveTerminalAsync(ReserveTerminalRequest request)
        => _maintenance.ReserveTerminalAsync(request);

    public Task<ResultResponse> ReleaseReservationAsync(Guid terminalId, string cashierName)
        => _maintenance.ReleaseReservationAsync(terminalId, cashierName);

    // Chat
    public Task<IReadOnlyList<ChatHistoryItemDto>> GetChatHistoryAsync(Guid terminalId, Guid? sessionId)
        => _chatHistory.GetChatHistoryAsync(terminalId, sessionId);

    // Database & Backup
    public Task<ResultResponse> TriggerBackupAsync(string? targetDirectory, string cashierName)
        => _backup.TriggerBackupAsync(targetDirectory, cashierName);

    public Task<IReadOnlyList<BackupFileInfoDto>> ListBackupsAsync()
        => _backup.ListBackupsAsync();

    public Task<ResultResponse> RestoreBackupAsync(string backupFilePath, string cashierName)
        => _backup.RestoreBackupAsync(backupFilePath, cashierName);

    public Task<string> GetDatabaseInfoAsync()
        => _backup.GetDatabaseInfoAsync();

    private async Task PushRackSnapshotAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminals = await db.Terminals
            .Include(t => t.Zone)
            .OrderBy(t => t.Zone.DisplayOrder)
            .ThenBy(t => t.Name)
            .ToListAsync();

        var actives = await db.Sessions
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .Include(s => s.Lines)
            .Include(s => s.Tariff).ThenInclude(t => t!.Rules)
            .ToDictionaryAsync(s => s.TerminalId);

        var now = DateTime.UtcNow;
        var venueTz = TimeZoneInfo.Local;

        foreach (var terminal in terminals)
        {
            actives.TryGetValue(terminal.Id, out var active);
            var elapsed = active is null
                ? 0
                : (int)((now - active.StartedAt - TimeSpan.FromMinutes(active.PausedMinutes)).TotalMinutes);
            if (elapsed < 0) elapsed = 0;

            decimal amount = 0;
            int? remaining = null;
            if (active?.Tariff is not null)
            {
                amount = TariffEngine.ComputeTimeCharge(active.Tariff, active.StartedAt, now, venueTz, active.PausedMinutes, out _);
                amount += active.Lines.Sum(l => l.Amount);
                if (active.PlannedEndAt is { } end)
                {
                    remaining = (int)Math.Max(0, (end - now).TotalMinutes);
                }
            }

            var dto = new TerminalStateDto(
                terminal.Id,
                terminal.Name,
                terminal.Zone.Name,
                (TerminalStatusDto)terminal.Status,
                terminal.IsLocked,
                terminal.AgentVersion,
                terminal.LastSeenAt,
                active?.Id,
                amount,
                elapsed,
                remaining,
                active?.PlannedEndAt,
                active is { Status: SessionStatus.Paused },
                terminal.MaintenanceReason,
                terminal.ReservedFor,
                terminal.CpuTemp,
                terminal.GpuTemp,
                terminal.RamPercent);

            await Clients.Caller.TerminalStateChanged(dto);
        }
    }

    public async Task<HardwareBaselineDto?> GetTerminalHardwareAsync(Guid terminalId)
        => await _hardware.GetTerminalHardwareAsync(terminalId);

    public async Task<ResultResponse> SetTerminalHardwareBaselineAsync(Guid terminalId, string requestingCashier)
        => await _hardware.SetTerminalHardwareBaselineAsync(terminalId, requestingCashier);

    public async Task<ResultResponse> EnforceTerminalRefreshRateAsync(Guid terminalId, string requestingCashier)
        => await _hardware.EnforceTerminalRefreshRateAsync(terminalId, requestingCashier);

    public async Task<ResultResponse> TriggerDisklessWipeAsync(Guid terminalId, string requestingCashier)
        => await _hardware.TriggerDisklessWipeAsync(terminalId, requestingCashier);

    // Counter & Desk Workflows
    public async Task<ResultResponse> SwitchStationAsync(SwitchStationRequest req)
        => await _sessions.SwitchStationAsync(req);

    // Energy & IoT Automation
    public async Task<ResultResponse> WakeTerminalAsync(Guid terminalId, string requestingCashier)
        => await _energyIot.WakeTerminalAsync(terminalId, requestingCashier);

    public async Task<ResultResponse> WakeAllTerminalsAsync(Guid? zoneId, string requestingCashier)
        => await _energyIot.WakeAllTerminalsAsync(zoneId, requestingCashier);

    public async Task<ResultResponse> TriggerSmartRelayAsync(SmartRelayTriggerRequest req)
        => await _energyIot.TriggerSmartRelayAsync(req.TerminalId, req.PowerOn, req.CashierName);

    // Master Configuration Engine
    public async Task<MasterSystemSettingsDto> GetMasterSettingsAsync()
        => await _masterConfig.GetSettingsDtoAsync();

    public async Task<ResultResponse> SaveMasterSettingsAsync(MasterSystemSettingsDto settings, string reason, string cashierName)
        => await _masterConfig.SaveSettingsDtoAsync(settings, reason, cashierName);

    public async Task<MasterSystemSettingsDto> ResetSettingsCategoryAsync(string category, string cashierName)
        => await _masterConfig.ResetCategoryAsync(category, cashierName);
}
