using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_instruments_TenantId_Kind_Direction_BankName_Number",
                table: "instruments",
                columns: new[] { "TenantId", "Kind", "Direction", "BankName", "Number" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_instruments_TenantId_Kind_Direction_BankName_Number",
                table: "instruments");
        }
    }
}
