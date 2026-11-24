using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinalReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_series_TenantId_KindGroup_Mode_Prefix_Year",
                table: "document_series");

            migrationBuilder.AddColumn<string>(
                name: "Serials",
                table: "invoice_lines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_series_prefix_year",
                table: "document_series",
                columns: new[] { "TenantId", "Prefix", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_series_prefix_year",
                table: "document_series");

            migrationBuilder.DropColumn(
                name: "Serials",
                table: "invoice_lines");

            migrationBuilder.CreateIndex(
                name: "IX_document_series_TenantId_KindGroup_Mode_Prefix_Year",
                table: "document_series",
                columns: new[] { "TenantId", "KindGroup", "Mode", "Prefix", "Year" },
                unique: true);
        }
    }
}
