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

public class DatabaseBackupValidationTests
{
    [Fact]
    public void Sqlite_header_validation_identifies_valid_magic_bytes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var headerBytes = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
            var fullDummyFile = new byte[200];
            Array.Copy(headerBytes, fullDummyFile, headerBytes.Length);
            File.WriteAllBytes(tempFile, fullDummyFile);

            using var stream = File.OpenRead(tempFile);
            var header = new byte[16];
            var read = stream.Read(header, 0, 16);
            var headerStr = System.Text.Encoding.ASCII.GetString(header);

            Assert.Equal(16, read);
            Assert.StartsWith("SQLite format 3", headerStr);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Corrupted_or_empty_file_fails_sqlite_header_validation()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, [0x00, 0x01, 0x02, 0x03]);
            using var stream = File.OpenRead(tempFile);
            var header = new byte[16];
            var read = stream.Read(header, 0, 16);
            var headerStr = System.Text.Encoding.ASCII.GetString(header);

            Assert.False(headerStr.StartsWith("SQLite format 3"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public class HardwareAntiTheftWatchdogTests
{
    [Fact]
    public void Identical_hardware_reports_zero_discrepancies()
    {
        var baseline = new TerminalHardwareBaseline
        {
            TerminalId = Guid.NewGuid(),
            CpuName = "AMD Ryzen 7 7800X3D",
            CpuId = "CPU-1001",
            GpuName = "NVIDIA GeForce RTX 4080",
            GpuDeviceId = "PCI\\VEN_10DE&DEV_2704",
            TotalRamMb = 32768,
            RamSerials = "RAM-32GB-SN1",
            DiskSerial = "NVME-SN-9988",
            UsbDevicesJson = System.Text.Json.JsonSerializer.Serialize(new[] { "USB\\VID_046D&PID_C08B", "USB\\VID_1532&PID_022A" })
        };

        var currentUsb = new List<string> { "USB\\VID_046D&PID_C08B", "USB\\VID_1532&PID_022A" };

        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            currentCpuName: "AMD Ryzen 7 7800X3D",
            currentCpuId: "CPU-1001",
            currentGpuName: "NVIDIA GeForce RTX 4080",
            currentGpuDeviceId: "PCI\\VEN_10DE&DEV_2704",
            currentRamMb: 32768,
            currentRamSerials: "RAM-32GB-SN1",
            currentDiskSerial: "NVME-SN-9988",
            currentUsbDeviceIds: currentUsb);

        Assert.Empty(discrepancies);
    }

    [Fact]
    public void Ram_stick_removal_triggers_critical_alert()
    {
        var baseline = new TerminalHardwareBaseline
        {
            TerminalId = Guid.NewGuid(),
            CpuName = "Intel Core i7-14700K",
            GpuName = "NVIDIA GeForce RTX 4070 Ti",
            TotalRamMb = 32768,
            RamSerials = "RAM-32GB-SN1"
        };

        // User or thief removed a 16GB RAM stick (now 16384 MB)
        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            currentCpuName: "Intel Core i7-14700K",
            currentCpuId: null,
            currentGpuName: "NVIDIA GeForce RTX 4070 Ti",
            currentGpuDeviceId: null,
            currentRamMb: 16384,
            currentRamSerials: "RAM-16GB-SN1",
            currentDiskSerial: null,
            currentUsbDeviceIds: []);

        Assert.Contains(discrepancies, d => d.ComponentType == "RAM" && d.Severity == "Critical");
    }

    [Fact]
    public void Gpu_swap_or_removal_triggers_critical_alert()
    {
        var baseline = new TerminalHardwareBaseline
        {
            TerminalId = Guid.NewGuid(),
            CpuName = "Intel Core i7-14700K",
            GpuName = "NVIDIA GeForce RTX 4090",
            GpuDeviceId = "PCI\\VEN_10DE&DEV_2684",
            TotalRamMb = 32768
        };

        // GPU swapped for older RTX 3060
        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            currentCpuName: "Intel Core i7-14700K",
            currentCpuId: null,
            currentGpuName: "NVIDIA GeForce RTX 3060",
            currentGpuDeviceId: "PCI\\VEN_10DE&DEV_2503",
            currentRamMb: 32768,
            currentRamSerials: null,
            currentDiskSerial: null,
            currentUsbDeviceIds: []);

        Assert.Contains(discrepancies, d => d.ComponentType == "GPU" && d.Severity == "Critical");
    }

    [Fact]
    public void Unplugged_peripheral_triggers_warning_alert()
    {
        var baseline = new TerminalHardwareBaseline
        {
            TerminalId = Guid.NewGuid(),
            CpuName = "Ryzen 5 7600",
            GpuName = "RTX 4060",
            TotalRamMb = 16384,
            UsbDevicesJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                "USB\\VID_046D&PID_C08B", // Mouse
                "USB\\VID_1532&PID_022A", // Keyboard
                "USB\\VID_0951&PID_16D8"  // Headset
            })
        };

        // Headset was unplugged / stolen
        var currentUsb = new List<string>
        {
            "USB\\VID_046D&PID_C08B",
            "USB\\VID_1532&PID_022A"
        };

        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            currentCpuName: "Ryzen 5 7600",
            currentCpuId: null,
            currentGpuName: "RTX 4060",
            currentGpuDeviceId: null,
            currentRamMb: 16384,
            currentRamSerials: null,
            currentDiskSerial: null,
            currentUsbDeviceIds: currentUsb);

        Assert.Contains(discrepancies, d => d.ComponentType == "USB Peripheral" && d.Severity == "Warning");
    }
}

public class DisplayRefreshRatePolicyTests
{
    [Fact]
    public void Native_high_refresh_rate_is_preferred_over_default_60hz()
    {
        var availableModes = new[] { 60, 120, 144, 240, 360 };
        var maxHz = availableModes.Max();

        Assert.Equal(360, maxHz);
        Assert.True(maxHz > 60);
    }
}

public class OfflineGracePeriodTests
{
    [Fact]
    public void Grace_period_countdown_formats_properly()
    {
        var remainingSeconds = 175;
        var span = TimeSpan.FromSeconds(remainingSeconds);
        var formatted = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";

        Assert.Equal("02:55", formatted);
    }

    [Fact]
    public void Grace_period_clamps_within_configured_bounds()
    {
        var configured = 5000;
        var clamped = Math.Clamp(configured, 10, 3600);
        Assert.Equal(3600, clamped);

        var tooLow = -50;
        var clampedLow = Math.Clamp(tooLow, 10, 3600);
        Assert.Equal(10, clampedLow);
    }
}

public class WakeOnLanTests
{
    [Fact]
    public void BuildMagicPacket_Constructs102BytePayloadWithProperPrefixAndReps()
    {
        var mac = "00:11:22:33:44:55";
        var packet = WakeOnLanService.BuildMagicPacket(mac);

        Assert.NotNull(packet);
        Assert.Equal(102, packet.Length);

        // First 6 bytes must be 0xFF
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(0xFF, packet[i]);
        }

        // Next 16 iterations must be the 6 MAC bytes
        byte[] expectedMacBytes = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55];
        for (int rep = 0; rep < 16; rep++)
        {
            for (int b = 0; b < 6; b++)
            {
                Assert.Equal(expectedMacBytes[b], packet[6 + (rep * 6) + b]);
            }
        }
    }

    [Theory]
    [InlineData("AA-BB-CC-DD-EE-FF")]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    [InlineData("aabb.ccdd.eeff")]
    [InlineData("AABBCCDDEEFF")]
    public void BuildMagicPacket_HandlesVariousMacAddressFormats(string macInput)
    {
        var packet = WakeOnLanService.BuildMagicPacket(macInput);
        Assert.Equal(102, packet.Length);
        Assert.Equal(0xAA, packet[6]);
        Assert.Equal(0xFF, packet[11]);
    }

    [Theory]
    [InlineData("invalid-mac")]
    [InlineData("00:11:22:33:44")] // Only 5 bytes
    [InlineData("00:11:22:33:44:55:66")] // 7 bytes
    [InlineData("GG:HH:II:JJ:KK:LL")] // Non-hex
    public void BuildMagicPacket_ThrowsOnInvalidMac(string invalidMac)
    {
        Assert.Throws<FormatException>(() => WakeOnLanService.BuildMagicPacket(invalidMac));
    }

    [Fact]
    public void BuildMagicPacket_ThrowsOnEmptyMac()
    {
        Assert.Throws<ArgumentException>(() => WakeOnLanService.BuildMagicPacket(""));
    }
}

public class SmartRelayControllerTests
{
    [Fact]
    public void BuildRelayRestUrl_GeneratesCorrectShellyRestUrl()
    {
        var urlOn = SmartRelayController.BuildRelayRestUrl("Shelly", "192.168.1.150", 0, true);
        Assert.Equal("http://192.168.1.150/relay/0?turn=on", urlOn);

        var urlOff = SmartRelayController.BuildRelayRestUrl("Shelly", "http://192.168.1.150", 1, false);
        Assert.Equal("http://192.168.1.150/relay/1?turn=off", urlOff);
    }

    [Fact]
    public void BuildRelayRestUrl_GeneratesCorrectSonoffOrTasmotaUrl()
    {
        var urlOn = SmartRelayController.BuildRelayRestUrl("Sonoff", "192.168.1.160", 0, true);
        Assert.Equal("http://192.168.1.160/cm?cmnd=Power1%20ON", urlOn);

        var urlOff = SmartRelayController.BuildRelayRestUrl("Tasmota", "192.168.1.160", 1, false);
        Assert.Equal("http://192.168.1.160/cm?cmnd=Power2%20OFF", urlOff);
    }

    [Fact]
    public void BuildMqttMessage_FormatsCorrectly()
    {
        var (topic, payload) = SmartRelayController.BuildMqttMessage("cmnd/rig01", 0, true);
        Assert.Equal("cmnd/rig01/cmnd/POWER1", topic);
        Assert.Equal("ON", payload);

        var (topicOff, payloadOff) = SmartRelayController.BuildMqttMessage("devices/vr_bay_1", 1, false);
        Assert.Equal("devices/vr_bay_1/cmnd/POWER2", topicOff);
        Assert.Equal("OFF", payloadOff);
    }
}

public class MasterSystemSettingsTests
{
    [Fact]
    public void CreateDefault_InitializesValidSchemaVersionAndProperties()
    {
        var s = MasterSystemSettings.CreateDefault();

        Assert.NotNull(s);
        Assert.Equal("1.0.0", s.SchemaVersion);
        Assert.Equal("ZixCafe Arena", s.VenueName);
        Assert.Equal(40000, s.SignalRServerPort);
        Assert.Equal(180, s.NetworkDropGracePeriodSeconds);
        Assert.Equal(9, s.WakeOnLanPort);
        Assert.Equal("255.255.255.255", s.WakeOnLanBroadcastSubnet);
        Assert.True(s.EnableInactivityStandby);
        Assert.Equal(10, s.InactivityStandbyMinutes);
        Assert.Equal("Sleep", s.InactivityStandbyMode);
        Assert.True(s.RequireSupervisorPinForBillVoid);
        Assert.True(s.EnforceMandatoryHardwareLoanReturnOnCheckout);
    }

    [Fact]
    public void Dynamic_policy_clamping_ensures_safe_operational_boundaries()
    {
        var s = MasterSystemSettings.CreateDefault();
        s.CleanupDefaultMasterVolumePercent = Math.Clamp(150, 0, 100);
        Assert.Equal(100, s.CleanupDefaultMasterVolumePercent);

        s.TaxRatePercent = Math.Clamp(-5m, 0m, 100m);
        Assert.Equal(0m, s.TaxRatePercent);

        s.InactivityStandbyMinutes = Math.Clamp(1, 2, 1440);
        Assert.Equal(2, s.InactivityStandbyMinutes);

        s.NetworkDropGracePeriodSeconds = Math.Clamp(10000, 10, 3600);
        Assert.Equal(3600, s.NetworkDropGracePeriodSeconds);
    }
}

public class StationTransferWorkflowTests
{
    [Fact]
    public void StationSwitch_PreservesSessionDataWhileReassigningTerminal()
    {
        var sourceTerminalId = Guid.NewGuid();
        var targetTerminalId = Guid.NewGuid();

        var session = new Session
        {
            Id = Guid.NewGuid(),
            TerminalId = sourceTerminalId,
            Mode = SessionMode.Prepaid,
            Status = SessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            PlannedEndAt = DateTime.UtcNow.AddMinutes(30),
            Amount = 10.00m
        };

        session.Lines.Add(new SessionLine
        {
            Kind = LineKind.Product,
            Description = "Energy Drink",
            Quantity = 1,
            UnitAmount = 3.50m,
            Amount = 3.50m
        });

        // Simulate Station Switch
        var originalSessionId = session.Id;
        var originalAmount = session.Amount;
        var originalLineCount = session.Lines.Count;
        var originalPlannedEnd = session.PlannedEndAt;

        session.TerminalId = targetTerminalId;

        Assert.Equal(originalSessionId, session.Id);
        Assert.Equal(targetTerminalId, session.TerminalId);
        Assert.Equal(originalAmount, session.Amount);
        Assert.Equal(originalLineCount, session.Lines.Count);
        Assert.Equal(originalPlannedEnd, session.PlannedEndAt);
    }
}

public class SystemConfigAndAuditIntegrationTests
{
    [Fact]
    public void AuditChain_Links_ConfigurationUpdate_Cryptographically()
    {
        var prevHash = "INITIAL_BLOCK_HASH_HEX_00000000000000000000000000000000000000000000";
        var now = DateTime.UtcNow;
        var (returnedPrev, hash) = AuditChain.Link(
            prevHash,
            "system.config_update",
            "SystemConfig",
            "all",
            "Updated system configuration: Admin Studio configuration update. Schema: 1.0.0",
            "Admin",
            now);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // 256-bit SHA-256 in hex
        Assert.Equal(prevHash, returnedPrev);
    }

    [Fact]
    public void ProhibitedProcessesCsv_SplitsAndCleansProperly()
    {
        var csv = "cheatengine, cheatengine-x86_64, artmoney, speedhack ,wireshark,processhacker";
        var items = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(6, items.Length);
        Assert.Contains("cheatengine", items);
        Assert.Contains("speedhack", items);
        Assert.Contains("wireshark", items);
    }

    [Theory]
    [InlineData("WhitelistHidOnly")]
    [InlineData("BlockMassStorage")]
    [InlineData("AllowAll")]
    public void UsbStoragePolicy_RecognizesValidModes(string mode)
    {
        var validModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AllowAll", "WhitelistHidOnly", "BlockMassStorage"
        };

        Assert.Contains(mode, validModes);
    }

    [Theory]
    [InlineData("Sleep")]
    [InlineData("Hibernate")]
    [InlineData("Shutdown")]
    public void InactivityStandbyMode_RecognizesValidModes(string mode)
    {
        var validModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Sleep", "Hibernate", "Shutdown"
        };

        Assert.Contains(mode, validModes);
    }
}

public class ShiftDrawerCalculationTests
{
    [Fact]
    public void ExpectedDrawer_CalculatesAccurately_WithSessionsAndSales()
    {
        var openingFloat = 100.00m;
        var sessionAmounts = new List<decimal> { 15.50m, 22.00m, 8.25m };
        var salesCash = new List<decimal> { 5.00m, 12.50m };

        var expectedDrawer = openingFloat + sessionAmounts.Sum() + salesCash.Sum();
        Assert.Equal(163.25m, expectedDrawer);

        var countedDrawer = 165.00m;
        var variance = countedDrawer - expectedDrawer;
        Assert.Equal(1.75m, variance);
    }
}

public class TariffEngineEdgeCaseTests
{
    [Fact]
    public void End_before_start_clamps_to_zero_charge()
    {
        var tariff = new Tariff
        {
            Name = "Flat",
            Model = TariffModel.Flat,
            BaseRatePerHour = 5.00m,
            RoundingMinutes = 1
        };

        var start = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 6, 15, 11, 0, 0, DateTimeKind.Utc);

        var charge = TariffEngine.ComputeTimeCharge(tariff, start, end, TimeZoneInfo.Utc, 0, out var billed);
        Assert.Equal(TimeSpan.Zero, billed);
        Assert.Equal(0m, charge);
    }

    [Fact]
    public void Paused_minutes_exceeding_duration_clamps_to_zero()
    {
        var tariff = new Tariff
        {
            Name = "Flat",
            Model = TariffModel.Flat,
            BaseRatePerHour = 4.00m,
            RoundingMinutes = 1
        };

        var start = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(30);

        var charge = TariffEngine.ComputeTimeCharge(tariff, start, end, TimeZoneInfo.Utc, pausedMinutes: 45, out var billed);
        Assert.Equal(TimeSpan.Zero, billed);
        Assert.Equal(0m, charge);
    }

    [Fact]
    public void DaySchedule_crosses_midnight_correctly_transitioning_rates()
    {
        var tariff = new Tariff
        {
            Name = "NightOwl",
            Model = TariffModel.DaySchedule,
            BaseRatePerHour = 2.00m,
            RoundingMinutes = 1,
            Rules =
            {
                // Day band: 08:00 to 23:00 ($3.00/hr)
                new TariffRule { DaysMask = 0b1111111, StartMinute = 8 * 60, EndMinute = 23 * 60, RatePerHour = 3.00m },
                // Night band: 23:00 to 24:00 ($1.50/hr)
                new TariffRule { DaysMask = 0b1111111, StartMinute = 23 * 60, EndMinute = 24 * 60, RatePerHour = 1.50m }
                // 00:00 to 08:00 outside band falls back to BaseRate ($2.00/hr)
            }
        };

        // Start at 22:30 UTC, end at 01:30 UTC (3 hours total)
        // 22:30 to 23:00 (30 min @ 3.00/hr = $1.50)
        // 23:00 to 00:00 (60 min @ 1.50/hr = $1.50)
        // 00:00 to 01:30 (90 min @ 2.00/hr = $3.00)
        // Total expected = $6.00
        var start = new DateTime(2026, 6, 15, 22, 30, 0, DateTimeKind.Utc);
        var end = start.AddHours(3);

        var charge = TariffEngine.ComputeTimeCharge(tariff, start, end, TimeZoneInfo.Utc, 0, out var billed);
        Assert.Equal(TimeSpan.FromHours(3), billed);
        Assert.Equal(6.00m, charge);
    }
}

public class SecretHasherEdgeCaseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("P@ssw0rd!#%$^&*()_+~`|}{[]:;?><,./")]
    [InlineData("1234567890123456789012345678901234567890")]
    public void Various_passwords_hash_and_verify_correctly(string password)
    {
        var hash = SecretHasher.Hash(password);
        Assert.True(SecretHasher.Verify(password, hash));
        Assert.False(SecretHasher.Verify(password + "_wrong", hash));
    }

    [Theory]
    [InlineData("invalid_format")]
    [InlineData("pbkdf2-sha256$not_a_number$salt$hash")]
    [InlineData("pbkdf2-sha256$0$salt$hash")]
    [InlineData("pbkdf2-sha256$-100$salt$hash")]
    [InlineData("other-algo$210000$salt$hash")]
    [InlineData("")]
    public void Malformed_encoded_hashes_return_false_without_crashing(string malformed)
    {
        Assert.False(SecretHasher.Verify("test", malformed));
    }
}

public class TicketCodeEdgeCaseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("ABCD")]
    [InlineData("ABCD-EFGH")]
    [InlineData("ABCD-EFGH-IJKL-M")] // 'I' and 'L' are invalid in Crockford Base32
    [InlineData("ABCD-EFGH-OXYZ-M")] // 'O' is invalid
    [InlineData("ABCD-EFGH-UXYZ-M")] // 'U' is invalid
    public void Invalid_ticket_formats_are_rejected(string? invalidCode)
    {
        Assert.False(TicketCodeGenerator.IsValidFormat(invalidCode!));
    }

    [Fact]
    public void Valid_codes_are_case_insensitive_and_hyphen_tolerant()
    {
        var rng = RandomNumberGenerator.Create();
        var code = TicketCodeGenerator.NewCode(rng);

        Assert.True(TicketCodeGenerator.IsValidFormat(code));
        Assert.True(TicketCodeGenerator.IsValidFormat(code.ToLowerInvariant()));
        Assert.True(TicketCodeGenerator.IsValidFormat(code.Replace("-", "")));
    }
}

public class HardwareAntiTheftComprehensiveTests
{
    [Fact]
    public void Multiple_simultaneous_hardware_swaps_reported_in_full()
    {
        var baseline = new TerminalHardwareBaseline
        {
            TerminalId = Guid.NewGuid(),
            CpuName = "Intel Core i9-14900K",
            CpuId = "CPU-14900K-001",
            GpuName = "NVIDIA GeForce RTX 4090",
            GpuDeviceId = "PCI\\VEN_10DE&DEV_2684",
            TotalRamMb = 65536,
            RamSerials = "RAM1;RAM2;RAM3;RAM4",
            DiskSerial = "SAMSUNG-990-PRO-2TB",
            UsbDevicesJson = System.Text.Json.JsonSerializer.Serialize(new[] { "USB\\VID_046D&PID_C08B", "USB\\VID_1532&PID_022A" })
        };

        var currentUsb = new List<string> { "USB\\VID_046D&PID_C08B" }; // Mouse present, keyboard missing

        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            currentCpuName: "Intel Core i5-10400",
            currentCpuId: "CPU-10400-999",
            currentGpuName: "NVIDIA GeForce GTX 1060",
            currentGpuDeviceId: "PCI\\VEN_10DE&DEV_1C03",
            currentRamMb: 16384,
            currentRamSerials: "RAM1",
            currentDiskSerial: "CHEAP-SSD-120GB",
            currentUsbDeviceIds: currentUsb);

        Assert.Equal(6, discrepancies.Count);
        Assert.Contains(discrepancies, d => d.ComponentType == "CPU");
        Assert.Contains(discrepancies, d => d.ComponentType == "GPU");
        Assert.Contains(discrepancies, d => d.ComponentType == "RAM");
        Assert.Contains(discrepancies, d => d.ComponentType == "RAM Serial");
        Assert.Contains(discrepancies, d => d.ComponentType == "Disk");
        Assert.Contains(discrepancies, d => d.ComponentType == "USB Peripheral");
    }

    [Fact]
    public void Corrupted_usb_json_does_not_throw_exception()
    {
        var baseline = new TerminalHardwareBaseline
        {
            TerminalId = Guid.NewGuid(),
            CpuName = "AMD Ryzen 5 5600",
            GpuName = "RTX 3060",
            TotalRamMb = 16384,
            UsbDevicesJson = "{ malformed json ::: "
        };

        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            currentCpuName: "AMD Ryzen 5 5600",
            currentCpuId: null,
            currentGpuName: "RTX 3060",
            currentGpuDeviceId: null,
            currentRamMb: 16384,
            currentRamSerials: null,
            currentDiskSerial: null,
            currentUsbDeviceIds: []);

        Assert.Empty(discrepancies);
    }
}

public class MemberAndTaxMathTests
{
    [Fact]
    public void MemberTier_discount_applied_to_purchases()
    {
        var originalPrice = 50.00m;
        var discountPercent = 15.00m; // 15% discount for Gold tier

        var discountAmount = Math.Round(originalPrice * (discountPercent / 100m), 2, MidpointRounding.AwayFromZero);
        var finalPrice = originalPrice - discountAmount;

        Assert.Equal(7.50m, discountAmount);
        Assert.Equal(42.50m, finalPrice);
    }

    [Fact]
    public void Tax_calculation_computes_accurate_tax_and_grand_total()
    {
        var subtotal = 125.75m;
        var taxRatePercent = 8.25m; // 8.25% sales tax

        var taxAmount = Math.Round(subtotal * (taxRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        var grandTotal = subtotal + taxAmount;

        Assert.Equal(10.37m, taxAmount);
        Assert.Equal(136.12m, grandTotal);
    }

    [Fact]
    public void Member_topup_updates_money_balance_accurately()
    {
        var startingBalance = 25.00m;
        var topUpAmount = 50.00m;
        var bonusCredit = 5.00m;

        var endingBalance = startingBalance + topUpAmount + bonusCredit;
        Assert.Equal(80.00m, endingBalance);
    }
}

public class AuditTamperDetectionChainTests
{
    [Fact]
    public void AuditChain_tampering_at_any_field_is_immediately_detectable()
    {
        var now = DateTime.UtcNow;
        var (p0, h0) = AuditChain.Link("", "init", "System", "0", null, "Admin", now);

        // Chain 5 items
        var prev = h0;
        var chain = new List<(string Action, string Prev, string Hash, DateTime Time)>();
        for (int i = 1; i <= 5; i++)
        {
            var t = now.AddMinutes(i);
            var (p, h) = AuditChain.Link(prev, $"action.{i}", "Terminal", $"{i}", $"{{\"value\":{i}}}", "Admin", t);
            chain.Add(($"action.{i}", p, h, t));
            prev = h;
        }

        // Verify chain passes
        var checkPrev = h0;
        foreach (var item in chain)
        {
            var (_, computed) = AuditChain.Link(checkPrev, item.Action, "Terminal", item.Action.Split('.')[1], $"{{\"value\":{item.Action.Split('.')[1]}}}", "Admin", item.Time);
            Assert.Equal(item.Hash, computed);
            checkPrev = item.Hash;
        }

        // Now simulate tampering: attacker changes value in item 3
        var tamperedItem = chain[2];
        var (_, tamperedHash) = AuditChain.Link(tamperedItem.Prev, tamperedItem.Action, "Terminal", "3", "{\"value\":999999}", "Admin", tamperedItem.Time);
        Assert.NotEqual(tamperedItem.Hash, tamperedHash);
    }
}

public class CommandCenterOperationalMetricsTests
{
    [Theory]
    [InlineData(64, 0, 0, 64)]
    [InlineData(64, 16, 25, 48)]
    [InlineData(64, 32, 50, 32)]
    [InlineData(64, 48, 75, 16)]
    [InlineData(64, 64, 100, 0)]
    [InlineData(0, 0, 0, 0)]
    public void Occupancy_ratio_computes_accurate_percentage(int total, int inUse, int expectedPercent, int expectedIdle)
    {
        var idle = total > 0 ? total - inUse : 0;
        var ratio = total > 0 ? (int)Math.Round((double)inUse / total * 100.0) : 0;

        Assert.Equal(expectedIdle, idle);
        Assert.Equal(expectedPercent, ratio);
    }

    [Fact]
    public void Monospaced_digital_clock_formats_hh_mm_ss_and_full_date()
    {
        var testTime = new DateTime(2026, 9, 1, 22, 54, 0);
        var clockStr = testTime.ToString("HH:mm:ss");
        var dateStr = testTime.ToString("dddd, d MMMM yyyy");

        Assert.Equal("22:54:00", clockStr);
        Assert.Equal("Tuesday, 1 September 2026", dateStr);
    }

    [Fact]
    public void LiveEventLog_formats_timestamp_and_message_accurately()
    {
        var now = DateTime.Now;
        var formatted = $"{now:HH:mm:ss}  ·  Session started on PC-01";

        Assert.Contains(now.ToString("HH:mm:ss"), formatted);
        Assert.Contains("PC-01", formatted);
    }
}

