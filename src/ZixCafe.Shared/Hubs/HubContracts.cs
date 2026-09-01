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
}

/// <summary>
/// Methods the SERVER pushes to connected dashboard clients (operator UI).
/// </summary>
public interface IDashboardClient
{
    Task TerminalStateChanged(TerminalStateDto state);

    Task AlertRaised(string severity, string kind, string message, Guid? terminalId, DateTime createdAtUtc);

    Task SessionUpdated(Guid sessionId, Guid terminalId, string status, decimal amount, int minutesElapsed);

    Task ChatMessage(Guid terminalId, string fromName, string message, DateTime sentAtUtc);

    Task WaitlistChanged(IReadOnlyList<WaitlistEntryDto> waiting);
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
}
