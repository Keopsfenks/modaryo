namespace IdentityService.Web.FunctionalTests.Branches;

/// <summary>Şube kapsamında güvenli varsayılanlar: daraltma kaldırma, pasif şube, kısıtlı çağıran, kod kuralları.</summary>
public partial class BranchTests
{
    [Test]
    public async Task ClearOwnNarrowing_WhenRestrictedCaller_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var b2 = await CreateBranchAsync(owner, "S2");
        var (caller, callerId) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "member");
        using var _c = caller;
        await BranchRoleForAsync(owner, callerId, "Sınırlı", ["members.manage"], false, b1, b2);
        (await owner.PutAsJsonAsync($"{MembersUrl}/{callerId}/branches", new { branchIds = new[] { b1 } })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var clear = await caller.PutAsJsonAsync($"{MembersUrl}/{callerId}/branches", new { branchIds = (Guid[]?)null });

        clear.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clear.ErrorCodeAsync()).ShouldBe("permission_not_grantable");
        (await RecordAsync(callerId)).BranchIds.ShouldBe([b1]);
    }

    [Test]
    public async Task ClearOthersNarrowing_RoleWiderThanCaller_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var (caller, callerId) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "member");
        using var _c = caller;
        await BranchRoleForAsync(owner, callerId, "Sınırlı", ["members.manage"], false, b1);
        var (target, targetId) = await Factory.SignedInMemberAsync(tenantId, "hedef@ornek.com", "member");
        using var _t = target;
        (await owner.PutAsJsonAsync($"{MembersUrl}/{targetId}/branches", new { branchIds = new[] { b1 } })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var clear = await caller.PutAsJsonAsync($"{MembersUrl}/{targetId}/branches", new { branchIds = (Guid[]?)null });

        clear.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clear.ErrorCodeAsync()).ShouldBe("permission_not_grantable");
        (await RecordAsync(targetId)).BranchIds.ShouldBe([b1]);
    }

    [Test]
    public async Task EmptyArrayNarrowing_Returns400()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;

        using var response = await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = Array.Empty<Guid>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DeleteLastNarrowingBranch_MemberHasNoBranches()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        (await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = new[] { b1 } })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecordAsync(memberId)).BranchIds.ShouldBe([b1]);

        (await owner.DeleteAsync($"{Url}/{b1}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RecordAsync(memberId)).BranchIds.ShouldBe([]);
        (await member.SessionAsync()).GetProperty("branchIds").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task RestrictedCaller_CannotCreateBranch_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var (caller, callerId) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "member");
        using var _c = caller;
        await BranchRoleForAsync(owner, callerId, "Şube yöneticisi", ["ayarlar.subeler"], false, b1);

        using var create = await caller.PostAsJsonAsync(Url, new { code = "S9", name = "Yeni" });
        using var update = await caller.PutAsJsonAsync($"{Url}/{b1}", new { code = "S1", name = "Yeni ad", isActive = true });
        using var delete = await caller.DeleteAsync($"{Url}/{b1}");

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await create.ErrorCodeAsync()).ShouldBe("permission_not_grantable");
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DeactivateBranch_RemovedFromSessions()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var b2 = await CreateBranchAsync(owner, "S2");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        await BranchRoleForAsync(owner, memberId, "Şubeli", ["cari.read"], false, b1, b2);

        (await owner.PutAsJsonAsync($"{Url}/{b1}", new { code = "S1", name = "Şube", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RecordAsync(memberId)).BranchIds.ShouldBe([b2]);

        (await owner.PutAsJsonAsync($"{Url}/{b1}", new { code = "S1", name = "Şube", isActive = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RecordAsync(memberId)).BranchIds!.Order().ShouldBe(new[] { b1, b2 }.Order());
    }

    [Test]
    public async Task UpdateRole_WithInactiveExistingBranch_StillSucceeds()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var b2 = await CreateBranchAsync(owner, "S2");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        var roleId = await BranchRoleForAsync(owner, memberId, "Şubeli", ["cari.read"], false, b1, b2);
        (await owner.PutAsJsonAsync($"{Url}/{b1}", new { code = "S1", name = "Şube", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var rename = await owner.PutAsJsonAsync($"{RolesUrl}/{roleId}", new { name = "Şubeli 2" });
        using var keepInactive = await owner.PutAsJsonAsync($"{RolesUrl}/{roleId}", new { name = "Şubeli 2", allBranches = false, branchIds = new[] { b1, b2 } });

        rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        keepInactive.StatusCode.ShouldBe(HttpStatusCode.OK);

        var b3 = await CreateBranchAsync(owner, "S3");
        (await owner.PutAsJsonAsync($"{Url}/{b3}", new { code = "S3", name = "Şube", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var addNewInactive = await owner.PutAsJsonAsync($"{RolesUrl}/{roleId}", new { name = "Şubeli 2", allBranches = false, branchIds = new[] { b2, b3 } });
        addNewInactive.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BranchCode_LowercaseIBecomesAsciiI_AndInvalidCharsRejected()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        using var created = await owner.PostAsJsonAsync(Url, new { code = "ist-1", name = "İstanbul" });
        using var space = await owner.PostAsJsonAsync(Url, new { code = "A B", name = "Şube" });
        using var turkish = await owner.PostAsJsonAsync(Url, new { code = "ŞUBE", name = "Şube" });
        using var dup = await owner.PostAsJsonAsync(Url, new { code = "IST-1", name = "Şube" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.JsonAsync()).GetProperty("code").GetString().ShouldBe("IST-1");
        space.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        turkish.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task NarrowMemberOfOtherTenant_Returns404()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        using var other = await Factory.SignedInClientAsync("baska@ornek.com");
        var otherTenant = await other.CreateCompanyAsync("Diğer A.Ş.");
        var (otherMember, otherMemberId) = await Factory.SignedInMemberAsync(otherTenant, "yabanci@ornek.com", "member");
        using var _m = otherMember;

        using var response = await owner.PutAsJsonAsync($"{MembersUrl}/{otherMemberId}/branches", new { branchIds = new[] { b1 } });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
