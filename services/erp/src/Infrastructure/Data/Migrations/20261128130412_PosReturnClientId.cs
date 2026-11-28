using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosReturnClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashierName",
                table: "pos_returns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientReturnId",
                table: "pos_returns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_returns_TenantId_ClientReturnId",
                table: "pos_returns",
                columns: new[] { "TenantId", "ClientReturnId" },
                unique: true,
                filter: "\"ClientReturnId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pos_returns_TenantId_ClientReturnId",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "CashierName",
                table: "pos_returns");

            migrationBuilder.DropColumn(
                name: "ClientReturnId",
                table: "pos_returns");
        }
    }
}
