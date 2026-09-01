using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Services;

/// <summary>
/// Computes time charges for the four tariff models. Pure function: no I/O,
/// no clocks — time enters as parameters so shifts that cross midnight,
/// DST, and server hiccups are testable.
/// </summary>
public static class TariffEngine
{
    public static decimal ComputeTimeCharge(
        Tariff tariff,
        DateTime startUtc,
        DateTime endUtc,
        TimeZoneInfo venueTimeZone,
        int pausedMinutes,
        out TimeSpan billedDuration)
    {
        billedDuration = endUtc - startUtc - TimeSpan.FromMinutes(pausedMinutes);
        if (billedDuration < TimeSpan.Zero)
        {
            billedDuration = TimeSpan.Zero;
        }

        if (tariff.Model == TariffModel.Flat)
        {
            return RoundAndClamp(tariff, billedDuration, tariff.BaseRatePerHour);
        }

        var minutes = (int)Math.Ceiling(billedDuration.TotalMinutes);
        decimal total = 0;

        // Walk the billed window in venue-local wall-clock minutes, pricing
        // each rule band it overlaps. Midnight-crossing sessions therefore
        // price correctly by construction.
        var cursor = startUtc;
        var remaining = billedDuration;
        while (remaining > TimeSpan.Zero)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(cursor, venueTimeZone);
            var minuteOfDay = local.Hour * 60 + local.Minute;

            var (rule, rate) = ResolveBand(tariff, local.DayOfWeek, minuteOfDay);
            var minutesIntoBand = rule is null
                ? 1
                : 1 + Math.Max(0, MinutesLeftInBand(minuteOfDay, rule.EndMinute));

            var span = TimeSpan.FromMinutes(Math.Min(minutesIntoBand, remaining.TotalMinutes));
            total += rate / 60m * (decimal)span.TotalMinutes;
            cursor += span;
            remaining -= span;
        }

        total = Math.Round(total, 2, MidpointRounding.AwayFromZero);
        return RoundAndClamp(tariff, billedDuration, ratePerHour: 0, computedTotal: total);
    }

    public static (TariffRule? rule, decimal rate) ResolveBand(
        Tariff tariff, DayOfWeek day, int minuteOfDay)
    {
        var bit = 1 << (int)day;
        TariffRule? best = null;
        foreach (var rule in tariff.Rules)
        {
            if ((rule.DaysMask & bit) == 0)
            {
                continue;
            }
            if (minuteOfDay < rule.StartMinute || minuteOfDay >= rule.EndMinute)
            {
                continue;
            }
            if (best is null || rule.StartMinute > best.StartMinute)
            {
                best = rule;
            }
        }

        if (best is not null)
        {
            return (best, best.RatePerHour);
        }

        // Outside every band: fall back to the tariff's base rate.
        return (null, tariff.BaseRatePerHour);
    }

    private static int MinutesLeftInBand(int minuteOfDay, int bandEndMinute)
    {
        var left = bandEndMinute - minuteOfDay - 1;
        return left < 0 ? 0 : Math.Min(left, MinutesLeftInBandCap);
    }

    private const int MinutesLeftInBandCap = 24 * 60;

    private static decimal RoundAndClamp(
        Tariff tariff,
        TimeSpan billedDuration,
        decimal ratePerHour,
        decimal? computedTotal = null)
    {
        var minutes = (int)Math.Ceiling(billedDuration.TotalMinutes);

        decimal amount;
        if (computedTotal is null)
        {
            var roundedMinutes = tariff.RoundingMinutes > 1
                ? (int)Math.Ceiling(minutes / (double)tariff.RoundingMinutes) * tariff.RoundingMinutes
                : minutes;
            amount = ratePerHour / 60m * roundedMinutes;
        }
        else
        {
            amount = computedTotal.Value;
        }

        if (tariff.MinimumCharge > 0 && amount < tariff.MinimumCharge)
        {
            amount = tariff.MinimumCharge;
        }

        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
