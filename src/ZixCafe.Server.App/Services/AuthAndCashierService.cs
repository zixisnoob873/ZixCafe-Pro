using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class AuthAndCashierService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;

    public AuthAndCashierService(IDbContextFactory<ZixCafeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cashier = await db.Cashiers.FirstOrDefaultAsync(c => c.Name == request.Name && c.IsActive);
        if (cashier is null)
        {
            return new LoginResponse(false, "Invalid username or password.", "Staff");
        }

        if (!SecretHasher.Verify(request.Pin, cashier.PinHash))
        {
            return new LoginResponse(false, "Invalid username or password.", "Staff");
        }

        return new LoginResponse(true, null, cashier.Role.ToString());
    }

    public async Task<bool> VerifyManagerPinAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var managers = await db.Cashiers
            .Where(c => c.IsActive && (c.Role == CashierRole.Manager || c.Role == CashierRole.Owner))
            .ToListAsync();

        return managers.Any(m => SecretHasher.Verify(pin, m.PinHash));
    }

    public async Task<Cashier?> GetCashierByNameAsync(string name)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Cashiers.FirstOrDefaultAsync(c => c.Name == name && c.IsActive);
    }

    public async Task<IReadOnlyList<CashierDto>> GetCashiersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var list = await db.Cashiers
            .OrderBy(c => c.Name)
            .ToListAsync();

        return list.Select(c => new CashierDto(
            c.Id,
            c.Name,
            c.Role.ToString(),
            c.IsActive,
            c.CreatedAt)).ToList();
    }

    public async Task<ResultResponse> CreateCashierAsync(CreateCashierRequest request, string requestingCashier)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ResultResponse(false, "Cashier name cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(request.Pin) || request.Pin.Length < 4)
        {
            return new ResultResponse(false, "PIN must be at least 4 digits.");
        }

        if (!Enum.TryParse<CashierRole>(request.Role, true, out var role))
        {
            role = CashierRole.Staff;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var exists = await db.Cashiers.AnyAsync(c => c.Name.ToLower() == request.Name.ToLower());
        if (exists)
        {
            return new ResultResponse(false, "A cashier with this name already exists.");
        }

        var cashier = new Cashier
        {
            Name = request.Name.Trim(),
            PinHash = SecretHasher.Hash(request.Pin.Trim()),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Cashiers.Add(cashier);

        await db.AppendAuditAsync("cashier.create", "Cashier", cashier.Id.ToString(), $"name={cashier.Name}, role={role}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> UpdateCashierAsync(UpdateCashierRequest request, string requestingCashier)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cashier = await db.Cashiers.FirstOrDefaultAsync(c => c.Id == request.Id);
        if (cashier is null)
        {
            return new ResultResponse(false, "Cashier not found.");
        }

        if (!Enum.TryParse<CashierRole>(request.Role, true, out var role))
        {
            role = cashier.Role;
        }

        // Guard against disabling cashier with active shift
        if (!request.IsActive && cashier.IsActive)
        {
            var hasOpenShift = await db.Shifts.AnyAsync(s => s.CashierId == cashier.Id && s.EndedAt == null);
            if (hasOpenShift)
            {
                return new ResultResponse(false, "Cannot disable a cashier with an open shift.");
            }
        }

        cashier.Name = request.Name.Trim();
        cashier.Role = role;
        cashier.IsActive = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.NewPin) && request.NewPin.Trim().Length >= 4)
        {
            cashier.PinHash = SecretHasher.Hash(request.NewPin.Trim());
        }

        await db.AppendAuditAsync("cashier.update", "Cashier", cashier.Id.ToString(), $"name={cashier.Name}, role={role}, active={cashier.IsActive}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }
}
