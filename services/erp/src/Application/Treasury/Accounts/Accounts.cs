using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Settings.Categories;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Accounts;

public sealed record TreasuryAccountDto(
    Guid                Id,
    TreasuryAccountKind Kind,
    string              Name,
    string              Currency,
    Guid?               BranchId,
    string?             Iban,
    string?             BankName,
    decimal             Balance,
    bool                IsActive
);

public abstract record AccountInput {
    public TreasuryAccountKind Kind     { get; init; }
    public string              Name     { get; init; } = string.Empty;
    public string              Currency { get; init; } = "TRY";
    public Guid                BranchId { get; init; }
    public string?             Iban     { get; init; }
    public string?             BankName { get; init; }
}

public static class AccountRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : AccountInput {
        v.RuleFor(a => a.Kind).IsInEnum().WithMessage("Hesap türü geçersiz.");
        v.RuleFor(a => a.Name).NotEmpty().WithMessage("Hesap adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Hesap adı en fazla 100 karakter olabilir.");
        v.RuleFor(a => a.Currency).Must(c => CardRules.Currencies.Contains(c))
            .WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
        v.RuleFor(a => a.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        v.RuleFor(a => a.BankName).NotEmpty().When(a => a.Kind != TreasuryAccountKind.Cash)
            .WithMessage("Banka adı zorunludur.");
        v.RuleFor(a => a.BankName).MaximumLength(100).WithMessage("Banka adı en fazla 100 karakter olabilir.");
        v.RuleFor(a => a.Iban).NotEmpty().When(a => a.Kind == TreasuryAccountKind.Bank)
            .WithMessage("Banka hesabı için IBAN zorunludur.");
        v.RuleFor(a => a.Iban).Must(i => CompanyProfileSettings.IsValidIban(CompanyProfileSettings.NormalizeIban(i)))
            .When(a => a.Kind != TreasuryAccountKind.Cash && !string.IsNullOrWhiteSpace(a.Iban))
            .WithMessage("IBAN geçerli değil (TR ile başlayan 26 karakter ve sağlama toplamı doğru olmalı).");
    }

    /// <summary>Kasada banka alanları tutulmaz.</summary>
    public static void Apply(TreasuryAccount account, AccountInput input) {
        var bank = input.Kind != TreasuryAccountKind.Cash;
        account.Kind     = input.Kind;
        account.Name     = input.Name.Trim();
        account.Currency = input.Currency;
        account.BranchId = input.BranchId;
        account.Iban     = bank && !string.IsNullOrWhiteSpace(input.Iban) ? CompanyProfileSettings.NormalizeIban(input.Iban) : null;
        account.BankName = bank ? CardRules.Clean(input.BankName) : null;
    }

    public static TreasuryAccountDto ToDto(TreasuryAccount a, decimal balance) {
        return new TreasuryAccountDto(a.Id, a.Kind, a.Name, a.Currency, a.BranchId, a.Iban, a.BankName, balance, a.IsActive);
    }

    public static async Task<decimal> BalanceAsync(IApplicationDbContext db, Guid accountId, CancellationToken ct) {
        return await db.TreasuryMovements.AsNoTracking().Where(m => m.AccountId == accountId && m.CancelledAt == null)
                   .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
    }

    public static async Task<TreasuryAccount> FindAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, CancellationToken ct) {
        var account = await db.TreasuryAccounts.FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw new NotFoundException(id.ToString(), "TreasuryAccount");
        if (account.BranchId is { } branch) BranchGuard.Ensure(tenant, branch);
        return account;
    }

    public static async Task<bool> HasMovementsAsync(IApplicationDbContext db, Guid accountId, CancellationToken ct) {
        return await db.TreasuryMovements.AnyAsync(m => m.AccountId == accountId, ct)
               || await db.FinanceDocuments.AnyAsync(d => d.AccountId == accountId || d.TargetAccountId == accountId, ct);
    }

    public static async Task EnsureBranchAsync(ICurrentTenant tenant, IBranchDirectory branches, Guid branchId, CancellationToken ct) {
        BranchGuard.Ensure(tenant, branchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, branchId, ct)) throw TreasuryErrors.BranchNotFound();
    }
}

[Authorize(Permission = TreasuryPermissions.Read)]
public sealed record ListAccountsQuery(TreasuryAccountKind? Kind, bool IncludeInactive = true) : IRequest<IReadOnlyList<TreasuryAccountDto>>;

public sealed class ListAccountsQueryHandler(IApplicationDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ListAccountsQuery, IReadOnlyList<TreasuryAccountDto>> {
    public async Task<IReadOnlyList<TreasuryAccountDto>> Handle(ListAccountsQuery request, CancellationToken ct) {
        var query = db.TreasuryAccounts.AsNoTracking().AsQueryable();
        if (request.Kind is { } kind) query = query.Where(a => a.Kind == kind);
        if (!request.IncludeInactive) query = query.Where(a => a.IsActive);

        var accounts = (await query.OrderBy(a => a.Kind).ThenBy(a => a.Name).ToListAsync(ct))
            .Where(a => a.BranchId is null || tenant.CanAccessBranch(a.BranchId.Value)).ToList();
        var ids = accounts.Select(a => a.Id).ToList();
        var balances = await db.TreasuryMovements.AsNoTracking().Where(m => ids.Contains(m.AccountId) && m.CancelledAt == null)
                           .GroupBy(m => m.AccountId).Select(g => new { g.Key, Sum = g.Sum(m => m.Amount) }).ToListAsync(ct);
        var map = balances.ToDictionary(b => b.Key, b => b.Sum);
        return accounts.Select(a => AccountRules.ToDto(a, map.GetValueOrDefault(a.Id))).ToList();
    }
}

[Authorize(Permission = TreasuryPermissions.Read)]
public sealed record GetAccountQuery(Guid Id) : IRequest<TreasuryAccountDto>;

public sealed class GetAccountQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<GetAccountQuery, TreasuryAccountDto> {
    public async Task<TreasuryAccountDto> Handle(GetAccountQuery request, CancellationToken ct) {
        var account = await AccountRules.FindAsync(db, tenant, request.Id, ct);
        return AccountRules.ToDto(account, await AccountRules.BalanceAsync(db, account.Id, ct));
    }
}

[Authorize(Permission = TreasuryPermissions.Manage)]
public sealed record CreateAccountCommand : AccountInput, IRequest<TreasuryAccountDto>;

public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand> {
    public CreateAccountCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreateAccountCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, IBranchDirectory branches)
    : IRequestHandler<CreateAccountCommand, TreasuryAccountDto> {
    public async Task<TreasuryAccountDto> Handle(CreateAccountCommand request, CancellationToken ct) {
        await AccountRules.EnsureBranchAsync(tenant, branches, request.BranchId, ct);
        var name = request.Name.Trim();
        if (await db.TreasuryAccounts.AnyAsync(a => a.Name == name, ct)) throw TreasuryErrors.NameTaken(name);

        var account = new TreasuryAccount();
        AccountRules.Apply(account, request);
        db.TreasuryAccounts.Add(account);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (TreasuryErrors.IsUniqueViolation(ex)) {
            throw TreasuryErrors.NameTaken(name);
        }

        return AccountRules.ToDto(account, 0m);
    }
}

[Authorize(Permission = TreasuryPermissions.Manage)]
public sealed record UpdateAccountCommand : AccountInput, IRequest<TreasuryAccountDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateAccountCommandValidator : AbstractValidator<UpdateAccountCommand> {
    public UpdateAccountCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateAccountCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, IBranchDirectory branches)
    : IRequestHandler<UpdateAccountCommand, TreasuryAccountDto> {
    public async Task<TreasuryAccountDto> Handle(UpdateAccountCommand request, CancellationToken ct) {
        var account = await AccountRules.FindAsync(db, tenant, request.Id, ct);
        if (request.BranchId != account.BranchId) await AccountRules.EnsureBranchAsync(tenant, branches, request.BranchId, ct);

        var hasMovements = await AccountRules.HasMovementsAsync(db, account.Id, ct);
        if (hasMovements && (request.Currency != account.Currency || request.Kind != account.Kind)) throw TreasuryErrors.CurrencyLocked();

        var name = request.Name.Trim();
        if (await db.TreasuryAccounts.AnyAsync(a => a.Name == name && a.Id != account.Id, ct)) throw TreasuryErrors.NameTaken(name);

        AccountRules.Apply(account, request);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (TreasuryErrors.IsUniqueViolation(ex)) {
            throw TreasuryErrors.NameTaken(name);
        }

        return AccountRules.ToDto(account, await AccountRules.BalanceAsync(db, account.Id, ct));
    }
}

[Authorize(Permission = TreasuryPermissions.Manage)]
public sealed record SetAccountActiveCommand(Guid Id, bool Active) : IRequest;

public sealed class SetAccountActiveCommandHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<SetAccountActiveCommand> {
    public async Task Handle(SetAccountActiveCommand request, CancellationToken ct) {
        var account = await AccountRules.FindAsync(db, tenant, request.Id, ct);
        account.IsActive = request.Active;
        await db.SaveChangesAsync(ct);
    }
}

[Authorize(Permission = TreasuryPermissions.Manage)]
public sealed record DeleteAccountCommand(Guid Id) : IRequest;

public sealed class DeleteAccountCommandHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<DeleteAccountCommand> {
    public async Task Handle(DeleteAccountCommand request, CancellationToken ct) {
        var account = await AccountRules.FindAsync(db, tenant, request.Id, ct);
        if (await AccountRules.HasMovementsAsync(db, account.Id, ct)
            || await db.Instruments.AnyAsync(i => i.CollectionAccountId == account.Id, ct)
            || await db.PosRegisters.AnyAsync(r => r.CashAccountId == account.Id || r.CardAccountId == account.Id, ct))
            throw TreasuryErrors.AccountInUse();

        db.TreasuryAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }
}

public sealed record MovementRowDto(
    Guid                 Id,
    DateOnly             Date,
    Guid                 DocumentId,
    FinanceDocumentKind? DocumentKind,
    string?              DocumentNumber,
    string?              Description,
    decimal              Amount,
    decimal              Balance,
    bool                 IsCancelled
);

public sealed record AccountMovementsDto(
    TreasuryAccountDto            Account,
    decimal                       OpeningBalance,
    decimal                       ClosingBalance,
    int                           Total,
    IReadOnlyList<MovementRowDto> Rows
);

[Authorize(Permission = TreasuryPermissions.Read)]
public sealed record GetAccountMovementsQuery(Guid Id, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50) : IRequest<AccountMovementsDto>;

public sealed class GetAccountMovementsQueryValidator : AbstractValidator<GetAccountMovementsQuery> {
    public GetAccountMovementsQueryValidator() {
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200).WithMessage("Sayfa boyutu 1 ile 200 arasında olmalıdır.");
        RuleFor(q => q).Must(q => q.From is null || q.To is null || q.From <= q.To).WithName("From")
            .WithMessage("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
    }
}

public sealed class GetAccountMovementsQueryHandler(IApplicationDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetAccountMovementsQuery, AccountMovementsDto> {
    public async Task<AccountMovementsDto> Handle(GetAccountMovementsQuery request, CancellationToken ct) {
        var account = await AccountRules.FindAsync(db, tenant, request.Id, ct);

        var all = db.TreasuryMovements.AsNoTracking().Where(m => m.AccountId == account.Id);
        var opening = request.From is { } from
            ? await all.Where(m => m.CancelledAt == null && m.Date < from).SumAsync(m => (decimal?)m.Amount, ct) ?? 0m
            : 0m;

        var inRange = all;
        if (request.From is { } f) inRange = inRange.Where(m => m.Date >= f);
        if (request.To is { } t) inRange   = inRange.Where(m => m.Date <= t);

        // Yürüyen bakiye aralığın tamamı üzerinden hesaplanır; sayfa yalnızca dilimdir.
        var rows   = await inRange.OrderBy(m => m.Date).ThenBy(m => m.Created).ThenBy(m => m.Id).ToListAsync(ct);
        var docIds = rows.Select(r => r.DocumentId).Distinct().ToList();
        var docs = await db.FinanceDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id))
                       .Select(d => new { d.Id, d.Kind, d.Number }).ToDictionaryAsync(d => d.Id, ct);

        var running = opening;
        var dtos    = new List<MovementRowDto>(rows.Count);
        foreach (var m in rows) {
            if (!m.IsCancelled) running += m.Amount;
            docs.TryGetValue(m.DocumentId, out var doc);
            dtos.Add(new MovementRowDto(m.Id, m.Date, m.DocumentId, doc?.Kind, doc?.Number, m.Description, m.Amount, running, m.IsCancelled));
        }

        var page = dtos.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        return new AccountMovementsDto(AccountRules.ToDto(account, await AccountRules.BalanceAsync(db, account.Id, ct)), opening, running, dtos.Count, page);
    }
}
