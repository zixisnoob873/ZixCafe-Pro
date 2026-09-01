using ZixCafe.Shared.Contracts;

namespace ZixCafe.Shared.Hubs;

/// <summary>
/// Methods the SERVER pushes to a connected client agent.
/// </summary>
public interface ITerminalClient
{
    Task ShowLockScreen(DateTime serverUtcNow);

    Task SessionStarted(Guid sessionId, string mode, int? minutesGranted, DateTime? plannedEndUtc, string? memberName);

    Task SessionEnded(string reason, DateTime serverUtcNow);

    Task TimeSync(DateTime serverUtcNow, DateTime? plannedEndUtc, decimal currentAmount);

    Task ShowBanner(string severity, string message);

    Task ApplyPolicy(string policyName, bool enabled);

    Task ChatMessage(string fromName, string message, DateTime sentAtUtc);

    Task SessionPaused(DateTime serverUtcNow);

    Task SessionResumed(DateTime serverUtcNow, DateTime? plannedEndUtc);

    Task CaptureScreenFrame(Guid requestId);

    Task RemoteCommand(string command);
}

/// <summary>
/// Methods the CLIENT agent calls on the server's TerminalHub.
/// </summary>
public interface ITerminalServer
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request);

    Task HeartbeatAsync(string agentVersion, int cpuPercent, int ramPercent, int diskFreeGb);

    Task SessionCountdownTickAsync(Guid sessionId, int minutesElapsed, decimal currentAmount);

    Task SendChatToDeskAsync(string message);

    Task SubmitScreenFrameAsync(Guid requestId, byte[] jpegBytes);

    Task ReportProhibitedAppKilledAsync(string processName);

    Task ReportUsbUsageAsync(long bytesTransferred);
}

/// <summary>
/// Methods the SERVER pushes to connected dashboard clients (operator UI).
/// </summary>
public interface IDashboardClient
{
    Task TerminalStateChanged(TerminalStateDto state);

    Task AlertRaised(string severity, string kind, string message, Guid? terminalId, DateTime createdAtUtc);

    Task AlertsUpdated(IReadOnlyList<AlertDto> alerts);

    Task SessionUpdated(Guid sessionId, Guid terminalId, string status, decimal amount, int minutesElapsed);

    Task ChatMessage(Guid terminalId, string fromName, string message, DateTime sentAtUtc);

    Task WaitlistChanged(IReadOnlyList<WaitlistEntryDto> waiting);

    Task ScreenFrameReceived(Guid terminalId, byte[] jpegBytes);
}

/// <summary>
/// Methods the operator dashboard calls on the server's DashboardHub.
/// </summary>
public interface IDashboardServer
{
    Task SubscribeAsync();

    Task RequestRackSnapshotAsync();

    Task<string> IssuePairingCodeAsync(Guid terminalId);

    Task<StartSessionResponse> StartSessionAsync(StartSessionRequest request);

    Task<EndSessionResponse> EndSessionAsync(EndSessionRequest request);

    Task LockTerminalAsync(Guid terminalId);

    Task SendChatToTerminalAsync(Guid terminalId, string message);

    Task<LoginResponse> LoginAsync(LoginRequest request);

    Task<ResultResponse> PauseSessionAsync(Guid terminalId, string cashierName);

    Task<ResultResponse> ResumeSessionAsync(Guid terminalId, string cashierName);

    Task<FindMemberResponse> FindMemberAsync(string query);

    Task<IReadOnlyList<ProductDto>> GetProductsAsync();

    Task<AddLineResponse> AddProductLineAsync(Guid sessionId, Guid productId, decimal quantity, string cashierName);

    Task<ShiftResponse> OpenShiftAsync(string cashierName, decimal openingFloat);

    Task<ShiftDto?> GetCurrentShiftAsync();

    Task<ShiftResponse> CloseShiftAsync(string cashierName, decimal countedDrawer, string? note);

    Task<IReadOnlyList<WaitlistEntryDto>> GetWaitlistAsync();

    Task<WaitlistResponse> AddToWaitlistAsync(string guestName, int partySize, string? contact);

    Task<StartSessionResponse> SeatWaitlistGuestAsync(Guid entryId, Guid terminalId, string cashierName);

    Task<WaitlistResponse> SkipWaitlistEntryAsync(Guid entryId, string cashierName);

    Task<IReadOnlyList<LoanDto>> GetLoansAsync();

    Task<LoanResponse> LoanItemAsync(string itemName, decimal deposit, string heldBy, Guid? sessionId);

    Task<LoanResponse> ReturnLoanAsync(Guid loanId, string returnedTo, string cashierName, bool forfeited);

    Task<ResultResponse> LockAllTerminalsAsync(string cashierName);

    // Cashiers
    Task<IReadOnlyList<CashierDto>> GetCashiersAsync();
    Task<ResultResponse> CreateCashierAsync(CreateCashierRequest request, string requestingCashier);
    Task<ResultResponse> UpdateCashierAsync(UpdateCashierRequest request, string requestingCashier);
    Task<ResultResponse> VerifyManagerPinAsync(string pin);

    // Venue Settings
    Task<VenueSettingsDto> GetVenueSettingsAsync();
    Task<ResultResponse> SaveVenueSettingsAsync(VenueSettingsDto settings, string requestingCashier);

    // Tariffs
    Task<IReadOnlyList<TariffDto>> GetTariffsAsync();
    Task<ResultResponse> SaveTariffAsync(SaveTariffRequest request, string requestingCashier);
    Task<ResultResponse> DeleteTariffAsync(Guid tariffId, string requestingCashier);

    // POS & Retail Sales
    Task<ResultResponse> CreateSaleAsync(CreateSaleRequest request);
    Task<IReadOnlyList<SaleSummaryDto>> GetRecentSalesAsync(int limit);
    Task<SaleDetailDto?> GetSaleDetailAsync(Guid saleId);

    // Tickets
    Task<IReadOnlyList<TicketDto>> GetTicketsAsync(bool unusedOnly);
    Task<ResultResponse> SellTicketAsync(SellTicketRequest request);
    Task<ResultResponse> BatchGenerateTicketsAsync(BatchGenerateTicketsRequest request);
    Task<ResultResponse> VoidTicketAsync(Guid ticketId, string cashierName, string managerPin);

    // Members
    Task<IReadOnlyList<MemberDetailDto>> GetMembersAsync(string? search);
    Task<MemberDetailDto?> GetMemberDetailAsync(Guid memberId);
    Task<ResultResponse> SaveMemberAsync(SaveMemberRequest request, string requestingCashier);
    Task<ResultResponse> TopUpMemberAsync(MemberTopUpRequest request);
    Task<IReadOnlyList<MemberTransactionDto>> GetMemberTransactionsAsync(Guid memberId);
    Task<IReadOnlyList<MemberTierDto>> GetMemberTiersAsync();
    Task<ResultResponse> SetMemberFrozenAsync(Guid memberId, bool isFrozen, string requestingCashier);

    // Inventory
    Task<IReadOnlyList<ProductDetailDto>> GetProductsFullAsync();
    Task<ResultResponse> SaveProductAsync(SaveProductRequest request, string requestingCashier);
    Task<ResultResponse> AdjustStockAsync(StockAdjustmentRequest request);
    Task<IReadOnlyList<StockMovementDto>> GetStockMovementsAsync(Guid? productId, int limit);

    // Print & USB
    Task<IReadOnlyList<PrintJobDto>> GetPrintJobsAsync();
    Task<ResultResponse> ReleasePrintJobAsync(Guid printJobId, string paymentMethod, string cashierName);
    Task<ResultResponse> CancelPrintJobAsync(Guid printJobId, string reason, string cashierName);

    // Reports & Audit
    Task<ShiftReportDto?> GetShiftReportAsync(Guid shiftId);
    Task<IReadOnlyList<DailyRevenueDto>> GetDailyRevenueReportAsync(DateTime fromDateUtc, DateTime toDateUtc);
    Task<IReadOnlyList<SessionHistoryDto>> GetSessionHistoryAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? terminalId);
    Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync(int limit);
    Task<AuditVerificationResult> VerifyAuditChainAsync();

    // Alerts
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync();
    Task<ResultResponse> AcknowledgeAlertAsync(Guid alertId, string cashierName);
    Task<ResultResponse> MuteAlertKindAsync(string kind, int minutes);

    // Remote Ops
    Task<ResultResponse> RequestScreenViewAsync(Guid terminalId, string requestingCashier);
    Task<ResultResponse> ExecuteRemoteActionAsync(RemoteActionRequest request);
    Task<IReadOnlyList<ProhibitedAppDto>> GetProhibitedAppsAsync();
    Task<ResultResponse> SaveProhibitedAppAsync(string match, string matchKind, bool killOnSight, string requestingCashier);
    Task<ResultResponse> DeleteProhibitedAppAsync(Guid id, string requestingCashier);

    // Maintenance & Reservations
    Task<ResultResponse> SetTerminalMaintenanceAsync(SetTerminalMaintenanceRequest request);
    Task<ResultResponse> ReserveTerminalAsync(ReserveTerminalRequest request);
    Task<ResultResponse> ReleaseReservationAsync(Guid terminalId, string cashierName);

    // Chat
    Task<IReadOnlyList<ChatHistoryItemDto>> GetChatHistoryAsync(Guid terminalId, Guid? sessionId);

    // Database & Backup
    Task<ResultResponse> TriggerBackupAsync(string? targetDirectory, string cashierName);
    Task<string> GetDatabaseInfoAsync();
}
