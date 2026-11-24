using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeriesDefaultIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tekil indeks öncesi: aynı (firma, şube, grup, mod) için birden çok varsayılan varsa en eskisi (en küçük Id) kalır.
            migrationBuilder.Sql("""
                UPDATE document_series SET "IsDefault" = false
                WHERE "IsDefault" AND "Id" NOT IN (
                    SELECT DISTINCT ON ("TenantId", "BranchId", "KindGroup", "Mode") "Id"
                    FROM document_series WHERE "IsDefault"
                    ORDER BY "TenantId", "BranchId", "KindGroup", "Mode", "Id");
                """);

            migrationBuilder.DropIndex(
                name: "IX_document_series_TenantId_BranchId_KindGroup_Mode",
                table: "document_series");

            migrationBuilder.CreateIndex(
                name: "IX_document_series_default",
                table: "document_series",
                columns: new[] { "TenantId", "BranchId", "KindGroup", "Mode" },
                unique: true,
                filter: "\"IsDefault\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_series_default",
                table: "document_series");

            migrationBuilder.CreateIndex(
                name: "IX_document_series_TenantId_BranchId_KindGroup_Mode",
                table: "document_series",
                columns: new[] { "TenantId", "BranchId", "KindGroup", "Mode" });
        }
    }
}
