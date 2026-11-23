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
    public void CreateDefaults_AdminHasCariPermissions()
    {
        var admin = SystemRoles.CreateDefaults(TenantId).Single(r => r.SystemKey == SystemRoles.Admin);

        admin.Permissions.Select(p => p.Permission).ShouldBe(
            ["company.update", "members.read", "members.invite", "members.manage", "cari.read", "cari.manage", "cari.movements",
             "roller.read", "roller.manage", "ayarlar.moduller", "ayarlar.subeler", "denetim.read",
             "ayarlar.firma", "ayarlar.genel", "ayarlar.finans",
             "katalog.read", "katalog.manage", "ayarlar.katalog"],
            ignoreOrder: true);
    }

    [Test]
    public void CreateDefaults_MemberHasCariRead()
    {
        var member = SystemRoles.CreateDefaults(TenantId).Single(r => r.SystemKey == SystemRoles.Member);

        member.Permissions.Select(p => p.Permission).ShouldBe(["members.read", "cari.read", "katalog.read"], ignoreOrder: true);
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
