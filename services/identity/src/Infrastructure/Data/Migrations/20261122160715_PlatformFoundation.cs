using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppliedPermissionSetVersion",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<List<string>>(
                name: "EnabledModules",
                table: "tenants",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<bool>(
                name: "AllBranches",
                table: "tenant_roles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountPercent",
                table: "tenant_roles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CariId",
                table: "memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "memberships",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "employee");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_entries_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_branches_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_branches",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_branches", x => new { x.MembershipId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_membership_branches_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_membership_branches_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_branches",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_branches", x => new { x.RoleId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_role_branches_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_branches_tenant_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "tenant_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenant_roles_MaxDiscountPercent",
                table: "tenant_roles",
                sql: "\"MaxDiscountPercent\" >= 0 AND \"MaxDiscountPercent\" <= 100");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId_At",
                table: "audit_entries",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_branches_TenantId_Code",
                table: "branches",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branches_TenantId_default",
                table: "branches",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_membership_branches_BranchId",
                table: "membership_branches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_role_branches_BranchId",
                table: "role_branches",
                column: "BranchId");

            // Backfill: mevcut her firmaya varsayılan Merkez şubesi. Sütun varsayılanları
            // AppliedPermissionSetVersion = 1 ve AllBranches = true değerlerini zaten atar; açıkça da yazılır.
            migrationBuilder.Sql(
                "INSERT INTO branches (\"Id\", \"TenantId\", \"Code\", \"Name\", \"IsActive\", \"IsDefault\", \"CreatedAt\") " +
                "SELECT gen_random_uuid(), t.\"Id\", 'MRK', 'Merkez', TRUE, TRUE, t.\"CreatedAt\" FROM tenants t " +
                "WHERE NOT EXISTS (SELECT 1 FROM branches b WHERE b.\"TenantId\" = t.\"Id\");");
            migrationBuilder.Sql("UPDATE tenants SET \"AppliedPermissionSetVersion\" = 1;");
            migrationBuilder.Sql("UPDATE tenant_roles SET \"AllBranches\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "membership_branches");

            migrationBuilder.DropTable(
                name: "role_branches");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tenant_roles_MaxDiscountPercent",
                table: "tenant_roles");

            migrationBuilder.DropColumn(
                name: "AppliedPermissionSetVersion",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EnabledModules",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "AllBranches",
                table: "tenant_roles");

            migrationBuilder.DropColumn(
                name: "MaxDiscountPercent",
                table: "tenant_roles");

            migrationBuilder.DropColumn(
                name: "CariId",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "memberships");
        }
    }
}
