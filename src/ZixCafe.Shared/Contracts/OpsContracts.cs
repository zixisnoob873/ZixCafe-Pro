namespace ZixCafe.Shared.Contracts;

public record CashierDto(
    Guid Id,
    string Name,
    string Role,
    bool IsActive,
    DateTime CreatedAt);

public record CreateCashierRequest(
    string Name,
    string Pin,
    string Role);

public record UpdateCashierRequest(
    Guid Id,
    string Name,
    string? NewPin,
    string Role,
    bool IsActive);

public record VerifyManagerPinRequest(string Pin);

public record VenueSettingsDto(
    string VenueName,
    string CurrencyCode,
    string CurrencySymbol,
    string Locale,
    string TaxLabel,
    decimal TaxRatePercent,
    decimal DefaultOpeningFloat,
    decimal UsbRatePerGb,
    decimal PrintCostPerPage,
    string ClosingTime,
    string? LicenseKey,
    string? AutoBackupPath,
    int AutoBackupIntervalHours,
    DateTime? LastBackupAtUtc,
    bool IsConfigured);

public record TariffRuleDto(
    Guid Id,
    int DaysMask,
    int StartMinute,
    int EndMinute,
    decimal RatePerHour);

public record TariffDto(
    Guid Id,
    string Name,
    string Model,
    decimal BaseRatePerHour,
    int RoundingMinutes,
    decimal MinimumCharge,
    int Priority,
    IReadOnlyList<TariffRuleDto> Rules);

public record SaveTariffRequest(
    Guid? Id,
    string Name,
    string Model,
    decimal BaseRatePerHour,
    int RoundingMinutes,
    decimal MinimumCharge,
    int Priority,
    IReadOnlyList<TariffRuleDto> Rules);

public record SaleLineItemRequest(
    Guid? ProductId,
    string Kind,
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    decimal DiscountAmount);

public record CreateSaleRequest(
    Guid? SessionId,
    string CashierName,
    string? CustomerName,
    string PaymentMethod,
    decimal PaidCash,
    decimal PaidCard,
    decimal PaidQr,
    decimal Discount,
    string? Note,
    IReadOnlyList<SaleLineItemRequest> Lines);

public record SaleSummaryDto(
    Guid Id,
    string? CashierName,
    string? CustomerName,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal PaidCash,
    decimal PaidCard,
    decimal PaidQr,
    decimal ChangeDue,
    string PaymentMethod,
    DateTime CreatedAt,
    int ItemCount);

public record SaleDetailDto(
    Guid Id,
    string? CashierName,
    string? CustomerName,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal PaidCash,
    decimal PaidCard,
    decimal PaidQr,
    decimal ChangeDue,
    string PaymentMethod,
    string? Note,
    DateTime CreatedAt,
    IReadOnlyList<LineDto> Lines);

public record TicketDto(
    Guid Id,
    string Code,
    string Type,
    int? DurationMinutes,
    decimal? CreditAmount,
    decimal Price,
    bool IsUsed,
    DateTime? UsedAt,
    string? IssuedBy,
    string? BatchRef,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

public record SellTicketRequest(
    string Type,
    int? DurationMinutes,
    decimal? CreditAmount,
    decimal Price,
    string PaymentMethod,
    string CashierName);

public record BatchGenerateTicketsRequest(
    string Type,
    int? DurationMinutes,
    decimal? CreditAmount,
    decimal Price,
    int Count,
    string BatchRef,
    string CashierName);

public record MemberDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    Guid? TierId,
    string? TierName,
    int TimeBalanceMinutes,
    decimal MoneyBalance,
    bool IsFrozen,
    bool IsActive,
    DateTime CreatedAt);

public record MemberTierDto(
    Guid Id,
    string Name,
    decimal DiscountPercent,
    decimal MinTopUpAmount,
    int Priority);

public record SaveMemberRequest(
    Guid? Id,
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    Guid? TierId);

public record MemberTopUpRequest(
    Guid MemberId,
    string Kind,
    decimal Amount,
    int Minutes,
    string PaymentMethod,
    string CashierName);

public record MemberTransactionDto(
    Guid Id,
    Guid MemberId,
    string Type,
    decimal Amount,
    int TimeMinutes,
    string? Reference,
    string? CashierName,
    DateTime CreatedAt);

public record ProductDetailDto(
    Guid Id,
    string Sku,
    string Name,
    string Category,
    decimal Price,
    int StockQty,
    int LowStockThreshold,
    bool IsActive);

public record SaveProductRequest(
    Guid? Id,
    string Sku,
    string Name,
    string Category,
    decimal Price,
    int LowStockThreshold,
    bool IsActive);

public record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    int QuantityChange,
    string Reason,
    string? Reference,
    string? CashierName,
    DateTime CreatedAt);

public record StockAdjustmentRequest(
    Guid ProductId,
    int QuantityChange,
    string Reason,
    string? Reference,
    string CashierName);

public record PrintJobDto(
    Guid Id,
    Guid? SessionId,
    Guid? TerminalId,
    string DocumentName,
    int PageCount,
    decimal TotalCost,
    string Status,
    DateTime CreatedAt);

public record ReleasePrintJobRequest(
    Guid PrintJobId,
    string PaymentMethod,
    string CashierName);

public record UsbMeteringDto(
    Guid Id,
    Guid? SessionId,
    Guid? TerminalId,
    long MegabytesTransferred,
    decimal Amount,
    DateTime CreatedAt);

public record ShiftReportDto(
    Guid ShiftId,
    string CashierName,
    DateTime StartedAt,
    DateTime? EndedAt,
    decimal OpeningFloat,
    decimal TimeRevenue,
    decimal ProductRevenue,
    decimal PrintUsbRevenue,
    decimal DiscountsTotal,
    decimal AdjustmentsTotal,
    decimal ExpectedDrawer,
    decimal? CountedDrawer,
    decimal? Variance,
    int SessionCount,
    int SaleCount);

public record DailyRevenueDto(
    DateTime Date,
    decimal TimeRevenue,
    decimal ProductRevenue,
    decimal OtherRevenue,
    decimal TotalRevenue,
    int TotalSessions);

public record SessionHistoryDto(
    Guid Id,
    string TerminalName,
    string Mode,
    string? MemberName,
    string? TicketCode,
    decimal TotalAmount,
    DateTime StartedAt,
    DateTime? EndedAt,
    int DurationMinutes,
    string? EndedBy);

public record AuditEntryDto(
    Guid Id,
    string Action,
    string TargetType,
    string? TargetId,
    string? DetailJson,
    string? CashierName,
    string PrevHash,
    string Hash,
    DateTime CreatedAt);

public record AuditVerificationResult(
    bool IsValid,
    int CheckedCount,
    string? BrokenEntryId,
    string? ErrorMessage);

public record AlertDto(
    Guid Id,
    string Severity,
    string Kind,
    string Message,
    Guid? TerminalId,
    string? TerminalName,
    DateTime CreatedAt,
    bool IsAcknowledged,
    string? AcknowledgedBy,
    DateTime? AcknowledgedAt);

public record AcknowledgeAlertRequest(
    Guid AlertId,
    string CashierName);

public record MuteAlertRequest(
    string Kind,
    int MuteMinutes);

public record RemoteActionRequest(
    Guid TerminalId,
    string Action,
    string? Reason,
    string CashierName);

public record ScreenCaptureFrameDto(
    Guid TerminalId,
    byte[] JpegBytes,
    DateTime CapturedAtUtc);

public record HardwareTelemetryDto(
    Guid TerminalId,
    int? CpuTemp,
    int? GpuTemp,
    int? RamPercent,
    int? DiskFreeGb,
    string? AgentVersion);

public record ProhibitedAppDto(
    Guid Id,
    string Match,
    string MatchKind,
    bool KillOnSight,
    bool IsActive);

public record SetTerminalMaintenanceRequest(
    Guid TerminalId,
    bool InMaintenance,
    string? Reason,
    string CashierName);

public record ReserveTerminalRequest(
    Guid TerminalId,
    string GuestName,
    DateTime ReservedUntilUtc,
    string CashierName);

public record ChatHistoryItemDto(
    Guid Id,
    Guid? SessionId,
    Guid TerminalId,
    string FromName,
    string Message,
    bool IsFromCustomer,
    DateTime SentAtUtc);

public record BackupFileInfoDto(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTime CreatedAtUtc);
