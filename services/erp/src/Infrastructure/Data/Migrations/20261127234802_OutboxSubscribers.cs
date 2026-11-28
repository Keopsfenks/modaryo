using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutboxSubscribers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReturnId",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "document_counters",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.CreateTable(
                name: "outbox_subscriber_states",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subscriber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GaveUp = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_subscriber_states", x => new { x.MessageId, x.Subscriber });
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_subscriber_states_Pending",
                table: "outbox_subscriber_states",
                column: "NextAttemptAt",
                filter: "\"CompletedAt\" IS NULL AND NOT \"GaveUp\"");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_subscriber_states_TenantId",
                table: "outbox_subscriber_states",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_subscriber_states");

            migrationBuilder.DropColumn(
                name: "ReturnId",
                table: "shipments");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "document_counters",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);
        }
    }
}
