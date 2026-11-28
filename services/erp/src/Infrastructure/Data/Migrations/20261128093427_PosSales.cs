using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "pos_sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "pos_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CardAmount",
                table: "pos_sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashNet",
                table: "pos_sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CashierName",
                table: "pos_sales",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientSaleId",
                table: "pos_sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "OnAccountAmount",
                table: "pos_sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnCreditAmount",
                table: "pos_sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "pos_sale_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "CardRefund",
                table: "pos_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashRefund",
                table: "pos_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CashierUserId",
                table: "pos_returns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeCredit",
                table: "pos_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "pos_returns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "Net",
                table: "pos_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OnAccountCredit",
                table: "pos_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Vat",
                table: "pos_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Net",
                table: "pos_return_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Vat",
                table: "pos_return_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_TenantId_ClientSaleId",
                table: "pos_sales",
                columns: new[] { "TenantId", "ClientSaleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pos_sales_TenantId_ClientSaleId",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "CardAmount",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "CashNet",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "CashierName",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "ClientSaleId",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "OnAccountAmount",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "ReturnCreditAmount",
                table: "pos_sales");

            migrationBuilder.DropColumn(
                name: "CardRefund",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "CashRefund",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "CashierUserId",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "ExchangeCredit",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "Net",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "OnAccountCredit",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "Vat",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "Net",
                table: "pos_return_lines");

            migrationBuilder.DropColumn(
                name: "Vat",
                table: "pos_return_lines");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "pos_sale_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);
        }
    }
}
