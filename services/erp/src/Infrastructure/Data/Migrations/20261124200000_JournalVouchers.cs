using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class JournalVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "journal_vouchers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "journal_vouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_TenantId_ExternalCaller_SourceType_SourceId",
                table: "journal_vouchers",
                columns: new[] { "TenantId", "ExternalCaller", "SourceType", "SourceId" },
                unique: true,
                filter: "\"ExternalCaller\" IS NOT NULL AND \"SourceId\" IS NOT NULL AND NOT \"IsCancelled\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journal_vouchers_TenantId_ExternalCaller_SourceType_SourceId",
                table: "journal_vouchers");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "journal_vouchers");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "journal_vouchers");
        }
    }
}
