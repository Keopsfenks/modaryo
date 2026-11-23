using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Lists;

public abstract record PriceListInput {
    public string         Name              { get; init; } = string.Empty;
    public PriceListKind? Kind              { get; init; }
    public string         Currency          { get; init; } = "TRY";
    public bool           VatIncluded       { get; init; }
    public DateOnly?      ValidFrom         { get; init; }
    public DateOnly?      ValidTo           { get; init; }
    public Guid?          SupplierId        { get; init; }
    public Guid?          BaseListId        { get; init; }
    public decimal?       AdjustmentPercent { get; init; }
    public bool           IsActive          { get; init; } = true;
}

public static class PriceListRules {
    public const int MaxDepth = 3;

    public static void AddRules<T>(this AbstractValidator<T> v) where T : PriceListInput {
        v.RuleFor(c => c.Name).NotEmpty().WithMessage("Liste adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 200).WithMessage("Liste adı en fazla 200 karakter olabilir.");
        v.RuleFor(c => c.Currency).Must(c => PricingRules.Currencies.Contains(c))
            .WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
        v.RuleFor(c => c.Kind).IsInEnum().When(c => c.Kind is not null).WithMessage("Liste türü geçersiz.");
        v.RuleFor(c => c).Must(c => c.ValidFrom is null || c.ValidTo is null || c.ValidFrom <= c.ValidTo)
            .WithMessage("Geçerlilik başlangıcı bitişten sonra olamaz.").WithName("ValidFrom");
        v.RuleFor(c => c.AdjustmentPercent).NotNull().When(c => c.BaseListId is not null)
            .WithMessage("Türetilmiş liste için yüzde ayarı zorunludur.");
        v.RuleFor(c => c.AdjustmentPercent).InclusiveBetween(-100m, 1000m).When(c => c.AdjustmentPercent is not null)
            .WithMessage("Yüzde ayarı -100 ile 1000 arasında olmalıdır.");
    }

    /// <summary>Girdiyi listeye yazar: tür, tedarikçi ve taban liste kurallarını denetler (kaydetmez).</summary>
    public static async Task ApplyAsync(IApplicationDbContext db, PriceList list, PriceListInput input, bool isNew,
                                        CancellationToken ct) {
        var kind = input.Kind ?? list.Kind;
        if (!isNew && input.Kind is { } given && given != list.Kind)
            throw new AppException(400, "kind_immutable", "Liste türü sonradan değiştirilemez.");

        var currency = input.Currency.Trim().ToUpperInvariant();

        if (input.SupplierId is { } supplierId) {
            if (kind != PriceListKind.Purchase)
                throw new AppException(400, "invalid_supplier", "Tedarikçi yalnızca alış listelerine bağlanabilir.");
            if (!await db.CurrentAccounts.AnyAsync(a => a.Id == supplierId && a.IsSupplier, ct))
                throw new AppException(400, "invalid_supplier", "Tedarikçi bulunamadı.");
        }

        var lists = await db.PriceLists.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        if (input.BaseListId is { } baseId) {
            if (!lists.TryGetValue(baseId, out var baseList))
                throw new AppException(400, "invalid_base_list", "Taban liste bulunamadı.");
            if (baseList.Kind != kind)
                throw new AppException(400, "invalid_base_list", "Taban liste aynı türde olmalıdır.");
            if (baseList.Currency != currency)
                throw new AppException(400, "currency_mismatch", "Türetilmiş listenin para birimi taban listeyle aynı olmalıdır.");

            // Taban zinciri bu listeye ulaşırsa döngü olur.
            var up = 0;
            Guid? current = baseId;
            var seen = new HashSet<Guid>();
            while (current is { } cid && lists.TryGetValue(cid, out var node)) {
                if (!isNew && cid == list.Id)
                    throw new AppException(409, "list_cycle", "Liste kendi türevine bağlanamaz (döngü).");
                if (!seen.Add(cid)) break;

                up++;
                current = node.BaseListId;
            }

            var down = isNew ? 0 : Descendants(lists, list.Id, 0);
            if (up + 1 + down > MaxDepth)
                throw new AppException(409, "list_depth", $"Türetilmiş liste zinciri en fazla {MaxDepth} düzey olabilir.");
        }

        if (!isNew && currency != list.Currency && lists.Values.Any(l => l.BaseListId == list.Id))
            throw new AppException(400, "currency_mismatch", "Türevi olan listenin para birimi değiştirilemez.");

        list.Name              = input.Name.Trim();
        list.Kind              = kind;
        list.Currency          = currency;
        list.VatIncluded       = input.VatIncluded;
        list.ValidFrom         = input.ValidFrom;
        list.ValidTo           = input.ValidTo;
        list.SupplierId        = input.SupplierId;
        list.BaseListId        = input.BaseListId;
        list.AdjustmentPercent = input.BaseListId is null ? null : input.AdjustmentPercent;
        list.IsActive          = input.IsActive;
    }

    private static int Descendants(IReadOnlyDictionary<Guid, PriceList> lists, Guid id, int level) {
        if (level > 8) return level;

        var max = 0;
        foreach (var child in lists.Values.Where(l => l.BaseListId == id))
            max = Math.Max(max, 1 + Descendants(lists, child.Id, level + 1));

        return max;
    }
}

[Authorize(Permission = PricingPermissions.Read)]
public sealed record ListPriceListsQuery(PriceListKind? Kind, bool IncludeInactive) : IRequest<IReadOnlyList<PriceListDto>>;

public sealed class ListPriceListsQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<ListPriceListsQuery, IReadOnlyList<PriceListDto>> {
    public async Task<IReadOnlyList<PriceListDto>> Handle(ListPriceListsQuery request, CancellationToken ct) {
        var query = db.PriceLists.AsNoTracking().AsQueryable();
        if (!session.HasPermission(PricingPermissions.Purchase)) query = query.Where(l => l.Kind == PriceListKind.Sale);
        if (request.Kind is { } kind) query                            = query.Where(l => l.Kind == kind);
        if (!request.IncludeInactive) query                            = query.Where(l => l.IsActive);

        var lists  = await query.OrderBy(l => l.Name).ToListAsync(ct);
        var ids    = lists.Select(l => l.Id).ToList();
        var counts = await db.PriceListItems.AsNoTracking().Where(i => ids.Contains(i.PriceListId))
                         .GroupBy(i => i.PriceListId).Select(g => new { Id = g.Key, Count = g.Count() })
                         .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return lists.Select(l => PriceListDto.From(l, counts.GetValueOrDefault(l.Id))).ToList();
    }
}

[Authorize(Permission = PricingPermissions.Read)]
public sealed record GetPriceListQuery(Guid Id) : IRequest<PriceListDto>;

public sealed class GetPriceListQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<GetPriceListQuery, PriceListDto> {
    public async Task<PriceListDto> Handle(GetPriceListQuery request, CancellationToken ct) {
        var list  = await PricingRules.LoadListAsync(db, session, request.Id, ct, false);
        var count = await db.PriceListItems.CountAsync(i => i.PriceListId == list.Id, ct);
        return PriceListDto.From(list, count);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record CreatePriceListCommand : PriceListInput, IRequest<PriceListDto>;

public sealed class CreatePriceListCommandValidator : AbstractValidator<CreatePriceListCommand> {
    public CreatePriceListCommandValidator() {
        this.AddRules();
        RuleFor(c => c.Kind).NotNull().WithMessage("Liste türü zorunludur.");
    }
}

public sealed class CreatePriceListCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<CreatePriceListCommand, PriceListDto> {
    public async Task<PriceListDto> Handle(CreatePriceListCommand request, CancellationToken ct) {
        PricingRules.EnsureKindAccess(session, request.Kind!.Value);

        var list = new PriceList();
        await PriceListRules.ApplyAsync(db, list, request, true, ct);
        db.PriceLists.Add(list);
        await db.SaveChangesAsync(ct);
        return PriceListDto.From(list, 0);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record UpdatePriceListCommand : PriceListInput, IRequest<PriceListDto> {
    public Guid  Id      { get; init; }
    public uint? Version { get; init; }
}

public sealed class UpdatePriceListCommandValidator : AbstractValidator<UpdatePriceListCommand> {
    public UpdatePriceListCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdatePriceListCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<UpdatePriceListCommand, PriceListDto> {
    public async Task<PriceListDto> Handle(UpdatePriceListCommand request, CancellationToken ct) {
        var list = await PricingRules.LoadListAsync(db, session, request.Id, ct);
        if (request.Version is { } version && version != list.Version) throw PricingRules.VersionConflict();

        await PriceListRules.ApplyAsync(db, list, request, false, ct);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            throw PricingRules.VersionConflict();
        }

        var count = await db.PriceListItems.CountAsync(i => i.PriceListId == list.Id, ct);
        return PriceListDto.From(list, count);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record DeletePriceListCommand(Guid Id) : IRequest;

public sealed class DeletePriceListCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ITenantSettings       settings
) : IRequestHandler<DeletePriceListCommand> {
    public async Task Handle(DeletePriceListCommand request, CancellationToken ct) {
        var list = await PricingRules.LoadListAsync(db, session, request.Id, ct);

        var pricing = await settings.GetAsync<PricingSettings>(ct);
        var inUse = pricing.DefaultSaleListId == list.Id || pricing.DefaultPurchaseListId == list.Id
                    || await db.CustomerGroups.AnyAsync(g => g.SalePriceListId == list.Id, ct)
                    || await db.PriceLists.AnyAsync(l => l.BaseListId == list.Id, ct);
        if (inUse)
            throw new AppException(409, "list_in_use",
                                   "Liste varsayılan liste, müşteri grubu ya da türetilmiş bir listenin tabanı olarak kullanılıyor.");

        db.PriceLists.Remove(list);
        await db.SaveChangesAsync(ct);
    }
}
