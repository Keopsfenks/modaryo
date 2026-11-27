using System.Text.Json;
using IdentityService.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Invitations;

public class InvitationTests : TestBase
{
    private static async Task<(HttpClient Owner, Guid TenantId)> OwnerWithCompanyAsync(string name = "Akdeniz Tekstil")
    {
        var owner = await Factory.SignedInClientAsync("sahip@ornek.com", "Sahip Kişi");
        var tenantId = await owner.CreateCompanyAsync(name);
        return (owner, tenantId);
    }

    private static async Task<string> InviteAsync(HttpClient inviter, Guid tenantId, string email, string role = "member")
    {
        var roleId = await Factory.RoleIdAsync(tenantId, role);
        using var response = await inviter.PostAsJsonAsync("/api/identity/companies/current/invitations", new { email, roleId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return TokenFromLastMail(email);
    }

    private static string TokenFromLastMail(string email) =>
        FakeEmailSender.ExtractLink(Factory.Emails.LastTo(IUserAccounts.Normalize(email))).Split("/davet/")[1];

    private static Task<HttpResponseMessage> RegisterWithInviteAsync(HttpClient client, string email, string token) =>
        client.PostAsJsonAsync("/api/identity/auth/register", new { email, password = TestUsers.Password, fullName = "Davetli Kişi", inviteToken = token });

    [Test]
    public async Task Invite_SendsMailWithCompanyAndInviter()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();

        await InviteAsync(owner, tenantId, "veli@ornek.com");

        var mail = Factory.Emails.LastTo("veli@ornek.com");
        mail.Subject.ShouldBe("Modaryo: Akdeniz Tekstil sizi davet etti");
        mail.TextBody.ShouldContain("Sahip Kişi");
        owner.Dispose();
    }

    [Test]
    public async Task Preview_ShowsCompanyInviterAndAccountExists()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var anonymous = Factory.CreateClientWithCookies();

        var preview = await (await anonymous.GetAsync($"/api/identity/invitations/{token}")).JsonAsync();

        preview.GetProperty("companyName").GetString().ShouldBe("Akdeniz Tekstil");
        preview.GetProperty("invitedBy").GetString().ShouldBe("Sahip Kişi");
        preview.GetProperty("email").GetString().ShouldBe("veli@ornek.com");
        preview.GetProperty("accountExists").GetBoolean().ShouldBeFalse();
        owner.Dispose();
    }

    [Test]
    public async Task Preview_UnknownToken_Returns404()
    {
        using var anonymous = Factory.CreateClientWithCookies();

        (await anonymous.GetAsync("/api/identity/invitations/yok-boyle-bir-token")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Invite_NewPerson_RegisterViaLink_JoinsAndIsConfirmed()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var invitee = Factory.CreateClientWithCookies();

        using var response = await RegisterWithInviteAsync(invitee, "veli@ornek.com", token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.SessionCookieHeader().ShouldNotBeNull();
        var session = await invitee.SessionAsync();
        session.GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantId);
        session.GetProperty("activeCompany").GetProperty("roleName").GetString().ShouldBe("Kullanıcı");
        Factory.Emails.To("veli@ornek.com").Count.ShouldBe(1); // yalnız davet; doğrulama e-postası gitmez
        (await Factory.CreateClientWithCookies().LoginAsync("veli@ornek.com")).StatusCode.ShouldBe(HttpStatusCode.OK);
        owner.Dispose();
    }

    [Test]
    public async Task Invite_UnverifiedExistingAccount_RegisterViaLink_ConfirmsAndJoins()
    {
        await Factory.CreateConfirmedAsync("veli@ornek.com", confirmed: false);
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var invitee = Factory.CreateClientWithCookies();

        using var response = await invitee.PostAsJsonAsync("/api/identity/auth/register",
            new { email = "veli@ornek.com", password = "davetle-yeni-sifre", fullName = "Veli", inviteToken = token });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await invitee.SessionAsync()).GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantId);
        (await Factory.CreateClientWithCookies().LoginAsync("veli@ornek.com", "davetle-yeni-sifre")).StatusCode.ShouldBe(HttpStatusCode.OK);
        owner.Dispose();
    }

    [Test]
    public async Task Invite_RegisterViaLink_ExistingConfirmedAccount_Returns409()
    {
        await Factory.CreateConfirmedAsync("veli@ornek.com");
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var invitee = Factory.CreateClientWithCookies();

        using var response = await RegisterWithInviteAsync(invitee, "veli@ornek.com", token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("account_exists");
        (await invitee.LoginAsync("veli@ornek.com", "davetle-yeni-sifre")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        owner.Dispose();
    }

    [Test]
    public async Task Register_WithInviteForDifferentEmail_FallsBackToNormalRegistration()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var stranger = Factory.CreateClientWithCookies();

        using var response = await RegisterWithInviteAsync(stranger, "baska@ornek.com", token);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.SessionCookieHeader().ShouldBeNull();
        Factory.Emails.LastTo("baska@ornek.com").Subject.ShouldBe("Modaryo: e-posta adresinizi doğrulayın");
        owner.Dispose();
    }

    [Test]
    public async Task Accept_SignedIn_JoinsAndSwitches()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var veli = await Factory.SignedInClientAsync("veli@ornek.com");
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com", "admin");

        using var response = await veli.PostAsync($"/api/identity/invitations/{token}/accept", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await veli.SessionAsync();
        session.GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantId);
        session.GetProperty("permissions").GetArrayLength().ShouldBe(64);
        (await veli.PostAsync($"/api/identity/invitations/{token}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        owner.Dispose();
    }

    [Test]
    public async Task Accept_WrongAccount_Returns403Mismatch()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var someoneElse = await Factory.SignedInClientAsync("baska@ornek.com");

        using var response = await someoneElse.PostAsync($"/api/identity/invitations/{token}/accept", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe("invitation_email_mismatch");
        owner.Dispose();
    }

    [Test]
    public async Task Accept_WithoutSession_Returns401()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var anonymous = Factory.CreateClientWithCookies();

        (await anonymous.PostAsync($"/api/identity/invitations/{token}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        owner.Dispose();
    }

    [Test]
    public async Task Accept_MatchesEmailCaseInsensitively()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "  Veli@Ornek.COM ");
        using var veli = await Factory.SignedInClientAsync("veli@ornek.com");

        using var response = await veli.PostAsync($"/api/identity/invitations/{token}/accept", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        owner.Dispose();
    }

    [Test]
    public async Task Accept_WhenAlreadyInOtherCompany_AddsMembershipAndSwitches()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync("Davet Eden Firma");
        using var veli = await Factory.SignedInClientAsync("veli@ornek.com");
        await veli.CreateCompanyAsync("Veli'nin Firması");
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");

        (await veli.PostAsync($"/api/identity/invitations/{token}/accept", null)).EnsureSuccessStatusCode();

        var session = await veli.SessionAsync();
        session.GetProperty("companies").GetArrayLength().ShouldBe(2);
        session.GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantId);
        owner.Dispose();
    }

    [Test]
    public async Task Invite_ExistingMember_Returns409()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        var roleId = await Factory.RoleIdAsync(tenantId, "member");

        using var response = await owner.PostAsJsonAsync("/api/identity/companies/current/invitations", new { email = "UYE@ornek.com", roleId });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("already_member");
        owner.Dispose();
        member.Dispose();
    }

    [Test]
    public async Task Resend_InvalidatesOldToken()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var oldToken = await InviteAsync(owner, tenantId, "veli@ornek.com");
        var list = await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync();
        var invitationId = list[0].GetProperty("id").GetGuid();

        (await owner.PostAsync($"/api/identity/companies/current/invitations/{invitationId}/resend", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var newToken = TokenFromLastMail("veli@ornek.com");
        newToken.ShouldNotBe(oldToken);
        using var anonymous = Factory.CreateClientWithCookies();
        (await anonymous.GetAsync($"/api/identity/invitations/{oldToken}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/identity/invitations/{newToken}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        owner.Dispose();
    }

    [Test]
    public async Task Invite_SameEmailTwice_KeepsSinglePendingInvitation()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        await InviteAsync(owner, tenantId, "veli@ornek.com");

        await InviteAsync(owner, tenantId, "veli@ornek.com", "admin");

        var list = await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync();
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("roleName").GetString().ShouldBe("Yönetici");
        owner.Dispose();
    }

    [Test]
    public async Task Accept_ExpiredAfter7Days_Returns404()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var invitation = await db.Invitations.SingleAsync();
            (invitation.ExpiresAt - invitation.CreatedAt).ShouldBe(TimeSpan.FromDays(7));
            invitation.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        using var veli = await Factory.SignedInClientAsync("veli@ornek.com");

        (await veli.PostAsync($"/api/identity/invitations/{token}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        owner.Dispose();
    }

    [Test]
    public async Task RevokeInvitation_MakesTokenUnusable()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var token = await InviteAsync(owner, tenantId, "veli@ornek.com");
        var id = (await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync())[0].GetProperty("id").GetGuid();

        (await owner.DeleteAsync($"/api/identity/companies/current/invitations/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = Factory.CreateClientWithCookies();
        (await anonymous.GetAsync($"/api/identity/invitations/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        owner.Dispose();
    }

    [Test]
    public async Task RevokeInvitation_OfOtherTenant_Returns404()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        await InviteAsync(owner, tenantId, "veli@ornek.com");
        var id = (await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync())[0].GetProperty("id").GetGuid();
        using var otherOwner = await Factory.SignedInClientAsync("baska-sahip@ornek.com");
        await otherOwner.CreateCompanyAsync("Başka Firma");

        (await otherOwner.DeleteAsync($"/api/identity/companies/current/invitations/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherOwner.PostAsync($"/api/identity/companies/current/invitations/{id}/resend", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        owner.Dispose();
    }

    [Test]
    public async Task Member_CannotListInvitations_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");

        (await member.GetAsync("/api/identity/companies/current/invitations")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        owner.Dispose();
        member.Dispose();
    }

    [Test]
    public async Task Admin_CannotInviteAsOwner_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        var ownerRole = await Factory.RoleIdAsync(tenantId, "owner");

        using var response = await admin.PostAsJsonAsync("/api/identity/companies/current/invitations", new { email = "veli@ornek.com", roleId = ownerRole });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        owner.Dispose();
        admin.Dispose();
    }

    [Test]
    public async Task MyInvitations_ListsPendingAndAcceptsById()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        await InviteAsync(owner, tenantId, "veli@ornek.com");
        using var veli = await Factory.SignedInClientAsync("veli@ornek.com");

        var mine = await (await veli.GetAsync("/api/identity/account/invitations")).JsonAsync();

        mine.GetArrayLength().ShouldBe(1);
        mine[0].GetProperty("companyName").GetString().ShouldBe("Akdeniz Tekstil");
        var id = mine[0].GetProperty("id").GetGuid();
        (await veli.PostAsync($"/api/identity/account/invitations/{id}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await veli.SessionAsync()).GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantId);
        (await (await veli.GetAsync("/api/identity/account/invitations")).JsonAsync()).GetArrayLength().ShouldBe(0);
        owner.Dispose();
    }

    [Test]
    public async Task MyInvitations_CannotAcceptSomeoneElsesById()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        await InviteAsync(owner, tenantId, "veli@ornek.com");
        var id = (await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync())[0].GetProperty("id").GetGuid();
        using var stranger = await Factory.SignedInClientAsync("baska@ornek.com");

        (await stranger.PostAsync($"/api/identity/account/invitations/{id}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        owner.Dispose();
    }

    [Test]
    public async Task ResendOwnerInvitation_ByNonOwner_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        await InviteAsync(owner, tenantId, "veli@ornek.com", "owner");
        var invitationId = (await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync())[0].GetProperty("id").GetGuid();
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        using var _a = admin;

        using var response = await admin.PostAsync($"/api/identity/companies/current/invitations/{invitationId}/resend", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ResendRicherRoleInvitation_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var created = await owner.PostAsJsonAsync("/api/identity/companies/current/roles", new { name = "Zengin", permissions = new[] { "cari.read" }, maxDiscountPercent = 50m });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        using var invited = await owner.PostAsJsonAsync("/api/identity/companies/current/invitations", new { email = "veli@ornek.com", roleId });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invitationId = (await (await owner.GetAsync("/api/identity/companies/current/invitations")).JsonAsync())[0].GetProperty("id").GetGuid();
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        using var _a = admin;

        using var response = await admin.PostAsync($"/api/identity/companies/current/invitations/{invitationId}/resend", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
