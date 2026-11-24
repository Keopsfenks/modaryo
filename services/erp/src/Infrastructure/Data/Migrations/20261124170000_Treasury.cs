using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Treasury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEmployee",
                table: "current_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "exchange_difference_settlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResultType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocationIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_difference_settlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exchange_difference_settlements_current_accounts_CurrentAcc~",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsIncome = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "journal_vouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalCaller = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Lines = table.Column<string>(type: "jsonb", nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_vouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "treasury_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_treasury_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DrawerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankBranch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EndorsementCount = table.Column<int>(type: "integer", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndorsedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtestFlag = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instruments_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instruments_current_accounts_EndorsedToId",
                        column: x => x.EndorsedToId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instruments_treasury_accounts_CollectionAccountId",
                        column: x => x.CollectionAccountId,
                        principalTable: "treasury_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    IsAdvance = table.Column<bool>(type: "boolean", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<string>(type: "text", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_finance_documents_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_documents_expense_categories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "expense_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_documents_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_documents_treasury_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "treasury_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_documents_treasury_accounts_TargetAccountId",
                        column: x => x.TargetAccountId,
                        principalTable: "treasury_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instrument_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    From = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    To = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instrument_events_finance_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "finance_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instrument_events_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "treasury_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_treasury_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_treasury_movements_finance_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "finance_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_treasury_movements_treasury_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "treasury_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlements_CurrentAccountId",
                table: "exchange_difference_settlements",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlements_TenantId",
                table: "exchange_difference_settlements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_difference_settlements_TenantId_CurrentAccountId",
                table: "exchange_difference_settlements",
                columns: new[] { "TenantId", "CurrentAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_categories_TenantId",
                table: "expense_categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_categories_TenantId_Code",
                table: "expense_categories",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_AccountId",
                table: "finance_documents",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_CurrentAccountId",
                table: "finance_documents",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_ExpenseCategoryId",
                table: "finance_documents",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_InstrumentId",
                table: "finance_documents",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_TargetAccountId",
                table: "finance_documents",
                column: "TargetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_TenantId",
                table: "finance_documents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_TenantId_CurrentAccountId_Date",
                table: "finance_documents",
                columns: new[] { "TenantId", "CurrentAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_TenantId_Kind_Date",
                table: "finance_documents",
                columns: new[] { "TenantId", "Kind", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_documents_TenantId_Number",
                table: "finance_documents",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instrument_events_DocumentId",
                table: "instrument_events",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_events_InstrumentId_Date",
                table: "instrument_events",
                columns: new[] { "InstrumentId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_instrument_events_TenantId",
                table: "instrument_events",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_CollectionAccountId",
                table: "instruments",
                column: "CollectionAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_CurrentAccountId",
                table: "instruments",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_EndorsedToId",
                table: "instruments",
                column: "EndorsedToId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_TenantId",
                table: "instruments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_TenantId_CurrentAccountId",
                table: "instruments",
                columns: new[] { "TenantId", "CurrentAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_instruments_TenantId_Status_DueDate",
                table: "instruments",
                columns: new[] { "TenantId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_TenantId",
                table: "journal_vouchers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_TenantId_Date",
                table: "journal_vouchers",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_TenantId_Number",
                table: "journal_vouchers",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_TenantId_SourceType_SourceId",
                table: "journal_vouchers",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_treasury_accounts_TenantId",
                table: "treasury_accounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treasury_accounts_TenantId_BranchId",
                table: "treasury_accounts",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_treasury_accounts_TenantId_Name",
                table: "treasury_accounts",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_treasury_movements_AccountId",
                table: "treasury_movements",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_treasury_movements_DocumentId",
                table: "treasury_movements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_treasury_movements_TenantId",
                table: "treasury_movements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treasury_movements_TenantId_AccountId_Date",
                table: "treasury_movements",
                columns: new[] { "TenantId", "AccountId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_difference_settlements");

            migrationBuilder.DropTable(
                name: "instrument_events");

            migrationBuilder.DropTable(
                name: "journal_vouchers");

            migrationBuilder.DropTable(
                name: "treasury_movements");

            migrationBuilder.DropTable(
                name: "finance_documents");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "treasury_accounts");

            migrationBuilder.DropColumn(
                name: "IsEmployee",
                table: "current_accounts");
        }
    }
}
