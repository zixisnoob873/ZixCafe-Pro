using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class TariffService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;

    public TariffService(IDbContextFactory<ZixCafeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tariffs = await db.Tariffs
            .Include(t => t.Rules)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Name)
            .ToListAsync();

        return tariffs.Select(t => new TariffDto(
            t.Id,
            t.Name,
            t.Model.ToString(),
            t.BaseRatePerHour,
            t.RoundingMinutes,
            t.MinimumCharge,
            t.Priority,
            t.Rules.Select(r => new TariffRuleDto(r.Id, r.DaysMask, r.StartMinute, r.EndMinute, r.RatePerHour)).ToList()
        )).ToList();
    }

    public async Task<ResultResponse> SaveTariffAsync(SaveTariffRequest request, string requestingCashier)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ResultResponse(false, "Tariff name cannot be empty.");
        }
        if (request.BaseRatePerHour < 0)
        {
            return new ResultResponse(false, "Base rate per hour cannot be negative.");
        }
        if (request.RoundingMinutes < 1)
        {
            return new ResultResponse(false, "Rounding minutes must be at least 1.");
        }

        if (!Enum.TryParse<TariffModel>(request.Model, true, out var model))
        {
            model = TariffModel.Flat;
        }

        // Validate schedule rules if DaySchedule
        if (model == TariffModel.DaySchedule)
        {
            foreach (var rule in request.Rules)
            {
                if (rule.StartMinute < 0 || rule.EndMinute > 1440 || rule.StartMinute >= rule.EndMinute)
                {
                    return new ResultResponse(false, "Invalid time range in tariff rules (0-1440 minutes).");
                }
                if (rule.RatePerHour < 0)
                {
                    return new ResultResponse(false, "Rule rate per hour cannot be negative.");
                }
            }
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        Tariff? tariff = null;

        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
        {
            tariff = await db.Tariffs.Include(t => t.Rules).FirstOrDefaultAsync(t => t.Id == request.Id.Value);
            if (tariff is null)
            {
                return new ResultResponse(false, "Tariff not found.");
            }
        }
        else
        {
            tariff = new Tariff();
            db.Tariffs.Add(tariff);
        }

        tariff.Name = request.Name.Trim();
        tariff.Model = model;
        tariff.BaseRatePerHour = request.BaseRatePerHour;
        tariff.RoundingMinutes = request.RoundingMinutes;
        tariff.MinimumCharge = request.MinimumCharge;
        tariff.Priority = request.Priority;

        // Sync rules
        tariff.Rules.Clear();
        foreach (var r in request.Rules)
        {
            tariff.Rules.Add(new TariffRule
            {
                DaysMask = r.DaysMask,
                StartMinute = r.StartMinute,
                EndMinute = r.EndMinute,
                RatePerHour = r.RatePerHour
            });
        }

        await db.AppendAuditAsync("tariff.save", "Tariff", tariff.Id.ToString(), $"name={tariff.Name}, rate={tariff.BaseRatePerHour}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> DeleteTariffAsync(Guid tariffId, string requestingCashier)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tariff = await db.Tariffs.Include(t => t.Rules).FirstOrDefaultAsync(t => t.Id == tariffId);
        if (tariff is null)
        {
            return new ResultResponse(false, "Tariff not found.");
        }

        // Prevent deleting if it's the only tariff
        var count = await db.Tariffs.CountAsync();
        if (count <= 1)
        {
            return new ResultResponse(false, "Cannot delete the only remaining tariff.");
        }

        db.Tariffs.Remove(tariff);
        await db.AppendAuditAsync("tariff.delete", "Tariff", tariffId.ToString(), $"deleted {tariff.Name}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }
}
