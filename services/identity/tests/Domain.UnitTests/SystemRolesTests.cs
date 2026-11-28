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
             "katalog.read", "katalog.manage", "ayarlar.katalog",
             "fiyat.read", "fiyat.manage", "fiyat.alis", "ayarlar.fiyat",
             "stok.read", "stok.manage", "stok.sayim", "maliyet.read", "ayarlar.stok",
             "fatura.read", "fatura.manage", "fatura.issue", "fatura.cancel", "ayarlar.belgeler", "ayarlar.ebelge", "kur.manage",
             "finans.read", "finans.manage", "tahsilat.manage", "odeme.manage", "virman.manage", "masraf.manage", "ceksenet.manage", "muhasebe.manage", "muhasebe.export",
             "satinalma.read", "satinalma.manage", "satinalma.receive", "satinalma.quarantine", "ayarlar.satinalma",
             "satis.read", "satis.manage", "satis.approve", "satis.limit-asimi", "satis.ship", "satis.return", "satis.recurring", "satis.targets", "ayarlar.satis",
             "sevkiyat.read", "sevkiyat.pick", "sevkiyat.pack", "sevkiyat.ship", "sevkiyat.deliver", "sevkiyat.plan", "ayarlar.sevkiyat",
             "pos.sell", "pos.return", "pos.shift", "pos.cashmove", "pos.reports", "pos.manage", "ayarlar.pos"],
            ignoreOrder: true);
    }

    [Test]
    public void CreateDefaults_MemberHasCariRead()
    {
        var member = SystemRoles.CreateDefaults(TenantId).Single(r => r.SystemKey == SystemRoles.Member);

        member.Permissions.Select(p => p.Permission).ShouldBe(["members.read", "cari.read", "katalog.read", "fiyat.read", "stok.read", "fatura.read", "finans.read", "satinalma.read", "satis.read", "satis.manage", "sevkiyat.read"], ignoreOrder: true);
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
