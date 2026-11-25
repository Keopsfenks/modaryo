using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Incoterm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_files_current_accounts_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CloseReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_orders_current_accounts_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_file_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_file_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_file_links_import_files_ImportFileId",
                        column: x => x.ImportFileId,
                        principalTable: "import_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "landed_costs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImportFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landed_costs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landed_costs_import_files_ImportFileId",
                        column: x => x.ImportFileId,
                        principalTable: "import_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    SupplierDeliveryNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_goods_receipts_current_accounts_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goods_receipts_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goods_receipts_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Width = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Length = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    VatRate = table.Column<int>(type: "integer", nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ClosedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "landed_cost_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LandedCostId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CostType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AmountTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landed_cost_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landed_cost_sources_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_landed_cost_sources_landed_costs_LandedCostId",
                        column: x => x.LandedCostId,
                        principalTable: "landed_costs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitCostTl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    AdjustedUnitCostTl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quarantine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipt_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_goods_receipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "goods_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_purchase_order_lines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "purchase_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "landed_cost_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LandedCostId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    TlValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    WeightKgPerUnit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    AllocatedTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landed_cost_targets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landed_cost_targets_goods_receipt_lines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "goods_receipt_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_landed_cost_targets_goods_receipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "goods_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_landed_cost_targets_landed_costs_LandedCostId",
                        column: x => x.LandedCostId,
                        principalTable: "landed_costs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_GoodsReceiptId_Sort",
                table: "goods_receipt_lines",
                columns: new[] { "GoodsReceiptId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_ProductId",
                table: "goods_receipt_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_PurchaseOrderLineId",
                table: "goods_receipt_lines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_TenantId",
                table: "goods_receipt_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_VariantId",
                table: "goods_receipt_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_WarehouseId",
                table: "goods_receipt_lines",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_PurchaseOrderId",
                table: "goods_receipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_SupplierId",
                table: "goods_receipts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_TenantId",
                table: "goods_receipts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_TenantId_Date",
                table: "goods_receipts",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_TenantId_Number",
                table: "goods_receipts",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_TenantId_PurchaseOrderId",
                table: "goods_receipts",
                columns: new[] { "TenantId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_WarehouseId",
                table: "goods_receipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_import_file_links_ImportFileId_Kind_RefId",
                table: "import_file_links",
                columns: new[] { "ImportFileId", "Kind", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_file_links_TenantId",
                table: "import_file_links",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_import_files_SupplierId",
                table: "import_files",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_import_files_TenantId",
                table: "import_files",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_import_files_TenantId_Number",
                table: "import_files",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_sources_InvoiceId",
                table: "landed_cost_sources",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_sources_LandedCostId",
                table: "landed_cost_sources",
                column: "LandedCostId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_sources_TenantId",
                table: "landed_cost_sources",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_targets_GoodsReceiptId",
                table: "landed_cost_targets",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_targets_LandedCostId_ReceiptLineId",
                table: "landed_cost_targets",
                columns: new[] { "LandedCostId", "ReceiptLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_targets_ReceiptLineId",
                table: "landed_cost_targets",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_cost_targets_TenantId",
                table: "landed_cost_targets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_costs_ImportFileId",
                table: "landed_costs",
                column: "ImportFileId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_costs_TenantId",
                table: "landed_costs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_landed_costs_TenantId_Number",
                table: "landed_costs",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_ProductId",
                table: "purchase_order_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_PurchaseOrderId_Sort",
                table: "purchase_order_lines",
                columns: new[] { "PurchaseOrderId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_SalesOrderLineId",
                table: "purchase_order_lines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_TenantId",
                table: "purchase_order_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_VariantId",
                table: "purchase_order_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SupplierId",
                table: "purchase_orders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_TenantId",
                table: "purchase_orders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_TenantId_Number",
                table: "purchase_orders",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_TenantId_Status_Date",
                table: "purchase_orders",
                columns: new[] { "TenantId", "Status", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_TenantId_SupplierId",
                table: "purchase_orders",
                columns: new[] { "TenantId", "SupplierId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_file_links");

            migrationBuilder.DropTable(
                name: "landed_cost_sources");

            migrationBuilder.DropTable(
                name: "landed_cost_targets");

            migrationBuilder.DropTable(
                name: "goods_receipt_lines");

            migrationBuilder.DropTable(
                name: "landed_costs");

            migrationBuilder.DropTable(
                name: "goods_receipts");

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "import_files");

            migrationBuilder.DropTable(
                name: "purchase_orders");
        }
    }
}
