using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class MemberTier
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public decimal DiscountPercent { get; set; }

    public decimal MinTopUpAmount { get; set; }

    public int Priority { get; set; }
}

public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? PinHash { get; set; }

    public Guid? TierId { get; set; }

    public MemberTier? Tier { get; set; }

    public decimal MoneyBalance { get; set; }

    public int TimeBalanceMinutes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MemberTransaction> Transactions { get; set; } = [];
}

public class MemberTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MemberId { get; set; }

    public Member Member { get; set; } = null!;

    public string Kind { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public int TimeMinutesDelta { get; set; }

    public int TimeBalanceAfter { get; set; }

    public string? CashierName { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
