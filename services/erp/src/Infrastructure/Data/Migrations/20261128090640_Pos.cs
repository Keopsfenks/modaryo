using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pos_registers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_registers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_registers_treasury_accounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "treasury_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_registers_treasury_accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalTable: "treasury_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_registers_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_quick_buttons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    Row = table.Column<int>(type: "integer", nullable: false),
                    Col = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PresetQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_quick_buttons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_quick_buttons_pos_registers_RegisterId",
                        column: x => x.RegisterId,
                        principalTable: "pos_registers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pos_shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CountedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CloseNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_shifts_pos_registers_RegisterId",
                        column: x => x.RegisterId,
                        principalTable: "pos_registers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_cash_moves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_cash_moves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_cash_moves_pos_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "pos_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ChangeGiven = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_sales_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_sales_pos_registers_RegisterId",
                        column: x => x.RegisterId,
                        principalTable: "pos_registers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_sales_pos_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "pos_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_payments_pos_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "pos_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pos_returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExchangeSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_returns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_returns_pos_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "pos_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_returns_pos_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "pos_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_sale_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<int>(type: "integer", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CampaignDiscount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_sale_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_sale_lines_pos_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "pos_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pos_return_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_return_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_return_lines_pos_returns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "pos_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pos_return_lines_pos_sale_lines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "pos_sale_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pos_cash_moves_ShiftId",
                table: "pos_cash_moves",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_cash_moves_TenantId",
                table: "pos_cash_moves",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_payments_SaleId",
                table: "pos_payments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_payments_TenantId",
                table: "pos_payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_quick_buttons_RegisterId_Page_Row_Col",
                table: "pos_quick_buttons",
                columns: new[] { "RegisterId", "Page", "Row", "Col" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_quick_buttons_TenantId",
                table: "pos_quick_buttons",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_registers_CardAccountId",
                table: "pos_registers",
                column: "CardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_registers_CashAccountId",
                table: "pos_registers",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_registers_TenantId",
                table: "pos_registers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_registers_TenantId_BranchId",
                table: "pos_registers",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_pos_registers_TenantId_Code",
                table: "pos_registers",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_registers_WarehouseId",
                table: "pos_registers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_lines_ReturnId",
                table: "pos_return_lines",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_lines_SaleLineId",
                table: "pos_return_lines",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_lines_TenantId",
                table: "pos_return_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_returns_SaleId",
                table: "pos_returns",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_returns_ShiftId",
                table: "pos_returns",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_returns_TenantId",
                table: "pos_returns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_returns_TenantId_Number",
                table: "pos_returns",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_sale_lines_SaleId_Sort",
                table: "pos_sale_lines",
                columns: new[] { "SaleId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_sale_lines_TenantId",
                table: "pos_sale_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_CurrentAccountId",
                table: "pos_sales",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_RegisterId",
                table: "pos_sales",
                column: "RegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_ShiftId",
                table: "pos_sales",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_TenantId",
                table: "pos_sales",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_TenantId_Number",
                table: "pos_sales",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_sales_TenantId_RegisterId_Date",
                table: "pos_sales",
                columns: new[] { "TenantId", "RegisterId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_RegisterId",
                table: "pos_shifts",
                column: "RegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_TenantId",
                table: "pos_shifts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_TenantId_RegisterId",
                table: "pos_shifts",
                columns: new[] { "TenantId", "RegisterId" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_TenantId_RegisterId_OpenedAt",
                table: "pos_shifts",
                columns: new[] { "TenantId", "RegisterId", "OpenedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pos_cash_moves");

            migrationBuilder.DropTable(
                name: "pos_payments");

            migrationBuilder.DropTable(
                name: "pos_quick_buttons");

            migrationBuilder.DropTable(
                name: "pos_return_lines");

            migrationBuilder.DropTable(
                name: "pos_returns");

            migrationBuilder.DropTable(
                name: "pos_sale_lines");

            migrationBuilder.DropTable(
                name: "pos_sales");

            migrationBuilder.DropTable(
                name: "pos_shifts");

            migrationBuilder.DropTable(
                name: "pos_registers");
        }
    }
}
