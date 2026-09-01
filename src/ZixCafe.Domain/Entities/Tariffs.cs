using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class Tariff
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public TariffModel Model { get; set; } = TariffModel.Flat;

    public decimal BaseRatePerHour { get; set; }

    public int RoundingMinutes { get; set; } = 1;

    public decimal MinimumCharge { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    public List<TariffRule> Rules { get; set; } = [];
}

public class TariffRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TariffId { get; set; }

    public Tariff Tariff { get; set; } = null!;

    public int DaysMask { get; set; } = 0b1111111;

    public int StartMinute { get; set; }

    public int EndMinute { get; set; } = MinutesPerDay;

    public decimal RatePerHour { get; set; }

    public const int MinutesPerDay = 24 * 60;
}
