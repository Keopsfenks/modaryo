using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.CustomerPrices;

public abstract record CustomerPriceInput {
    public Guid             ProductId   { get; init; }
    public Guid?            VariantId   { get; init; }
    public string?          Unit        { get; init; }
    public string           Currency    { get; init; } = "TRY";
    public bool             VatIncluded { get; init; }
    public List<PriceTier>? Tiers       { get; init; }
    public DateOnly?        ValidFrom   { get; init; }
    public DateOnly?        ValidTo     { get; init; }
}

public static class CustomerPriceRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : CustomerPriceInput {
        v.RuleFor(c => c.Unit).NotEmpty().WithMessage("Birim zorunludur.");
        v.RuleFor(c => c.Currency).Must(c => PricingRules.Currencies.Contains(c))
            .WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
        v.RuleFor(c => c).Must(c => c.ValidFrom is null || c.ValidTo is null || c.ValidFrom <= c.ValidTo)
            .WithMessage("Geçerlilik başlangıcı bitişten sonra olamaz.").WithName("ValidFrom");
    }

    /// <summary>Girdiyi doğrular, çakışmayı denetler ve satıra yazar (kaydetmez).</summary>
    public static async Task ApplyAsync(IApplicationDbContext db, CustomerPrice price, CustomerPriceInput input, Guid accountId,
                                        CancellationToken ct) {
        var product = await db.Products.AsNoTracking().Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == input.ProductId, ct)
                      ?? throw new NotFoundException(input.ProductId.ToString(), "Product");
        if (input.VariantId is { } vid
            && !await db.ProductVariants.AnyAsync(v => v.Id == vid && v.ProductId == product.Id, ct))
            throw new NotFoundException(vid.ToString(), "ProductVariant");

        var unit     = input.Unit!.Trim();
        var currency = input.Currency.Trim().ToUpperInvariant();
        var tiers    = PricingRules.NormalizeTiers(input.Tiers);
        PricingRules.EnsureUnit(product, unit);

        var others = await db.CustomerPrices.AsNoTracking()
                         .Where(p => p.CurrentAccountId == accountId && p.ProductId == input.ProductId && p.VariantId == input.VariantId
                                     && p.Unit == unit && p.Currency == currency && p.Id != price.Id)
                         .Select(p => new { p.ValidFrom, p.ValidTo }).ToListAsync(ct);
        var from = input.ValidFrom ?? DateOnly.MinValue;
        var to   = input.ValidTo ?? DateOnly.MaxValue;
        if (others.Any(o => (o.ValidFrom ?? DateOnly.MinValue) <= to && from <= (o.ValidTo ?? DateOnly.MaxValue)))
            throw new AppException(409, "price_overlap",
                                   "Bu müşteri, kalem ve birim için tarih aralığı çakışan başka bir özel fiyat var.");

        price.CurrentAccountId = accountId;
        price.ProductId        = input.ProductId;
        price.VariantId        = input.VariantId;
        price.Unit             = unit;
        price.Currency         = currency;
        price.VatIncluded      = input.VatIncluded;
        price.Tiers            = tiers;
        price.ValidFrom        = input.ValidFrom;
        price.ValidTo          = input.ValidTo;
    }

    public static async Task<List<CustomerPriceDto>> ToDtosAsync(IApplicationDbContext db, IReadOnlyList<CustomerPrice> prices,
                                                                 CancellationToken ct) {
        var productIds = prices.Select(p => p.ProductId).Distinct().ToList();
        var variantIds = prices.Where(p => p.VariantId is not null).Select(p => p.VariantId!.Value).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                           .Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var variants = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
                           .Select(v => new { v.Id, v.Code }).ToDictionaryAsync(v => v.Id, v => v.Code, ct);
        return prices.Select(p => new CustomerPriceDto(
                                 p.Id, p.CurrentAccountId, p.ProductId, products.GetValueOrDefault(p.ProductId)?.Code ?? string.Empty,
                                 products.GetValueOrDefault(p.ProductId)?.Name ?? string.Empty, p.VariantId,
                                 p.VariantId is { } vid ? variants.GetValueOrDefault(vid) : null, p.Unit, p.Currency, p.VatIncluded,
                                 p.Tiers, p.ValidFrom, p.ValidTo)).ToList();
    }

    public static async Task EnsureAccountAsync(IApplicationDbContext db, Guid accountId, CancellationToken ct) {
        if (!await db.CurrentAccounts.AnyAsync(a => a.Id == accountId, ct))
            throw new NotFoundException(accountId.ToString(), "CurrentAccount");
    }
}

[Authorize(Permission = PricingPermissions.Read)]
public sealed record ListCustomerPricesQuery(Guid CurrentAccountId) : IRequest<IReadOnlyList<CustomerPriceDto>>;

public sealed class ListCustomerPricesQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<ListCustomerPricesQuery, IReadOnlyList<CustomerPriceDto>> {
    public async Task<IReadOnlyList<CustomerPriceDto>> Handle(ListCustomerPricesQuery request, CancellationToken ct) {
        await CustomerPriceRules.EnsureAccountAsync(db, request.CurrentAccountId, ct);
        var prices = await db.CustomerPrices.AsNoTracking().Where(p => p.CurrentAccountId == request.CurrentAccountId)
                         .OrderBy(p => p.ProductId).ThenBy(p => p.ValidFrom).ToListAsync(ct);
        return await CustomerPriceRules.ToDtosAsync(db, prices, ct);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record CreateCustomerPriceCommand : CustomerPriceInput, IRequest<CustomerPriceDto> {
    public Guid CurrentAccountId { get; init; }
}

public sealed class CreateCustomerPriceCommandValidator : AbstractValidator<CreateCustomerPriceCommand> {
    public CreateCustomerPriceCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreateCustomerPriceCommandHandler(
    IApplicationDbContext db,
    PriceHistoryWriter    history
) : IRequestHandler<CreateCustomerPriceCommand, CustomerPriceDto> {
    public async Task<CustomerPriceDto> Handle(CreateCustomerPriceCommand request, CancellationToken ct) {
        await CustomerPriceRules.EnsureAccountAsync(db, request.CurrentAccountId, ct);

        var price = new CustomerPrice();
        await CustomerPriceRules.ApplyAsync(db, price, request, request.CurrentAccountId, ct);
        db.CustomerPrices.Add(price);
        history.Record(PriceHistoryWriter.ScopeCustomer, price.CurrentAccountId, price.ProductId, price.VariantId, price.Unit, null,
                       price.Tiers, PriceListKind.Sale);
        await db.SaveChangesAsync(ct);
        return (await CustomerPriceRules.ToDtosAsync(db, [price], ct))[0];
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record UpdateCustomerPriceCommand : CustomerPriceInput, IRequest<CustomerPriceDto> {
    public Guid CurrentAccountId { get; init; }
    public Guid Id               { get; init; }
}

public sealed class UpdateCustomerPriceCommandValidator : AbstractValidator<UpdateCustomerPriceCommand> {
    public UpdateCustomerPriceCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateCustomerPriceCommandHandler(
    IApplicationDbContext db,
    PriceHistoryWriter    history
) : IRequestHandler<UpdateCustomerPriceCommand, CustomerPriceDto> {
    public async Task<CustomerPriceDto> Handle(UpdateCustomerPriceCommand request, CancellationToken ct) {
        var price = await db.CustomerPrices.FirstOrDefaultAsync(p => p.Id == request.Id && p.CurrentAccountId == request.CurrentAccountId, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "CustomerPrice");
        var before = price.Tiers.ToList();
        var key    = (price.ProductId, price.VariantId, price.Unit);

        await CustomerPriceRules.ApplyAsync(db, price, request, request.CurrentAccountId, ct);
        if (key != (price.ProductId, price.VariantId, price.Unit)) {
            // Kalem ya da birim değiştiyse eski kalem için silme, yenisi için oluşturma kaydı düşer.
            history.Record(PriceHistoryWriter.ScopeCustomer, price.CurrentAccountId, key.ProductId, key.VariantId, key.Unit, before, null,
                           PriceListKind.Sale);
            history.Record(PriceHistoryWriter.ScopeCustomer, price.CurrentAccountId, price.ProductId, price.VariantId, price.Unit, null,
                           price.Tiers, PriceListKind.Sale);
        } else if (!PricingRules.SameTiers(before, price.Tiers)) {
            history.Record(PriceHistoryWriter.ScopeCustomer, price.CurrentAccountId, price.ProductId, price.VariantId, price.Unit, before,
                           price.Tiers, PriceListKind.Sale);
        }

        await db.SaveChangesAsync(ct);
        return (await CustomerPriceRules.ToDtosAsync(db, [price], ct))[0];
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record DeleteCustomerPriceCommand(Guid CurrentAccountId, Guid Id) : IRequest;

public sealed class DeleteCustomerPriceCommandHandler(
    IApplicationDbContext db,
    PriceHistoryWriter    history
) : IRequestHandler<DeleteCustomerPriceCommand> {
    public async Task Handle(DeleteCustomerPriceCommand request, CancellationToken ct) {
        var price = await db.CustomerPrices.FirstOrDefaultAsync(p => p.Id == request.Id && p.CurrentAccountId == request.CurrentAccountId, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "CustomerPrice");
        history.Record(PriceHistoryWriter.ScopeCustomer, price.CurrentAccountId, price.ProductId, price.VariantId, price.Unit,
                       price.Tiers.ToList(), null, PriceListKind.Sale);
        db.CustomerPrices.Remove(price);
        await db.SaveChangesAsync(ct);
    }
}
