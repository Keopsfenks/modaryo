using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MembershipBranchRestriction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BranchesRestricted",
                table: "memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE memberships SET \"BranchesRestricted\" = TRUE " +
                "WHERE EXISTS (SELECT 1 FROM membership_branches mb WHERE mb.\"MembershipId\" = memberships.\"Id\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchesRestricted",
                table: "memberships");
        }
    }
}
