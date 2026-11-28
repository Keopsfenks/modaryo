using System.Text.RegularExpressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Inventory;
using ERP.Domain.Pos;
using ERP.Domain.Treasury;

namespace ERP.Application.Pos.Registers;

public sealed record QuickButtonDto(Guid ProductId, Guid? VariantId, string? Code, string? Name, decimal? PresetQty, string Color);

public sealed record RegisterDto(
    Guid                                       Id,
    string                                     Code,
    string                                     Name,
    Guid                                       BranchId,
    Guid                                       WarehouseId,
    Guid                                       CashAccountId,
    Guid                                       CardAccountId,
    bool                                       IsActive,
    uint                                       Version,
    IReadOnlyList<IReadOnlyList<QuickButtonDto?>> Pages
);

public sealed record RegisterRowDto(Guid Id, string Code, string Name, Guid BranchId, Guid WarehouseId, bool IsActive);

public abstract record RegisterInput {
    public string Code          { get; init; } = string.Empty;
    public string Name          { get; init; } = string.Empty;
    public Guid   BranchId      { get; init; }
    public Guid   WarehouseId   { get; init; }
    public Guid   CashAccountId { get; init; }
    public Guid   CardAccountId { get; init; }
}

public static partial class RegisterRules {
    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    public const string DefaultColor = "#1976d2";

    public static void AddRules<T>(this AbstractValidator<T> v) where T : RegisterInput {
        v.RuleFor(r => r.Code).Must(c => c is not null && CodePattern().IsMatch(c.Trim().ToUpperInvariant()))
         .WithMessage("Kasa kodu 1 ile 10 karakter arasında, harf (A-Z) ve rakamlardan oluşmalıdır.");
        v.RuleFor(r => r.Name).NotEmpty().WithMessage("Kasa adı zorunludur.")
         .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Kasa adı en fazla 100 karakter olabilir.");
        v.RuleFor(r => r.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        v.RuleFor(r => r.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
        v.RuleFor(r => r.CashAccountId).NotEmpty().WithMessage("Nakit hesabı seçilmelidir.");
        v.RuleFor(r => r.CardAccountId).NotEmpty().WithMessage("Kart hesabı seçilmelidir.");
    }

    public static string NormalizeCode(string code) {
        return code.Trim().ToUpperInvariant();
    }

    public static string NormalizeColor(string? color) {
        return string.IsNullOrWhiteSpace(color) ? DefaultColor : color.Trim();
    }

    public static bool IsColor(string? color) {
        return string.IsNullOrWhiteSpace(color) || ColorPattern().IsMatch(color.Trim());
    }

    /// <summary>Şube, depo (şubenin normal deposu) ve hesapları (şubenin nakit ve kart POS hesabı) doğrular.</summary>
    public static async Task EnsureReferencesAsync(IApplicationDbContext db, ICurrentTenant tenant, IBranchDirectory branches, RegisterInput input,
                                                   CancellationToken ct) {
        BranchGuard.Ensure(tenant, input.BranchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, input.BranchId, ct))
            throw new AppException(404, "branch_not_found", "Şube bulunamadı ya da pasif.");

        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == input.WarehouseId, ct)
                        ?? throw PosErrors.Bad("warehouse_invalid", "Depo bulunamadı.");
        if (warehouse.BranchId != input.BranchId || warehouse.Kind != WarehouseKind.Normal || !warehouse.IsActive)
            throw PosErrors.Bad("warehouse_invalid", "Depo, kasanın şubesine ait aktif bir normal depo olmalıdır.");

        await EnsureAccountAsync(db, input.CashAccountId, TreasuryAccountKind.Cash, input.BranchId, "cash_account_invalid", "Nakit hesabı", ct);
        await EnsureAccountAsync(db, input.CardAccountId, TreasuryAccountKind.CardPos, input.BranchId, "card_account_invalid", "Kart hesabı", ct);
    }

    private static async Task EnsureAccountAsync(IApplicationDbContext db, Guid id, TreasuryAccountKind kind, Guid branchId, string code, string label,
                                                 CancellationToken ct) {
        var account = await db.TreasuryAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw PosErrors.Bad(code, $"{label} bulunamadı.");
        if (account.Kind != kind || account.BranchId != branchId || !account.IsActive || account.Currency != "TRY")
            throw PosErrors.Bad(code, kind == TreasuryAccountKind.Cash
                                          ? "Nakit hesabı, kasanın şubesine ait aktif bir TRY kasa hesabı olmalıdır."
                                          : "Kart hesabı, kasanın şubesine ait aktif bir TRY kart POS hesabı olmalıdır.");
    }

    public static async Task<RegisterDto> ToDtoAsync(IApplicationDbContext db, Register r, CancellationToken ct) {
        var buttons = r.Buttons.OrderBy(b => b.Page).ThenBy(b => b.Row).ThenBy(b => b.Col).ToList();
        var productIds = buttons.Select(b => b.ProductId).Distinct().ToList();
        var variantIds = buttons.Where(b => b.VariantId != null).Select(b => b.VariantId!.Value).Distinct().ToList();
        var products = productIds.Count == 0
                           ? []
                           : await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var variants = variantIds.Count == 0
                           ? []
                           : await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Code }).ToDictionaryAsync(v => v.Id, ct);

        var pages = new List<IReadOnlyList<QuickButtonDto?>>();
        var pageCount = buttons.Count == 0 ? 0 : buttons.Max(b => b.Page) + 1;
        for (var page = 0; page < pageCount; page++) {
            var cells = new QuickButtonDto?[buttons.Where(b => b.Page == page).Select(b => b.Row * QuickButton.MaxCols + b.Col + 1).DefaultIfEmpty(0).Max()];
            foreach (var b in buttons.Where(b => b.Page == page)) {
                products.TryGetValue(b.ProductId, out var product);
                var code = b.VariantId is { } vid && variants.TryGetValue(vid, out var variant) ? variant.Code : product?.Code;
                cells[b.Row * QuickButton.MaxCols + b.Col] = new QuickButtonDto(b.ProductId, b.VariantId, code, product?.Name, b.PresetQty, b.Color);
            }

            pages.Add(cells);
        }

        return new RegisterDto(r.Id, r.Code, r.Name, r.BranchId, r.WarehouseId, r.CashAccountId, r.CardAccountId, r.IsActive, r.Version, pages);
    }

    public static async Task<Register> LoadAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, CancellationToken ct) {
        var register = await db.PosRegisters.Include(r => r.Buttons).FirstOrDefaultAsync(r => r.Id == id, ct)
                       ?? throw new NotFoundException(id.ToString(), "Register");
        BranchGuard.Ensure(tenant, register.BranchId);
        return register;
    }

    /// <summary>Vardiya ya da satış kaydı olan kasanın şube/depo/hesap bağlantıları değişmez.</summary>
    public static async Task<bool> HasActivityAsync(IApplicationDbContext db, Guid registerId, CancellationToken ct) {
        return await db.PosShifts.AnyAsync(s => s.RegisterId == registerId, ct);
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewRegisters)]
public sealed record ListRegistersQuery(bool IncludeInactive = true) : IRequest<IReadOnlyList<RegisterRowDto>>;

public sealed class ListRegistersQueryHandler(IApplicationDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ListRegistersQuery, IReadOnlyList<RegisterRowDto>> {
    public async Task<IReadOnlyList<RegisterRowDto>> Handle(ListRegistersQuery request, CancellationToken ct) {
        var query = db.PosRegisters.AsNoTracking().AsQueryable();
        if (!request.IncludeInactive) query = query.Where(r => r.IsActive);
        return (await query.OrderBy(r => r.Code).ToListAsync(ct)).Where(r => tenant.CanAccessBranch(r.BranchId))
                                                                 .Select(r => new RegisterRowDto(r.Id, r.Code, r.Name, r.BranchId, r.WarehouseId, r.IsActive)).ToList();
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewRegisters)]
public sealed record GetRegisterQuery(Guid Id) : IRequest<RegisterDto>;

public sealed class GetRegisterQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<GetRegisterQuery, RegisterDto> {
    public async Task<RegisterDto> Handle(GetRegisterQuery request, CancellationToken ct) {
        return await RegisterRules.ToDtoAsync(db, await RegisterRules.LoadAsync(db, tenant, request.Id, ct), ct);
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Manage)]
public sealed record CreateRegisterCommand : RegisterInput, IRequest<RegisterDto>;

public sealed class CreateRegisterCommandValidator : AbstractValidator<CreateRegisterCommand> {
    public CreateRegisterCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreateRegisterCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, IBranchDirectory branches, PosSetup setup)
    : IRequestHandler<CreateRegisterCommand, RegisterDto> {
    public async Task<RegisterDto> Handle(CreateRegisterCommand request, CancellationToken ct) {
        await RegisterRules.EnsureReferencesAsync(db, tenant, branches, request, ct);
        var code = RegisterRules.NormalizeCode(request.Code);
        if (await db.PosRegisters.AnyAsync(r => r.Code == code, ct)) throw PosErrors.Conflict("code_taken", $"\"{code}\" kasa kodu zaten kullanılıyor.");

        // İlk kasa açılırken perakende müşteri hazır olsun.
        await setup.EnsureAsync(ct);

        var register = new Register {
            Code = code, Name = request.Name.Trim(), BranchId = request.BranchId, WarehouseId = request.WarehouseId,
            CashAccountId = request.CashAccountId, CardAccountId = request.CardAccountId, IsActive = true
        };
        db.PosRegisters.Add(register);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (PosErrors.IsUniqueViolation(ex)) {
            throw PosErrors.Conflict("code_taken", $"\"{code}\" kasa kodu zaten kullanılıyor.");
        }

        return await RegisterRules.ToDtoAsync(db, register, ct);
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Manage)]
public sealed record UpdateRegisterCommand : RegisterInput, IRequest<RegisterDto> {
    public Guid Id       { get; init; }
    public bool IsActive { get; init; } = true;
    public uint Version  { get; init; }
}

public sealed class UpdateRegisterCommandValidator : AbstractValidator<UpdateRegisterCommand> {
    public UpdateRegisterCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateRegisterCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, IBranchDirectory branches)
    : IRequestHandler<UpdateRegisterCommand, RegisterDto> {
    public async Task<RegisterDto> Handle(UpdateRegisterCommand request, CancellationToken ct) {
        var register = await RegisterRules.LoadAsync(db, tenant, request.Id, ct);
        if (request.Version != 0 && request.Version != register.Version) throw PosErrors.VersionConflict();

        var code = RegisterRules.NormalizeCode(request.Code);
        var relinked = request.BranchId != register.BranchId || request.WarehouseId != register.WarehouseId
                       || request.CashAccountId != register.CashAccountId || request.CardAccountId != register.CardAccountId;
        if (relinked) {
            if (await RegisterRules.HasActivityAsync(db, register.Id, ct))
                throw PosErrors.Conflict("register_in_use", "Vardiyası olan kasanın şube, depo ve hesapları değiştirilemez. Yeni bir kasa açabilirsiniz.");
            await RegisterRules.EnsureReferencesAsync(db, tenant, branches, request, ct);
        }

        if (code != register.Code) {
            if (await RegisterRules.HasActivityAsync(db, register.Id, ct))
                throw PosErrors.Conflict("register_in_use", "Vardiyası olan kasanın kodu değiştirilemez.");
            if (await db.PosRegisters.AnyAsync(r => r.Code == code && r.Id != register.Id, ct))
                throw PosErrors.Conflict("code_taken", $"\"{code}\" kasa kodu zaten kullanılıyor.");
        }

        register.Code          = code;
        register.Name          = request.Name.Trim();
        register.BranchId      = request.BranchId;
        register.WarehouseId   = request.WarehouseId;
        register.CashAccountId = request.CashAccountId;
        register.CardAccountId = request.CardAccountId;
        register.IsActive      = request.IsActive;
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            throw PosErrors.VersionConflict();
        } catch (DbUpdateException ex) when (PosErrors.IsUniqueViolation(ex)) {
            throw PosErrors.Conflict("code_taken", $"\"{code}\" kasa kodu zaten kullanılıyor.");
        }

        return await RegisterRules.ToDtoAsync(db, register, ct);
    }
}

public sealed record QuickButtonInput(Guid ProductId, Guid? VariantId, decimal? PresetQty, string? Color);

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Manage)]
public sealed record SetButtonsCommand(Guid Id, IReadOnlyList<IReadOnlyList<QuickButtonInput?>>? Pages) : IRequest<RegisterDto>;

public sealed class SetButtonsCommandValidator : AbstractValidator<SetButtonsCommand> {
    public SetButtonsCommandValidator() {
        RuleFor(c => c.Pages).NotNull().WithMessage("Sayfalar gönderilmelidir.");
        RuleFor(c => c.Pages).Must(p => p is null || p.Count <= QuickButton.MaxPages)
                             .WithMessage($"En fazla {QuickButton.MaxPages} sayfa tanımlanabilir.");
        RuleFor(c => c.Pages).Must(p => p is null || p.All(page => page is not null && page.Count <= QuickButton.MaxRows * QuickButton.MaxCols))
                             .WithMessage($"Bir sayfada en fazla {QuickButton.MaxRows}x{QuickButton.MaxCols} tuş olabilir.");
        RuleFor(c => c.Pages).Must(p => p is null || p.All(page => page.All(b => b is null || RegisterRules.IsColor(b.Color))))
                             .WithMessage("Tuş rengi #RRGGBB biçiminde olmalıdır.");
        RuleFor(c => c.Pages).Must(p => p is null || p.All(page => page.All(b => b is null || b.PresetQty is null or > 0m)))
                             .WithMessage("Hazır miktar sıfırdan büyük olmalıdır.");
    }
}

public sealed class SetButtonsCommandHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<SetButtonsCommand, RegisterDto> {
    public async Task<RegisterDto> Handle(SetButtonsCommand request, CancellationToken ct) {
        var register = await RegisterRules.LoadAsync(db, tenant, request.Id, ct);
        var pages    = request.Pages ?? [];

        var wanted     = pages.SelectMany(p => p).Where(b => b is not null).Select(b => b!).ToList();
        var productIds = wanted.Select(b => b.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.IsActive }).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = wanted.Where(b => b.VariantId != null).Select(b => b.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.ProductId, v.IsActive }).ToDictionaryAsync(v => v.Id, ct);
        foreach (var b in wanted) {
            if (!products.TryGetValue(b.ProductId, out var product) || !product.IsActive)
                throw PosErrors.Bad("product_invalid", "Tuşta kullanılan kalem bulunamadı ya da pasif.");
            if (b.VariantId is { } vid && (!variants.TryGetValue(vid, out var variant) || variant.ProductId != b.ProductId || !variant.IsActive))
                throw PosErrors.Bad("variant_invalid", "Tuşta kullanılan varyant kaleme ait değil ya da pasif.");
        }

        // Yerleşim bütünüyle değişir: eski tuşlar silinir, yenileri sayfa ve hücre sırasıyla eklenir.
        await using var transaction = await db.BeginTransactionAsync(ct);
        db.PosQuickButtons.RemoveRange(register.Buttons);
        register.Buttons.Clear();
        await db.SaveChangesAsync(ct);
        for (var page = 0; page < pages.Count; page++)
            for (var cell = 0; cell < pages[page].Count; cell++) {
                if (pages[page][cell] is not { } b) continue;
                register.Buttons.Add(new QuickButton {
                    RegisterId = register.Id, Page = page, Row = cell / QuickButton.MaxCols, Col = cell % QuickButton.MaxCols,
                    ProductId = b.ProductId, VariantId = b.VariantId, PresetQty = b.PresetQty, Color = RegisterRules.NormalizeColor(b.Color)
                });
            }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await RegisterRules.ToDtoAsync(db, register, ct);
    }
}

/// <summary>Şubede kasa noktası varsa şube kullanımdadır.</summary>
public sealed class PosBranchUsageProvider(IApplicationDbContext db) : IBranchUsageProvider {
    public async Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
        return await db.PosRegisters.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && r.BranchId == branchId, ct);
    }
}
