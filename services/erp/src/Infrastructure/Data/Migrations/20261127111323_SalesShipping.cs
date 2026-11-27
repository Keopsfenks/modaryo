using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SalesShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceivedBy",
                table: "shipments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Factor",
                table: "sales_return_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "sales_return_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_plan_consumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRowId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_plan_consumptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_plan_consumptions_InvoiceId",
                table: "invoice_plan_consumptions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_plan_consumptions_SalesOrderId",
                table: "invoice_plan_consumptions",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_plan_consumptions_TenantId",
                table: "invoice_plan_consumptions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_plan_consumptions");

            migrationBuilder.DropColumn(
                name: "ReceivedBy",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "Factor",
                table: "sales_return_lines");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "sales_return_lines");
        }
    }
}
