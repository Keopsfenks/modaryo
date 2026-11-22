using IdentityService.Domain.Constants;
using NUnit.Framework;
using Shouldly;

namespace IdentityService.Domain.UnitTests;

public class SystemRolesTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Test]
    public void CreateDefaults_CreatesThreeSystemRoles()
    {
        var roles = SystemRoles.CreateDefaults(TenantId);

        roles.Select(r => r.SystemKey).ShouldBe(["owner", "admin", "member"]);
        roles.Select(r => r.Name).ShouldBe(["Sahip", "Yönetici", "Kullanıcı"]);
        roles.ShouldAllBe(r => r.IsSystem && r.TenantId == TenantId && r.Id != Guid.Empty);
    }

    [Test]
    public void CreateDefaults_OwnerHasStar()
    {
        var owner = SystemRoles.CreateDefaults(TenantId).Single(r => r.SystemKey == SystemRoles.Owner);

        owner.Permissions.Select(p => p.Permission).ShouldBe(["*"]);
    }

    [Test]
    public void CreateDefaults_AdminHasFourPermissions()
    {
        var admin = SystemRoles.CreateDefaults(TenantId).Single(r => r.SystemKey == SystemRoles.Admin);

        admin.Permissions.Select(p => p.Permission).ShouldBe(
            ["company.update", "members.read", "members.invite", "members.manage"], ignoreOrder: true);
    }

    [Test]
    public void CreateDefaults_MemberHasMembersRead()
    {
        var member = SystemRoles.CreateDefaults(TenantId).Single(r => r.SystemKey == SystemRoles.Member);

        member.Permissions.Select(p => p.Permission).ShouldBe(["members.read"]);
    }

    [Test]
    public void CreateDefaults_PermissionsPointAtTheirRole()
    {
        foreach (var role in SystemRoles.CreateDefaults(TenantId))
        {
            role.Permissions.ShouldAllBe(p => p.RoleId == role.Id);
        }
    }
}
