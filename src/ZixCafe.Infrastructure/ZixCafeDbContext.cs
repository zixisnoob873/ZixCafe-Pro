using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;

namespace ZixCafe.Infrastructure;

public class ZixCafeDbContext(DbContextOptions<ZixCafeDbContext> options) : DbContext(options)
{
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<TariffRule> TariffRules => Set<TariffRule>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionLine> SessionLines => Set<SessionLine>();
    public DbSet<MemberTier> MemberTiers => Set<MemberTier>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberTransaction> MemberTransactions => Set<MemberTransaction>();
    public DbSet<TicketVoucher> Tickets => Set<TicketVoucher>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<UsbTransferCharge> UsbTransferCharges => Set<UsbTransferCharge>();
    public DbSet<ItemLoan> ItemLoans => Set<ItemLoan>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Cashier> Cashiers => Set<Cashier>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ProhibitedApp> ProhibitedApps => Set<ProhibitedApp>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<WaitQueueEntry> WaitQueue => Set<WaitQueueEntry>();
    public DbSet<VenueSettings> VenueSettings => Set<VenueSettings>();
    public DbSet<ChatEntry> ChatEntries => Set<ChatEntry>();
    public DbSet<AlertMute> AlertMutes => Set<AlertMute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Zone>(e =>
        {
            e.Property(z => z.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(z => z.DisplayOrder);
        });

        modelBuilder.Entity<Terminal>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Name).HasMaxLength(40).IsRequired();
            e.Property(t => t.SecretHash).HasMaxLength(128);
            e.HasOne(t => t.Zone)
                .WithMany(z => z.Terminals)
                .HasForeignKey(t => t.ZoneId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.LastSeenAt);
        });

        modelBuilder.Entity<Tariff>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Name).HasMaxLength(80).IsRequired();
            e.Property(t => t.BaseRatePerHour).HasPrecision(18, 4);
            e.Property(t => t.MinimumCharge).HasPrecision(18, 4);
            e.HasMany(t => t.Rules)
                .WithOne(r => r.Tariff)
                .HasForeignKey(r => r.TariffId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TariffRule>(e =>
        {
            e.Property(r => r.RatePerHour).HasPrecision(18, 4);
            e.HasIndex(r => new { r.TariffId, r.StartMinute });
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.Property(s => s.Amount).HasPrecision(18, 4);
            e.Property(s => s.CreditApplied).HasPrecision(18, 4);
            e.HasOne(s => s.Terminal)
                .WithMany(t => t.Sessions)
                .HasForeignKey(s => s.TerminalId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Member)
                .WithMany()
                .HasForeignKey(s => s.MemberId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(s => s.Ticket)
                .WithMany()
                .HasForeignKey(s => s.TicketId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(s => s.Tariff)
                .WithMany()
                .HasForeignKey(s => s.TariffId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => new { s.Status, s.StartedAt });
            e.HasIndex(s => s.TerminalId);
        });

        modelBuilder.Entity<SessionLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.UnitAmount).HasPrecision(18, 4);
            e.Property(l => l.Amount).HasPrecision(18, 4);
            e.Property(l => l.Description).HasMaxLength(200).IsRequired();
            e.HasOne(l => l.Session)
                .WithMany(s => s.Lines)
                .HasForeignKey(l => l.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.SessionId);
        });

        modelBuilder.Entity<MemberTier>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Name).HasMaxLength(60).IsRequired();
            e.Property(t => t.DiscountPercent).HasPrecision(5, 2);
            e.Property(t => t.MinTopUpAmount).HasPrecision(18, 4);
        });

        modelBuilder.Entity<Member>(e =>
        {
            e.HasIndex(m => m.Code).IsUnique();
            e.Property(m => m.Code).HasMaxLength(20).IsRequired();
            e.Property(m => m.Name).HasMaxLength(120).IsRequired();
            e.Property(m => m.Phone).HasMaxLength(30);
            e.Property(m => m.MoneyBalance).HasPrecision(18, 4);
            e.HasOne(m => m.Tier)
                .WithMany()
                .HasForeignKey(m => m.TierId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MemberTransaction>(e =>
        {
            e.Property(t => t.Amount).HasPrecision(18, 4);
            e.Property(t => t.BalanceAfter).HasPrecision(18, 4);
            e.Property(t => t.Kind).HasMaxLength(30).IsRequired();
            e.Property(t => t.CashierName).HasMaxLength(120);
            e.Property(t => t.Note).HasMaxLength(300);
            e.HasOne(t => t.Member)
                .WithMany(m => m.Transactions)
                .HasForeignKey(t => t.MemberId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.MemberId, t.CreatedAt });
        });

        modelBuilder.Entity<TicketVoucher>(e =>
        {
            e.HasIndex(t => t.Code).IsUnique();
            e.Property(t => t.Code).HasMaxLength(24).IsRequired();
            e.Property(t => t.CreditAmount).HasPrecision(18, 4);
            e.Property(t => t.Price).HasPrecision(18, 4);
            e.Property(t => t.BatchRef).HasMaxLength(40);
            e.Property(t => t.IssuedBy).HasMaxLength(120);
            e.HasIndex(t => new { t.IsUsed, t.CreatedAt });
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Sku).HasMaxLength(30).IsRequired();
            e.Property(p => p.Name).HasMaxLength(120).IsRequired();
            e.Property(p => p.Category).HasMaxLength(60).IsRequired();
            e.Property(p => p.Price).HasPrecision(18, 4);
        });

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.Property(m => m.Reference).HasMaxLength(60);
            e.Property(m => m.CashierName).HasMaxLength(120);
            e.HasOne(m => m.Product)
                .WithMany(p => p.Movements)
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.ProductId, m.CreatedAt });
        });

        modelBuilder.Entity<Sale>(e =>
        {
            e.Property(s => s.Subtotal).HasPrecision(18, 4);
            e.Property(s => s.Discount).HasPrecision(18, 4);
            e.Property(s => s.Total).HasPrecision(18, 4);
            e.Property(s => s.PaidCash).HasPrecision(18, 4);
            e.Property(s => s.PaidCard).HasPrecision(18, 4);
            e.Property(s => s.PaidQr).HasPrecision(18, 4);
            e.Property(s => s.ChangeDue).HasPrecision(18, 4);
            e.Property(s => s.PaymentMethod).HasMaxLength(30).IsRequired();
            e.Property(s => s.CashierName).HasMaxLength(120);
            e.Property(s => s.CustomerName).HasMaxLength(120);
            e.Property(s => s.Note).HasMaxLength(300);
            e.HasOne(s => s.Session)
                .WithMany()
                .HasForeignKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(s => s.Cashier)
                .WithMany()
                .HasForeignKey(s => s.CashierId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.CreatedAt);
        });

        modelBuilder.Entity<SaleLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.UnitAmount).HasPrecision(18, 4);
            e.Property(l => l.DiscountAmount).HasPrecision(18, 4);
            e.Property(l => l.Amount).HasPrecision(18, 4);
            e.Property(l => l.Description).HasMaxLength(200).IsRequired();
            e.HasOne(l => l.Sale)
                .WithMany(s => s.Lines)
                .HasForeignKey(l => l.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrintJob>(e =>
        {
            e.Property(j => j.CostPerPage).HasPrecision(18, 4);
            e.Property(j => j.Amount).HasPrecision(18, 4);
            e.Property(j => j.PrinterName).HasMaxLength(120).IsRequired();
            e.Property(j => j.FailureReason).HasMaxLength(300);
        });

        modelBuilder.Entity<UsbTransferCharge>(e =>
        {
            e.Property(c => c.RatePerGb).HasPrecision(18, 4);
            e.Property(c => c.Amount).HasPrecision(18, 4);
        });

        modelBuilder.Entity<ItemLoan>(e =>
        {
            e.Property(l => l.ItemName).HasMaxLength(120).IsRequired();
            e.Property(l => l.DepositAmount).HasPrecision(18, 4);
            e.Property(l => l.HeldBy).HasMaxLength(120);
            e.Property(l => l.ReturnedTo).HasMaxLength(120);
        });

        modelBuilder.Entity<AlertEvent>(e =>
        {
            e.Property(a => a.Kind).HasMaxLength(40).IsRequired();
            e.Property(a => a.Message).HasMaxLength(500).IsRequired();
            e.HasIndex(a => new { a.AcknowledgedAt, a.CreatedAt });
        });

        modelBuilder.Entity<Shift>(e =>
        {
            e.Property(s => s.OpeningFloat).HasPrecision(18, 4);
            e.Property(s => s.ExpectedDrawer).HasPrecision(18, 4);
            e.Property(s => s.CountedDrawer).HasPrecision(18, 4);
            e.Property(s => s.ClosingNote).HasMaxLength(500);
            e.HasOne(s => s.Cashier)
                .WithMany(c => c.Shifts)
                .HasForeignKey(s => s.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.StartedAt);
        });

        modelBuilder.Entity<Cashier>(e =>
        {
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Name).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(60).IsRequired();
            e.Property(a => a.TargetType).HasMaxLength(60).IsRequired();
            e.Property(a => a.TargetId).HasMaxLength(60);
            e.Property(a => a.CashierName).HasMaxLength(120);
            e.Property(a => a.PrevHash).HasMaxLength(64).IsRequired();
            e.Property(a => a.Hash).HasMaxLength(64).IsRequired();
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => a.Hash).IsUnique();
        });

        modelBuilder.Entity<ProhibitedApp>(e =>
        {
            e.Property(p => p.Match).HasMaxLength(200).IsRequired();
            e.Property(p => p.MatchKind).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(80).IsRequired();
            e.Property(s => s.Value).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<WaitQueueEntry>(e =>
        {
            e.Property(q => q.GuestName).HasMaxLength(120).IsRequired();
            e.Property(q => q.Contact).HasMaxLength(120);
            e.Property(q => q.ServedBy).HasMaxLength(120);
        });

        modelBuilder.Entity<VenueSettings>(e =>
        {
            e.Property(v => v.VenueName).HasMaxLength(120).IsRequired();
            e.Property(v => v.CurrencyCode).HasMaxLength(10).IsRequired();
            e.Property(v => v.CurrencySymbol).HasMaxLength(10).IsRequired();
            e.Property(v => v.Locale).HasMaxLength(20).IsRequired();
            e.Property(v => v.TaxLabel).HasMaxLength(30).IsRequired();
            e.Property(v => v.TaxRatePercent).HasPrecision(5, 2);
            e.Property(v => v.DefaultOpeningFloat).HasPrecision(18, 4);
            e.Property(v => v.UsbRatePerGb).HasPrecision(18, 4);
            e.Property(v => v.PrintCostPerPage).HasPrecision(18, 4);
            e.Property(v => v.ClosingTime).HasMaxLength(10).IsRequired();
            e.Property(v => v.LicenseKey).HasMaxLength(200);
            e.Property(v => v.AutoBackupPath).HasMaxLength(500);
        });

        modelBuilder.Entity<ChatEntry>(e =>
        {
            e.Property(c => c.FromName).HasMaxLength(120).IsRequired();
            e.Property(c => c.Message).HasMaxLength(1000).IsRequired();
            e.HasIndex(c => new { c.TerminalId, c.SentAtUtc });
            e.HasIndex(c => c.SessionId);
        });

        modelBuilder.Entity<AlertMute>(e =>
        {
            e.Property(a => a.Kind).HasMaxLength(40).IsRequired();
            e.HasIndex(a => a.Kind).IsUnique();
        });
    }
}
