using ERP.Application.Common.Behaviours;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using MediatR;
using NUnit.Framework;
using Shouldly;

namespace ERP.Application.UnitTests.Common.Behaviours;

public class AuthorizationBehaviourTests {
    private static Task<int> Run<TRequest>(TRequest request, ICurrentSession session) where TRequest : notnull {
        return new AuthorizationBehaviour<TRequest, int>(session).Handle(request, _ => Task.FromResult(42),
                                                                         CancellationToken.None);
    }

    [Test]
    public async Task NotSignedIn_Throws401() {
        await Should.ThrowAsync<UnauthorizedAccessException>(() => Run(new SignedInOnly(),
                                                                       new FakeSession(null, null)));
    }

    [Test]
    public async Task SignedIn_WithoutPermissionRequirement_Allows() {
        (await Run(new SignedInOnly(), new FakeSession(Guid.NewGuid(), null))).ShouldBe(42);
    }

    [Test]
    public async Task WithoutTenant_Throws403() {
        await Should.ThrowAsync<ForbiddenAccessException>(() => Run(new ManageMembers(),
                                                                    new FakeSession(Guid.NewGuid(), null, "*")));
    }

    [Test]
    public async Task WithStar_Allows() {
        (await Run(new ManageMembers(), new FakeSession(Guid.NewGuid(), Guid.NewGuid(), "*"))).ShouldBe(42);
    }

    [Test]
    public async Task MissingPermission_Throws403() {
        await Should.ThrowAsync<ForbiddenAccessException>(() => Run(new ManageMembers(),
                                                                    new FakeSession(Guid.NewGuid(), Guid.NewGuid(),
                                                                        "members.read")));
    }

    [Test]
    public async Task NoAttribute_AllowsAnonymous() {
        (await Run(new Anonymous(), new FakeSession(null, null))).ShouldBe(42);
    }

    [Authorize(Permission = "members.manage")]
    public sealed record ManageMembers : IRequest<int>;

    [Authorize]
    public sealed record SignedInOnly : IRequest<int>;

    public sealed record Anonymous : IRequest<int>;

    private sealed class FakeSession(
        Guid?           userId,
        Guid?           tenantId,
        params string[] permissions
    ) : ICurrentSession {
        public Guid?                 UserId      => userId;
        public Guid?                 TenantId    => tenantId;
        public string?               SidHash     => userId is null ? null : "hash";
        public string?               SessionId   => userId is null ? null : "id";
        public IReadOnlyList<string> Permissions => permissions;

        public bool HasPermission(string permission) {
            return permissions.Contains("*") || permissions.Contains(permission);
        }

        public string? Ip        => null;
        public string? UserAgent => null;
    }
}