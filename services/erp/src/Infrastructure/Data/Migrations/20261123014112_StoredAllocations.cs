using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoredAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_due_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_due_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ledger_due_lines_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_due_lines_ledger_entries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_allocations", x => x.Id);
                    table.CheckConstraint("CK_ledger_allocations_Amount_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ledger_allocations_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_allocations_ledger_due_lines_CreditLineId",
                        column: x => x.CreditLineId,
                        principalTable: "ledger_due_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ledger_allocations_ledger_due_lines_DebitLineId",
                        column: x => x.DebitLineId,
                        principalTable: "ledger_due_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_allocations_CreditLineId",
                table: "ledger_allocations",
                column: "CreditLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_allocations_CurrentAccountId",
                table: "ledger_allocations",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_allocations_DebitLineId",
                table: "ledger_allocations",
                column: "DebitLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_allocations_TenantId",
                table: "ledger_allocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_due_lines_CurrentAccountId",
                table: "ledger_due_lines",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_due_lines_EntryId_Sequence",
                table: "ledger_due_lines",
                columns: new[] { "EntryId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_due_lines_TenantId",
                table: "ledger_due_lines",
                column: "TenantId");

            // Tek seferlik veri adımlarının işaret tablosu (modelde yok). Mevcut hareketlerin vade satırları ve
            // eşleştirmeleri ApplicationDbContextInitialiser'daki StoredAllocationsBackfill ile doldurulur.
            migrationBuilder.Sql(
                "CREATE TABLE IF NOT EXISTS \"__modaryo_backfill\" (name text PRIMARY KEY, applied_at timestamptz NOT NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"__modaryo_backfill\";");

            migrationBuilder.DropTable(
                name: "ledger_allocations");

            migrationBuilder.DropTable(
                name: "ledger_due_lines");
        }
    }
}
