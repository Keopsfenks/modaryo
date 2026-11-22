using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;

namespace ERP.Application.Common.Security;

/// <summary>Şube kapsamlı işlemlerde, oturumun o şubeye erişimini doğrular.</summary>
public static class BranchGuard {
    /// <summary>Erişim yoksa 403 "branch_forbidden" fırlatır. Şube kapsamı null ise tüm şubeler serbesttir.</summary>
    public static void Ensure(ICurrentTenant tenant, Guid branchId) {
        if (!tenant.CanAccessBranch(branchId))
            throw new AppException(403, "branch_forbidden",
                                   "Bu şubeye erişim yetkiniz yok.");
    }
}