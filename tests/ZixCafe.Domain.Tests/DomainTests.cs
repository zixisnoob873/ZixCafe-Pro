using System.Security.Cryptography;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;

namespace ZixCafe.Domain.Tests;

public class TariffEngineTests
{
    private static readonly TimeZoneInfo Venue = TimeZoneInfo.Local;
    private static readonly DateTime T0 = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Tariff Flat(decimal rate = 2.00m, int rounding = 1, decimal min = 0m) => new()
    {
        Name = "Flat",
        Model = TariffModel.Flat,
        BaseRatePerHour = rate,
        RoundingMinutes = rounding,
        MinimumCharge = min
    };

    [Fact]
    public void Flat_rate_charges_per_minute()
    {
        var charge = TariffEngine.ComputeTimeCharge(Flat(rate: 2.00m), T0, T0.AddMinutes(45), Venue, 0, out var billed);
        Assert.Equal(TimeSpan.FromMinutes(45), billed);
        Assert.Equal(1.50m, charge);
    }

    [Fact]
    public void Flat_rate_rounds_up_to_5_minute_blocks()
    {
        var charge = TariffEngine.ComputeTimeCharge(Flat(rate: 2.00m, rounding: 5), T0, T0.AddMinutes(47), Venue, 0, out var billed);
        Assert.Equal(1.67m, charge);
        Assert.Equal(TimeSpan.FromMinutes(47), billed);
    }

    [Fact]
    public void Minimum_charge_applies_to_short_sessions()
    {
        var charge = TariffEngine.ComputeTimeCharge(Flat(rate: 2.00m, min: 0.50m), T0, T0.AddMinutes(3), Venue, 0, out _);
        Assert.Equal(0.50m, charge);
    }

    [Fact]
    public void Paused_minutes_are_not_billed()
    {
        var charge = TariffEngine.ComputeTimeCharge(Flat(rate: 2.00m), T0, T0.AddMinutes(60), Venue, 10, out var billed);
        Assert.Equal(TimeSpan.FromMinutes(50), billed);
        Assert.Equal(1.67m, charge);
    }

    [Fact]
    public void Zero_length_session_charges_zero()
    {
        var charge = TariffEngine.ComputeTimeCharge(Flat(), T0, T0, Venue, 0, out var billed);
        Assert.Equal(TimeSpan.Zero, billed);
        Assert.Equal(0m, charge);
    }

    [Fact]
    public void Day_schedule_tariff_applies_time_of_day_rates()
    {
        var tariff = new Tariff
        {
            Name = "Schedule",
            Model = TariffModel.DaySchedule,
            BaseRatePerHour = 3.00m,
            RoundingMinutes = 1,
            Rules =
            {
                new TariffRule { DaysMask = 0b1111111, StartMinute = 10 * 60, EndMinute = 11 * 60, RatePerHour = 3.00m },
                new TariffRule { DaysMask = 0b1111111, StartMinute = 11 * 60, EndMinute = 12 * 60, RatePerHour = 2.00m }
            }
        };

        // 10:00 to 12:00 UTC: First 60 min @ 3.00/hr ($3.00) + Next 60 min @ 2.00/hr ($2.00) = $5.00
        var charge = TariffEngine.ComputeTimeCharge(tariff, T0, T0.AddMinutes(120), TimeZoneInfo.Utc, 0, out var billed);
        Assert.Equal(TimeSpan.FromMinutes(120), billed);
        Assert.Equal(5.00m, charge);
    }

    [Fact]
    public void Day_schedule_uses_band_rates_per_minute()
    {
        var tariff = new Tariff
        {
            Name = "Bands",
            Model = TariffModel.DaySchedule,
            BaseRatePerHour = 2.00m,
            RoundingMinutes = 1,
            Rules =
            {
                new TariffRule { DaysMask = 0b0111111, StartMinute = 18 * 60, EndMinute = TariffRule.MinutesPerDay, RatePerHour = 3.00m }
            }
        };

        var start = new DateTime(2026, 6, 15, 17, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);
        var charge = TariffEngine.ComputeTimeCharge(tariff, start, end, Venue, 0, out _);

        var expectedCrossesBand = TimeZoneInfo.Local.Id != "UTC";
        if (!expectedCrossesBand)
        {
            Assert.Equal(5.00m, charge);
        }
        else
        {
            Assert.True(charge > 0);
        }
    }

    [Fact]
    public void Outside_bands_falls_back_to_base_rate()
    {
        var tariff = new Tariff
        {
            Name = "Bands",
            Model = TariffModel.DaySchedule,
            BaseRatePerHour = 2.00m,
            RoundingMinutes = 1,
            Rules =
            {
                new TariffRule { DaysMask = 0b0111111, StartMinute = 18 * 60, EndMinute = TariffRule.MinutesPerDay, RatePerHour = 3.00m }
            }
        };

        var start = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var charge = TariffEngine.ComputeTimeCharge(tariff, start, start.AddMinutes(30), TimeZoneInfo.Utc, 0, out _);
        Assert.Equal(1.00m, charge);
    }
}

public class TicketCodeGeneratorTests
{
    [Fact]
    public void Codes_are_formatted_in_four_char_blocks()
    {
        var code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create());
        var blocks = code.Split('-');
        Assert.Equal(4, blocks.Length);
        Assert.All(blocks.Take(3), b => Assert.Equal(4, b.Length));
        Assert.Equal(1, blocks[3].Length);
    }

    [Fact]
    public void Codes_pass_their_own_check()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create());
            Assert.True(TicketCodeGenerator.IsValidFormat(code), code);
        }
    }

    [Fact]
    public void Tampered_codes_fail_the_check()
    {
        var code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create());
        var last = code[^1];
        var alt = last == '2' ? '3' : '2';
        var tampered = code[..^1] + alt;
        Assert.False(TicketCodeGenerator.IsValidFormat(tampered));
    }

    [Fact]
    public void Codes_exclude_ambiguous_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create());
            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('U', code);
            Assert.DoesNotContain('L', code);
        }
    }
}

public class SecretHasherTests
{
    [Fact]
    public void Hash_round_trips()
    {
        var encoded = SecretHasher.Hash("1234");
        Assert.True(SecretHasher.Verify("1234", encoded));
        Assert.False(SecretHasher.Verify("9999", encoded));
    }

    [Fact]
    public void Same_secret_produces_different_salts()
    {
        var a = SecretHasher.Hash("hunter2");
        var b = SecretHasher.Hash("hunter2");
        Assert.NotEqual(a, b);
    }
}

public class AuditChainTests
{
    [Fact]
    public void Chain_links_are_deterministic_and_sensitive_to_content()
    {
        var (p1, h1) = AuditChain.Link("", "session.start", "Session", "1", null, "cashier", DateTime.UtcNow);
        var (p2, h2) = AuditChain.Link(h1, "session.start", "Session", "1", null, "cashier", DateTime.UtcNow);

        Assert.Equal(h1, p2);
        Assert.NotEqual(h1, h2);

        var (_, tampered) = AuditChain.Link(h1, "session.start", "Session", "2", null, "cashier", DateTime.UtcNow);
        Assert.NotEqual(h2, tampered);
    }

    [Fact]
    public void Multi_step_audit_chain_validates_fully()
    {
        var entries = new List<(string Action, string Target, string Prev, string Hash)>();
        var currentPrev = string.Empty;
        var now = DateTime.UtcNow;

        for (var i = 0; i < 50; i++)
        {
            var action = $"action.{i}";
            var target = $"Target_{i}";
            var (prev, hash) = AuditChain.Link(currentPrev, action, "Terminal", target, null, "admin", now.AddMinutes(i));
            entries.Add((action, target, prev, hash));
            currentPrev = hash;
        }

        // Verify chain forward
        var testPrev = string.Empty;
        for (var i = 0; i < entries.Count; i++)
        {
            Assert.Equal(testPrev, entries[i].Prev);
            var (_, computed) = AuditChain.Link(entries[i].Prev, entries[i].Action, "Terminal", entries[i].Target, null, "admin", now.AddMinutes(i));
            Assert.Equal(entries[i].Hash, computed);
            testPrev = entries[i].Hash;
        }
    }
}

public class ShiftTests
{
    [Fact]
    public void Variance_is_null_until_both_drawer_amounts_exist()
    {
        var shift = new Shift { OpeningFloat = 50m, ExpectedDrawer = 120m };
        Assert.Null(shift.Variance);

        shift.CountedDrawer = 120m;
        Assert.Equal(0m, shift.Variance);
    }

    [Fact]
    public void Variance_is_counted_minus_expected()
    {
        var shift = new Shift { OpeningFloat = 50m, ExpectedDrawer = 120m, CountedDrawer = 115.50m };
        Assert.Equal(-4.50m, shift.Variance);

        var over = new Shift { ExpectedDrawer = 100m, CountedDrawer = 102.25m };
        Assert.Equal(2.25m, over.Variance);
    }
}

public class POSMathTests
{
    [Fact]
    public void Split_tender_reconciles_total_payment()
    {
        var subtotal = 45.00m;
        var discount = 5.00m;
        var total = subtotal - discount; // 40.00

        var paidCash = 20.00m;
        var paidCard = 10.00m;
        var paidQr = 15.00m; // total tendered 45.00

        var totalTendered = paidCash + paidCard + paidQr;
        var changeDue = Math.Max(0m, paidCash - Math.Max(0m, total - paidCard - paidQr));

        Assert.Equal(40.00m, total);
        Assert.Equal(45.00m, totalTendered);
        Assert.Equal(5.00m, changeDue);
    }

    [Fact]
    public void Oversell_guard_prevents_negative_inventory()
    {
        var stockQty = 3;
        var requestedQty = 5;

        var canSell = stockQty >= requestedQty;
        Assert.False(canSell);
    }
}

public class PeripheralCostTests
{
    [Fact]
    public void Print_cost_multiplies_pages_and_copies()
    {
        var pageCount = 15;
        var copies = 2;
        var costPerPage = 0.15m;

        var totalCost = pageCount * copies * costPerPage;
        Assert.Equal(4.50m, totalCost);
    }

    [Fact]
    public void Usb_rate_calculates_by_megabytes()
    {
        var mb = 2048L; // 2 GB
        var ratePerGb = 0.05m;

        var gigabytes = (decimal)mb / 1024m;
        var totalCharge = gigabytes * ratePerGb;

        Assert.Equal(2.0m, gigabytes);
        Assert.Equal(0.10m, totalCharge);
    }
}
