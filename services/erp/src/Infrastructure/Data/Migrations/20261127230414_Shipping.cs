using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Shipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "shipments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchAt",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Driver",
                table: "shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IrsaliyeNo",
                table: "shipments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PickListId",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNo",
                table: "shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vehicle",
                table: "shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Vehicle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DriverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_proofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PhotoFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_proofs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_proofs_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delivery_proofs_stored_files_PhotoFileId",
                        column: x => x.PhotoFileId,
                        principalTable: "stored_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    WidthCm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LengthCm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Desi = table.Column<decimal>(type: "numeric(18,1)", precision: 18, scale: 1, nullable: false),
                    Contents = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parcels_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pick_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentIds = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pick_lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_stops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_stops_delivery_plans_DeliveryPlanId",
                        column: x => x.DeliveryPlanId,
                        principalTable: "delivery_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delivery_stops_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pick_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SuggestedLots = table.Column<string>(type: "jsonb", nullable: true),
                    PickedLots = table.Column<string>(type: "jsonb", nullable: true),
                    PickedSerials = table.Column<string>(type: "jsonb", nullable: true),
                    Sources = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pick_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pick_lines_pick_lists_PickListId",
                        column: x => x.PickListId,
                        principalTable: "pick_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_PickListId",
                table: "shipments",
                column: "PickListId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_ShipmentId_AttemptNo",
                table: "delivery_attempts",
                columns: new[] { "ShipmentId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_TenantId",
                table: "delivery_attempts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_plans_TenantId",
                table: "delivery_plans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_plans_TenantId_Date",
                table: "delivery_plans",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_proofs_PhotoFileId",
                table: "delivery_proofs",
                column: "PhotoFileId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_proofs_ShipmentId",
                table: "delivery_proofs",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_proofs_TenantId",
                table: "delivery_proofs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_stops_DeliveryPlanId_Sort",
                table: "delivery_stops",
                columns: new[] { "DeliveryPlanId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_stops_ShipmentId",
                table: "delivery_stops",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_stops_TenantId",
                table: "delivery_stops",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_parcels_ShipmentId_Index",
                table: "parcels",
                columns: new[] { "ShipmentId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parcels_TenantId",
                table: "parcels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_lines_PickListId_Sort",
                table: "pick_lines",
                columns: new[] { "PickListId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pick_lines_TenantId",
                table: "pick_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_lists_TenantId",
                table: "pick_lists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_lists_TenantId_Number",
                table: "pick_lists",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pick_lists_TenantId_Status",
                table: "pick_lists",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "delivery_proofs");

            migrationBuilder.DropTable(
                name: "delivery_stops");

            migrationBuilder.DropTable(
                name: "parcels");

            migrationBuilder.DropTable(
                name: "pick_lines");

            migrationBuilder.DropTable(
                name: "delivery_plans");

            migrationBuilder.DropTable(
                name: "pick_lists");

            migrationBuilder.DropIndex(
                name: "IX_shipments_PickListId",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "DispatchAt",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "Driver",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "IrsaliyeNo",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "PickListId",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "TrackingNo",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "Vehicle",
                table: "shipments");
        }
    }
}
