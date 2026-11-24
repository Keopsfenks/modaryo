using System.Text;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.ExpenseCategories;

public sealed record ExpenseCategoryDto(Guid Id, string Code, string Name, string AccountCode, bool IsIncome, bool IsSystem, bool IsActive);

public static class CategoryRules {
    /// <summary>Varsayılan kategoriler: kod, ad ve gelir olup olmadığı.</summary>
    private static readonly Dictionary<string, (string Name, bool IsIncome)> Defaults = new() {
        ["genel"]            = ("Genel gider", false),
        ["kira"]             = ("Kira", false),
        ["ulasim"]           = ("Ulaşım", false),
        ["banka-masrafi"]    = ("Banka masrafı", false),
        ["kur-farki"]        = ("Kur farkı gideri", false),
        ["genel-gelir"]      = ("Genel gelir", true),
        ["kur-farki-geliri"] = ("Kur farkı geliri", true)
    };

    public static ExpenseCategoryDto ToDto(ExpenseCategory c) {
        return new ExpenseCategoryDto(c.Id, c.Code, c.Name, c.AccountCode, c.IsIncome, c.IsSystem, c.IsActive);
    }

    public static bool IsAccountCode(string? code) {
        return !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 30 && code.Trim().All(c => char.IsAsciiDigit(c) || c is '.' or '-');
    }

    /// <summary>Ad - kod: küçük harf, Türkçe karakterler sadeleştirilir, ayırıcı tire.</summary>
    public static string Slug(string name) {
        var sb   = new StringBuilder();
        var dash = false;
        foreach (var ch in name.Trim()) {
            var c = ch switch {
                        'ğ' or 'Ğ'       => 'g',
                        'ü' or 'Ü'       => 'u',
                        'ş' or 'Ş'       => 's',
                        'ı' or 'İ'       => 'i',
                        'ö' or 'Ö'       => 'o',
                        'ç' or 'Ç'       => 'c',
                        _                => char.ToLowerInvariant(ch)
                    };
            if (char.IsAsciiLetterOrDigit(c)) {
                sb.Append(c);
                dash = false;
            } else if (!dash && sb.Length > 0) {
                sb.Append('-');
                dash = true;
            }
        }

        return sb.ToString().Trim('-');
    }

    /// <summary>Firmanın ilk kullanımında varsayılan kategorileri (hesap kodları finans ayarlarından) açar. Eşzamanlı ilk kullanımda çakışma yutulur.</summary>
    public static async Task EnsureSeededAsync(IApplicationDbContext db, ITenantSettings settings, CancellationToken ct) {
        if (await db.ExpenseCategories.AnyAsync(ct)) return;

        var codes   = (await settings.GetAsync<FinanceSettings>(ct)).ExpenseCategoryCodes;
        var created = new List<ExpenseCategory>();
        foreach (var (code, (name, income)) in Defaults) {
            var category = new ExpenseCategory { Code = code, Name = name, AccountCode = codes.GetValueOrDefault(code, "770"), IsIncome = income, IsSystem = true };
            db.ExpenseCategories.Add(category);
            created.Add(category);
        }

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (TreasuryErrors.IsUniqueViolation(ex)) {
            // Başka bir istek aynı anda açtı; bizimkiler izlemeden çıkarılır.
            foreach (var category in created) db.ExpenseCategories.Remove(category);
        }
    }

    public static void AddRules<T>(this AbstractValidator<T> v, Func<T, string?> name, Func<T, string?> accountCode) {
        v.RuleFor(c => name(c)).Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 100 && Slug(n).Length > 0)
            .WithMessage("Kategori adı zorunludur ve en fazla 100 karakter olabilir.");
        v.RuleFor(c => accountCode(c)).Must(IsAccountCode)
            .WithMessage("Hesap kodu en fazla 30 karakter olup yalnızca rakam, nokta ve tire içerebilir.");
    }
}

[Authorize]
public sealed record ListExpenseCategoriesQuery(bool? IsIncome, bool IncludeInactive = true) : IRequest<IReadOnlyList<ExpenseCategoryDto>>;

public sealed class ListExpenseCategoriesQueryHandler(IApplicationDbContext db, ITenantSettings settings, ICurrentSession session)
    : IRequestHandler<ListExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryDto>> {
    public async Task<IReadOnlyList<ExpenseCategoryDto>> Handle(ListExpenseCategoriesQuery request, CancellationToken ct) {
        TreasuryAccess.RequireAny(session, TreasuryPermissions.Expense, TreasuryPermissions.Read);
        await CategoryRules.EnsureSeededAsync(db, settings, ct);

        var query = db.ExpenseCategories.AsNoTracking().AsQueryable();
        if (request.IsIncome is { } income) query = query.Where(c => c.IsIncome == income);
        if (!request.IncludeInactive) query = query.Where(c => c.IsActive);

        return (await query.OrderBy(c => c.IsIncome).ThenBy(c => c.Name).ToListAsync(ct)).Select(CategoryRules.ToDto).ToList();
    }
}

[Authorize(Permission = TreasuryPermissions.Expense)]
public sealed record CreateExpenseCategoryCommand(string Name, string AccountCode, bool IsIncome = false) : IRequest<ExpenseCategoryDto>;

public sealed class CreateExpenseCategoryCommandValidator : AbstractValidator<CreateExpenseCategoryCommand> {
    public CreateExpenseCategoryCommandValidator() {
        this.AddRules(c => c.Name, c => c.AccountCode);
    }
}

public sealed class CreateExpenseCategoryCommandHandler(IApplicationDbContext db, ITenantSettings settings) : IRequestHandler<CreateExpenseCategoryCommand, ExpenseCategoryDto> {
    public async Task<ExpenseCategoryDto> Handle(CreateExpenseCategoryCommand request, CancellationToken ct) {
        await CategoryRules.EnsureSeededAsync(db, settings, ct);

        var name = request.Name.Trim();
        var code = CategoryRules.Slug(name);
        if (await db.ExpenseCategories.AnyAsync(c => c.Code == code, ct)) throw TreasuryErrors.CategoryNameTaken(name);

        var category = new ExpenseCategory { Code = code, Name = name, AccountCode = request.AccountCode.Trim(), IsIncome = request.IsIncome };
        db.ExpenseCategories.Add(category);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (TreasuryErrors.IsUniqueViolation(ex)) {
            throw TreasuryErrors.CategoryNameTaken(name);
        }

        return CategoryRules.ToDto(category);
    }
}

/// <summary>Ad, hesap kodu ve etkinlik güncellenir; kod ve gelir/gider türü değişmez.</summary>
[Authorize(Permission = TreasuryPermissions.Expense)]
public sealed record UpdateExpenseCategoryCommand : IRequest<ExpenseCategoryDto> {
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string AccountCode { get; init; } = string.Empty;

    public bool? IsActive { get; init; }
}

public sealed class UpdateExpenseCategoryCommandValidator : AbstractValidator<UpdateExpenseCategoryCommand> {
    public UpdateExpenseCategoryCommandValidator() {
        this.AddRules(c => c.Name, c => c.AccountCode);
    }
}

public sealed class UpdateExpenseCategoryCommandHandler(IApplicationDbContext db) : IRequestHandler<UpdateExpenseCategoryCommand, ExpenseCategoryDto> {
    public async Task<ExpenseCategoryDto> Handle(UpdateExpenseCategoryCommand request, CancellationToken ct) {
        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "ExpenseCategory");
        category.Name        = request.Name.Trim();
        category.AccountCode = request.AccountCode.Trim();
        if (request.IsActive is { } active) category.IsActive = active;

        await db.SaveChangesAsync(ct);
        return CategoryRules.ToDto(category);
    }
}

[Authorize(Permission = TreasuryPermissions.Expense)]
public sealed record DeleteExpenseCategoryCommand(Guid Id) : IRequest;

public sealed class DeleteExpenseCategoryCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteExpenseCategoryCommand> {
    public async Task Handle(DeleteExpenseCategoryCommand request, CancellationToken ct) {
        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "ExpenseCategory");
        if (category.IsSystem) throw TreasuryErrors.CategorySystem();
        if (await db.FinanceDocuments.AnyAsync(d => d.ExpenseCategoryId == category.Id, ct)) throw TreasuryErrors.CategoryInUse();

        db.ExpenseCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }
}
