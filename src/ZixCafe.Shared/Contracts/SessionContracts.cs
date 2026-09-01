namespace ZixCafe.Shared.Contracts;

public record RegisterRequest(
    string Credential,
    string MachineGuid,
    string AgentVersion);

public record RegisterResult(
    Guid TerminalId,
    string Name,
    string ZoneName,
    string? Secret);

public record StartSessionRequest(
    Guid TerminalId,
    string Mode,
    Guid? MemberId,
    string? TicketCode,
    int? PrepaidMinutes,
    string CashierName);

public record StartSessionResponse(
    bool Ok,
    Guid? SessionId,
    string? Error,
    int? MinutesGranted,
    decimal? DepositDue);

public record EndSessionRequest(
    Guid SessionId,
    string CashierName);

public record EndSessionResponse(
    bool Ok,
    string? Error,
    decimal TimeCharge,
    decimal ExtrasTotal,
    decimal TotalDue,
    IReadOnlyList<LineDto> Lines);

public record LineDto(
    string Kind,
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    decimal Amount);

public record LoginRequest(string Name, string Pin);

public record LoginResponse(bool Ok, string? Error, string Role);

public record ProductDto(Guid Id, string Sku, string Name, decimal Price, int StockQty);

public record MemberDto(Guid Id, string Code, string Name, int TimeBalanceMinutes, decimal MoneyBalance);

public record FindMemberResponse(bool Ok, string? Error, MemberDto? Member);

public record AddLineResponse(bool Ok, string? Error, decimal ExtrasTotal);

public record ResultResponse(bool Ok, string? Error)
{
    public static ResultResponse Success(string? message = null) => new(true, message);
    public static ResultResponse Fail(string error) => new(false, error);
}
