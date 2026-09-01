using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZixCafe.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaintenanceReason",
                table: "Terminals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservedUntilUtc",
                table: "Terminals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservedFor",
                table: "Terminals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CpuTemp",
                table: "Terminals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GpuTemp",
                table: "Terminals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RamPercent",
                table: "Terminals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiskFreeGb",
                table: "Terminals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFrozen",
                table: "Members",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Members",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "CashierName",
                table: "Sales",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "Sales",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeDue",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Sales",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Cash");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SaleLines",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Cashiers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateTable(
                name: "VenueSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VenueName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CurrencySymbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TaxLabel = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    DefaultOpeningFloat = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UsbRatePerGb = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PrintCostPerPage = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ClosingTime = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    LicenseKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AutoBackupPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AutoBackupIntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    LastBackupAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsConfigured = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TerminalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsFromCustomer = table.Column<bool>(type: "INTEGER", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertMutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MutedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertMutes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_TerminalId_SentAtUtc",
                table: "ChatEntries",
                columns: new[] { "TerminalId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_SessionId",
                table: "ChatEntries",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertMutes_Kind",
                table: "AlertMutes",
                column: "Kind",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "VenueSettings");
            migrationBuilder.DropTable(name: "ChatEntries");
            migrationBuilder.DropTable(name: "AlertMutes");

            migrationBuilder.DropColumn(name: "MaintenanceReason", table: "Terminals");
            migrationBuilder.DropColumn(name: "ReservedUntilUtc", table: "Terminals");
            migrationBuilder.DropColumn(name: "ReservedFor", table: "Terminals");
            migrationBuilder.DropColumn(name: "CpuTemp", table: "Terminals");
            migrationBuilder.DropColumn(name: "GpuTemp", table: "Terminals");
            migrationBuilder.DropColumn(name: "RamPercent", table: "Terminals");
            migrationBuilder.DropColumn(name: "DiskFreeGb", table: "Terminals");

            migrationBuilder.DropColumn(name: "IsFrozen", table: "Members");
            migrationBuilder.DropColumn(name: "Notes", table: "Members");
            migrationBuilder.DropColumn(name: "Category", table: "Products");
            migrationBuilder.DropColumn(name: "CashierName", table: "Sales");
            migrationBuilder.DropColumn(name: "CustomerName", table: "Sales");
            migrationBuilder.DropColumn(name: "ChangeDue", table: "Sales");
            migrationBuilder.DropColumn(name: "PaymentMethod", table: "Sales");
            migrationBuilder.DropColumn(name: "DiscountAmount", table: "SaleLines");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "Cashiers");
        }
    }
}
