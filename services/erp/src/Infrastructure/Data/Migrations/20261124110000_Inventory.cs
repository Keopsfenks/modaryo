using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBundle",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TrackingMode",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bundle_components",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotNo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lots_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "average_cost_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    AverageCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_average_cost_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_average_cost_snapshots_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "min_stock_levels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    MinQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ReorderQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_min_stock_levels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_min_stock_levels_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_min_stock_levels_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedReceiptSourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reservations_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reservations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revaluation_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revaluation_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_revaluation_jobs_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "serial_numbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serial_numbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_serial_numbers_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_numbers_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_numbers_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SnapshotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_counts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_counts_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_movements_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CountedQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_count_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_count_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_count_lines_stock_counts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "stock_counts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cost_layers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboundMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    OriginalQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RemainingQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_layers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cost_layers_stock_movements_InboundMovementId",
                        column: x => x.InboundMovementId,
                        principalTable: "stock_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cost_layers_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movement_lot_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movement_lot_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movement_lot_lines_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movement_lot_lines_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movement_lot_lines_stock_movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "stock_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_average_cost_snapshots_TenantId",
                table: "average_cost_snapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_average_cost_snapshots_TenantId_WarehouseId_ProductId",
                table: "average_cost_snapshots",
                columns: new[] { "TenantId", "WarehouseId", "ProductId" },
                unique: true,
                filter: "\"VariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_average_cost_snapshots_TenantId_WarehouseId_ProductId_Varia~",
                table: "average_cost_snapshots",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "VariantId" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_average_cost_snapshots_WarehouseId",
                table: "average_cost_snapshots",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_cost_layers_InboundMovementId",
                table: "cost_layers",
                column: "InboundMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_cost_layers_TenantId",
                table: "cost_layers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_cost_layers_TenantId_WarehouseId_ProductId_VariantId_Date_S~",
                table: "cost_layers",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "VariantId", "Date", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_cost_layers_WarehouseId",
                table: "cost_layers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_ProductId",
                table: "lots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_TenantId",
                table: "lots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_TenantId_ProductId_LotNo",
                table: "lots",
                columns: new[] { "TenantId", "ProductId", "LotNo" },
                unique: true,
                filter: "\"VariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lots_TenantId_ProductId_VariantId_LotNo",
                table: "lots",
                columns: new[] { "TenantId", "ProductId", "VariantId", "LotNo" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lots_VariantId",
                table: "lots",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_min_stock_levels_ProductId",
                table: "min_stock_levels",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_min_stock_levels_TenantId",
                table: "min_stock_levels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_min_stock_levels_TenantId_WarehouseId_ProductId",
                table: "min_stock_levels",
                columns: new[] { "TenantId", "WarehouseId", "ProductId" },
                unique: true,
                filter: "\"VariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_min_stock_levels_TenantId_WarehouseId_ProductId_VariantId",
                table: "min_stock_levels",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "VariantId" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_min_stock_levels_WarehouseId",
                table: "min_stock_levels",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_movement_lot_lines_LotId",
                table: "movement_lot_lines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_movement_lot_lines_MovementId",
                table: "movement_lot_lines",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_movement_lot_lines_SerialId",
                table: "movement_lot_lines",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_ProductId",
                table: "reservations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_TenantId",
                table: "reservations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_TenantId_SourceType_SourceId",
                table: "reservations",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_TenantId_WarehouseId_ProductId_VariantId_Status",
                table: "reservations",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "VariantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_WarehouseId",
                table: "reservations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_revaluation_jobs_TenantId",
                table: "revaluation_jobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_revaluation_jobs_TenantId_WarehouseId_ProductId",
                table: "revaluation_jobs",
                columns: new[] { "TenantId", "WarehouseId", "ProductId" },
                unique: true,
                filter: "\"VariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_revaluation_jobs_TenantId_WarehouseId_ProductId_VariantId",
                table: "revaluation_jobs",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "VariantId" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_revaluation_jobs_WarehouseId",
                table: "revaluation_jobs",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_ProductId",
                table: "serial_numbers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_TenantId",
                table: "serial_numbers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_TenantId_ProductId_Serial",
                table: "serial_numbers",
                columns: new[] { "TenantId", "ProductId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_TenantId_WarehouseId",
                table: "serial_numbers",
                columns: new[] { "TenantId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_VariantId",
                table: "serial_numbers",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_WarehouseId",
                table: "serial_numbers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_ProductId",
                table: "stock_count_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_StockCountId_ProductId_VariantId",
                table: "stock_count_lines",
                columns: new[] { "StockCountId", "ProductId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_TenantId",
                table: "stock_counts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_TenantId_Number",
                table: "stock_counts",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_TenantId_WarehouseId_open",
                table: "stock_counts",
                columns: new[] { "TenantId", "WarehouseId" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_WarehouseId",
                table: "stock_counts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ProductId",
                table: "stock_movements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_Sequence",
                table: "stock_movements",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId",
                table: "stock_movements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_ProductId",
                table: "stock_movements",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_SourceType_SourceId",
                table: "stock_movements",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_WarehouseId_ProductId_VariantId_Da~",
                table: "stock_movements",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "VariantId", "Date", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_VariantId",
                table: "stock_movements",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_WarehouseId",
                table: "stock_movements",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_TenantId",
                table: "warehouses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_TenantId_BranchId_default",
                table: "warehouses",
                columns: new[] { "TenantId", "BranchId" },
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_TenantId_Code",
                table: "warehouses",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "average_cost_snapshots");

            migrationBuilder.DropTable(
                name: "cost_layers");

            migrationBuilder.DropTable(
                name: "min_stock_levels");

            migrationBuilder.DropTable(
                name: "movement_lot_lines");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "revaluation_jobs");

            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "lots");

            migrationBuilder.DropTable(
                name: "serial_numbers");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropColumn(
                name: "IsBundle",
                table: "products");

            migrationBuilder.DropColumn(
                name: "TrackingMode",
                table: "products");

            migrationBuilder.DropColumn(
                name: "bundle_components",
                table: "products");
        }
    }
}
