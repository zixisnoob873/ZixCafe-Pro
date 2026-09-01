using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class PeripheralMeteringService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly VenueSettingsService _venueSettings;

    public PeripheralMeteringService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        VenueSettingsService venueSettings)
    {
        _dbFactory = dbFactory;
        _venueSettings = venueSettings;
    }

    public async Task<IReadOnlyList<PrintJobDto>> GetPrintJobsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var jobs = await db.PrintJobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(100)
            .ToListAsync();

        return jobs.Select(j => new PrintJobDto(
            j.Id,
            j.SessionId,
            null,
            j.PrinterName,
            j.Pages * j.Copies,
            j.Amount,
            j.Status.ToString(),
            j.CreatedAt
        )).ToList();
    }

    public async Task<ResultResponse> ReleasePrintJobAsync(Guid printJobId, string paymentMethod, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.PrintJobs.FirstOrDefaultAsync(j => j.Id == printJobId);
        if (job is null)
        {
            return new ResultResponse(false, "Print job not found.");
        }
        if (job.Status != PrintStatus.Queued)
        {
            return new ResultResponse(false, $"Job is already {job.Status}.");
        }

        job.Status = PrintStatus.Released;

        // Register sale for the print job
        if (job.Amount > 0)
        {
            var sale = new Sale
            {
                CashierName = cashierName,
                Subtotal = job.Amount,
                Total = job.Amount,
                PaidCash = paymentMethod == "Cash" ? job.Amount : 0m,
                PaidCard = paymentMethod == "Card" ? job.Amount : 0m,
                PaidQr = paymentMethod == "QR" ? job.Amount : 0m,
                PaymentMethod = paymentMethod,
                Note = $"Print job release: {job.PrinterName} ({job.Pages * job.Copies} pages)",
                CreatedAt = DateTime.UtcNow,
                Lines =
                {
                    new SaleLine
                    {
                        Kind = LineKind.Print,
                        Description = $"Print: {job.PrinterName} ({job.Pages * job.Copies} pages)",
                        Quantity = job.Pages * job.Copies,
                        UnitAmount = job.CostPerPage,
                        Amount = job.Amount
                    }
                }
            };
            db.Sales.Add(sale);
        }

        await db.AppendAuditAsync("print.release", "Peripheral", job.Id.ToString(), $"printer={job.PrinterName}, pages={job.Pages}, cost={job.Amount}", cashierName);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> CancelPrintJobAsync(Guid printJobId, string reason, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.PrintJobs.FirstOrDefaultAsync(j => j.Id == printJobId);
        if (job is null)
        {
            return new ResultResponse(false, "Print job not found.");
        }

        job.Status = PrintStatus.Cancelled;
        job.FailureReason = reason;
        await db.AppendAuditAsync("print.cancel", "Peripheral", job.Id.ToString(), $"reason={reason}", cashierName);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task RecordUsbTransferAsync(Guid terminalId, long bytesTransferred)
    {
        var settings = await _venueSettings.GetSettingsAsync();
        var mb = bytesTransferred / (1024 * 1024);
        if (mb <= 0)
        {
            return;
        }

        var ratePerGb = settings.UsbRatePerGb;
        var cost = (mb / 1024.0m) * ratePerGb;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var activeSession = await db.Sessions
            .Where(s => s.TerminalId == terminalId && s.Status == SessionStatus.Active)
            .FirstOrDefaultAsync();

        db.UsbTransferCharges.Add(new UsbTransferCharge
        {
            SessionId = activeSession?.Id,
            TerminalId = terminalId,
            BytesIn = bytesTransferred,
            BytesOut = 0,
            RatePerGb = ratePerGb,
            Amount = cost,
            Billed = activeSession is not null,
            CreatedAt = DateTime.UtcNow
        });

        if (activeSession is not null && cost > 0)
        {
            activeSession.Amount += cost;
            activeSession.Lines.Add(new SessionLine
            {
                SessionId = activeSession.Id,
                Kind = LineKind.Usb,
                Description = $"USB Transfer Metering ({mb} MB @ {ratePerGb:0.00}/GB [±1% precision])",
                Quantity = mb,
                UnitAmount = ratePerGb / 1024.0m,
                Amount = cost
            });
        }

        await db.SaveChangesAsync();
    }
}
