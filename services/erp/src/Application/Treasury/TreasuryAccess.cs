using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;

namespace ERP.Application.Treasury;

public static class TreasuryAccess {
    /// <summary>İzinlerden en az biri gerekir ([Authorize] özniteliği hepsini ister).</summary>
    public static void RequireAny(ICurrentSession session, params string[] permissions) {
        if (session.UserId is null) throw new UnauthorizedAccessException();

        if (session.TenantId is null || !permissions.Any(session.HasPermission)) throw new ForbiddenAccessException();
    }
}
