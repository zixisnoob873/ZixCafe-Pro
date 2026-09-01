using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZixCafe.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminalSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecretHash",
                table: "Terminals",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecretHash",
                table: "Terminals");
        }
    }
}
