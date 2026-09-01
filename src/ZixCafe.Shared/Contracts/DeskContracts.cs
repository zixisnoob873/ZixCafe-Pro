namespace ZixCafe.Shared.Contracts;

public record ShiftDto(
    Guid Id,
    string CashierName,
    decimal OpeningFloat,
    decimal? ExpectedDrawer,
    decimal? CountedDrawer,
    decimal? Variance,
    DateTime StartedAt,
    DateTime? EndedAt,
    bool IsOpen);

public record ShiftResponse(bool Ok, string? Error, ShiftDto? Shift);

public record WaitlistEntryDto(
    Guid Id,
    string GuestName,
    int PartySize,
    string Status,
    string? Contact,
    DateTime EnqueuedAt,
    Guid? ServedTerminalId,
    DateTime? ServedAt);

public record WaitlistResponse(bool Ok, string? Error, WaitlistEntryDto? Entry);

public record LoanDto(
    Guid Id,
    Guid? SessionId,
    string ItemName,
    decimal DepositAmount,
    string Status,
    string? HeldBy,
    string? ReturnedTo,
    DateTime CreatedAt,
    DateTime? ReturnedAt);

public record LoanResponse(bool Ok, string? Error, LoanDto? Loan);
