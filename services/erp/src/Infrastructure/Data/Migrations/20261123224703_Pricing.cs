using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerGroupId",
                table: "current_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BuyQty = table.Column<int>(type: "integer", nullable: true),
                    PayQty = table.Column<int>(type: "integer", nullable: true),
                    Percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Days = table.Column<int[]>(type: "integer[]", nullable: true),
                    FromTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ToTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ProductIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    VariantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CategoryIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VatIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    tiers = table.Column<string>(type: "jsonb", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_prices_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_prices_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_prices_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_history_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_history_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VatIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseListId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdjustmentPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
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
                    table.PrimaryKey("PK_price_lists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_lists_current_accounts_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_lists_price_lists_BaseListId",
                        column: x => x.BaseListId,
                        principalTable: "price_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SalePriceListId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_groups_price_lists_SalePriceListId",
                        column: x => x.SalePriceListId,
                        principalTable: "price_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tiers = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_list_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_list_items_price_lists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "price_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_price_list_items_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_price_list_items_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_current_accounts_CustomerGroupId",
                table: "current_accounts",
                column: "CustomerGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_TenantId",
                table: "campaigns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_TenantId_IsActive",
                table: "campaigns",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_groups_SalePriceListId",
                table: "customer_groups",
                column: "SalePriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_groups_TenantId",
                table: "customer_groups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_groups_TenantId_Name",
                table: "customer_groups",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_prices_CurrentAccountId",
                table: "customer_prices",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_prices_ProductId",
                table: "customer_prices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_prices_TenantId",
                table: "customer_prices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_prices_TenantId_CurrentAccountId_ProductId",
                table: "customer_prices",
                columns: new[] { "TenantId", "CurrentAccountId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_prices_VariantId",
                table: "customer_prices",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_price_history_entries_TenantId",
                table: "price_history_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_price_history_entries_TenantId_ProductId_At",
                table: "price_history_entries",
                columns: new[] { "TenantId", "ProductId", "At" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_price_history_entries_TenantId_Scope_ScopeId_At",
                table: "price_history_entries",
                columns: new[] { "TenantId", "Scope", "ScopeId", "At" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_PriceListId_ProductId_Unit",
                table: "price_list_items",
                columns: new[] { "PriceListId", "ProductId", "Unit" },
                unique: true,
                filter: "\"VariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_PriceListId_ProductId_VariantId_Unit",
                table: "price_list_items",
                columns: new[] { "PriceListId", "ProductId", "VariantId", "Unit" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_ProductId",
                table: "price_list_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_TenantId",
                table: "price_list_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_TenantId_ProductId",
                table: "price_list_items",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_VariantId",
                table: "price_list_items",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_BaseListId",
                table: "price_lists",
                column: "BaseListId");

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_SupplierId",
                table: "price_lists",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_TenantId",
                table: "price_lists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_TenantId_Kind_IsActive",
                table: "price_lists",
                columns: new[] { "TenantId", "Kind", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_current_accounts_customer_groups_CustomerGroupId",
                table: "current_accounts",
                column: "CustomerGroupId",
                principalTable: "customer_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_current_accounts_customer_groups_CustomerGroupId",
                table: "current_accounts");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "customer_groups");

            migrationBuilder.DropTable(
                name: "customer_prices");

            migrationBuilder.DropTable(
                name: "price_history_entries");

            migrationBuilder.DropTable(
                name: "price_list_items");

            migrationBuilder.DropTable(
                name: "price_lists");

            migrationBuilder.DropIndex(
                name: "IX_current_accounts_CustomerGroupId",
                table: "current_accounts");

            migrationBuilder.DropColumn(
                name: "CustomerGroupId",
                table: "current_accounts");
        }
    }
}
