using System.Globalization;
using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Branches;

public sealed record BranchDto(Guid Id, string Code, string Name, bool IsActive, bool IsDefault);

/// <summary>Şube komutlarının ortak kuralları: kod normalleştirme, doğrulama, benzersizlik, firma kısıtlı yükleme.</summary>
public sealed class BranchRules(IApplicationDbContext db, CompanyAccess access)
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    public const int CodeMax = 10;
    public const int NameMin = 2;
    public const int NameMax = 100;

    /// <summary>Kodlar büyük harfle (tr-TR) saklanır; karşılaştırma bu sayede harf duyarsızdır.</summary>
    public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpper(Turkish);

    public static BranchDto ToDto(Branch b) => new(b.Id, b.Code, b.Name, b.IsActive, b.IsDefault);

    public static IRuleBuilderOptions<T, string> ValidCode<T>(IRuleBuilder<T, string> rule) =>
        rule.Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Şube kodu boş olamaz.")
            .Must(c => c is null || c.Trim().Length <= CodeMax).WithMessage("Şube kodu en fazla 10 karakter olabilir.");

    public static IRuleBuilderOptions<T, string> ValidName<T>(IRuleBuilder<T, string> rule) =>
        rule.Must(n => n is not null && n.Trim().Length >= NameMin).WithMessage("Şube adı en az 2 karakter olmalıdır.")
            .Must(n => n is null || n.Trim().Length <= NameMax).WithMessage("Şube adı en fazla 100 karakter olabilir.");

    public async Task EnsureCodeFreeAsync(string normalizedCode, Guid? exceptId, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        if (await db.Branches.AnyAsync(b => b.TenantId == tenantId && b.Code == normalizedCode && b.Id != exceptId, ct))
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.BranchCodeTaken, "Bu şube kodu zaten kullanılıyor.");
        }
    }

    public static AuthException DefaultBranch(string message) =>
        new(StatusCodes.Status409Conflict, AuthErrorCodes.BranchIsDefault, message);

    /// <summary>Başka firmanın şubesi "bulunamadı" sayılır.</summary>
    public async Task<Branch> LoadAsync(Guid id, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        return await db.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct)
            ?? throw new NotFoundException(id.ToString(), "Branch");
    }
}
