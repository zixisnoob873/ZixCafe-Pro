using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ZixCafe.Server.App.Hubs;

public class DashboardHub : Hub<IDashboardClient>, IDashboardServer
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly TerminalRegistry _registry;
    private readonly SessionService _sessions;
    private readonly DeskService _desk;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminals;

    public DashboardHub(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        TerminalRegistry registry,
        SessionService sessions,
        DeskService desk,
        IHubContext<TerminalHub, ITerminalClient> terminals)
    {
        _dbFactory = dbFactory;
        _registry = registry;
        _sessions = sessions;
        _desk = desk;
        _terminals = terminals;
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
        await _terminals.Clients.Group(TerminalGroups.Terminal(terminalId)).ChatMessage("Front desk", message, sentAt);
        await Clients.Others.ChatMessage(terminalId, "Front desk", message, sentAt);
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request)
        => Task.FromResult(_sessions.Login(request));

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

    private async Task PushRackSnapshotAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var states = await db.Terminals
            .Include(t => t.Zone)
            .Select(t => new TerminalStateDto(
                t.Id,
                t.Name,
                t.Zone.Name,
                (TerminalStatusDto)t.Status,
                t.IsLocked,
                t.AgentVersion,
                t.LastSeenAt,
                null,
                0m,
                0,
                null,
                null,
                false))
            .ToListAsync();

        foreach (var state in states)
        {
            await Clients.Caller.TerminalStateChanged(state);
        }
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
