using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InternalApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Caller = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => new { x.Caller, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_CreatedAt",
                table: "idempotency_records",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
