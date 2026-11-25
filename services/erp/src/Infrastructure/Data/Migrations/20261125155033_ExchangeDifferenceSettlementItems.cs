using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeDifferenceSettlementItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_difference_settlement_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_difference_settlement_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exchange_difference_settlement_items_exchange_difference_se~",
                        column: x => x.SettlementId,
                        principalTable: "exchange_difference_settlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exchange_difference_settlement_items_ledger_entries_Invoice~",
                        column: x => x.InvoiceEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlement_items_InvoiceEntryId",
                table: "exchange_difference_settlement_items",
                column: "InvoiceEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlement_items_SettlementId",
                table: "exchange_difference_settlement_items",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlement_items_TenantId",
                table: "exchange_difference_settlement_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlement_items_TenantId_InvoiceEntryId",
                table: "exchange_difference_settlement_items",
                columns: new[] { "TenantId", "InvoiceEntryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_difference_settlement_items");
        }
    }
}
