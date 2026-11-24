using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Campaigns;

public sealed record CampaignDto(
    Guid         Id,
    string       Name,
    CampaignKind Kind,
    int?         BuyQty,
    int?         PayQty,
    decimal?     Percent,
    DateOnly?    ValidFrom,
    DateOnly?    ValidTo,
    DayOfWeek[]? Days,
    TimeOnly?    FromTime,
    TimeOnly?    ToTime,
    Guid[]       ProductIds,
    Guid[]       VariantIds,
    Guid[]       CategoryIds,
    bool         IsActive
) {
    public static CampaignDto From(Campaign c) {
        return new CampaignDto(c.Id, c.Name, c.Kind, c.BuyQty, c.PayQty, c.Percent, c.ValidFrom, c.ValidTo, c.Days,
                               c.FromTime, c.ToTime, c.ProductIds, c.VariantIds, c.CategoryIds, c.IsActive);
    }
}

public interface ICampaignService {
    /// <summary>Sonuçlar satır sırasındadır; kampanya uygulanmayan satır null döner. Satırda CategoryId boşsa ürünün kategorisi kullanılır.</summary>
    Task<IReadOnlyList<CampaignResult?>> EvaluateAsync(IReadOnlyList<CampaignLine> lines, DateTimeOffset at, CancellationToken ct);
}

public sealed class CampaignService(IApplicationDbContext db) : ICampaignService {
    public async Task<IReadOnlyList<CampaignResult?>> EvaluateAsync(IReadOnlyList<CampaignLine> lines, DateTimeOffset at, CancellationToken ct) {
        if (lines.Count == 0) return [];

        var campaigns = await db.Campaigns.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
        if (campaigns.Count == 0) return lines.Select(_ => (CampaignResult?)null).ToList();

        var parents = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId })
                          .ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);

        // Kategori kapsamı alt kategorilere iner: kampanyanın kategorileri alt dallarıyla genişletilir.
        foreach (var campaign in campaigns.Where(c => c.CategoryIds.Length > 0)) {
            var scope = new HashSet<Guid>(campaign.CategoryIds);
            foreach (var id in parents.Keys) {
                var current = (Guid?)id;
                var guard   = 0;
                while (current is { } step && guard++ < 64) {
                    if (scope.Contains(step)) {
                        scope.Add(id);
                        break;
                    }

                    current = parents.GetValueOrDefault(step);
                }
            }

            campaign.CategoryIds = scope.ToArray();
        }

        var productIds = lines.Where(l => l.CategoryId is null).Select(l => l.ProductId).Distinct().ToList();
        var productCategories = productIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                  .ToDictionaryAsync(p => p.Id, p => p.CategoryId, ct);

        var istanbul = CurrentAccountClock.ToIstanbul(at);
        return lines.Select(l => {
            var line = l.CategoryId is null && productCategories.TryGetValue(l.ProductId, out var cat) ? l with { CategoryId = cat } : l;
            return CampaignEvaluator.Best(line, campaigns, istanbul);
        }).ToList();
    }
}

public abstract record CampaignInput {
    public string        Name        { get; init; } = string.Empty;
    public CampaignKind? Kind        { get; init; }
    public int?          BuyQty      { get; init; }
    public int?          PayQty      { get; init; }
    public decimal?      Percent     { get; init; }
    public DateOnly?     ValidFrom   { get; init; }
    public DateOnly?     ValidTo     { get; init; }
    public DayOfWeek[]?  Days        { get; init; }
    public TimeOnly?     FromTime    { get; init; }
    public TimeOnly?     ToTime      { get; init; }
    public Guid[]?       ProductIds  { get; init; }
    public Guid[]?       VariantIds  { get; init; }
    public Guid[]?       CategoryIds { get; init; }
    public bool          IsActive    { get; init; } = true;
}

public static class CampaignRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : CampaignInput {
        v.RuleFor(c => c.Name).NotEmpty().WithMessage("Kampanya adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 200).WithMessage("Kampanya adı en fazla 200 karakter olabilir.");
        v.RuleFor(c => c.Kind).NotNull().IsInEnum().WithMessage("Kampanya türü geçersiz.");
        v.RuleFor(c => c).Must(c => c.Kind != CampaignKind.BuyXPayY || (c.BuyQty is { } x && c.PayQty is { } y && y >= 1 && x > y))
            .WithMessage("X al Y öde için X, Y'den büyük ve Y en az 1 olmalıdır.").WithName("BuyQty");
        v.RuleFor(c => c).Must(c => c.Kind != CampaignKind.PercentOff || c.Percent is > 0m and <= 100m)
            .WithMessage("İndirim yüzdesi 0'dan büyük, en çok 100 olmalıdır.").WithName("Percent");
        v.RuleFor(c => c).Must(c => c.ValidFrom is null || c.ValidTo is null || c.ValidFrom <= c.ValidTo)
            .WithMessage("Geçerlilik başlangıcı bitişten sonra olamaz.").WithName("ValidFrom");
        v.RuleFor(c => c).Must(c => (c.FromTime is null) == (c.ToTime is null))
            .WithMessage("Saat penceresi için başlangıç ve bitiş birlikte verilmelidir.").WithName("FromTime");
        v.RuleFor(c => c.Days).Must(d => d is null || d.All(x => Enum.IsDefined(x))).WithMessage("Gün değerleri geçersiz.");
        v.RuleFor(c => c).Must(c => (c.ProductIds?.Length ?? 0) + (c.VariantIds?.Length ?? 0) + (c.CategoryIds?.Length ?? 0) > 0)
            .WithMessage("Kampanya için en az bir ürün, varyant ya da kategori seçin.").WithName("ProductIds");
    }

    public static void Apply(Campaign c, CampaignInput input) {
        c.Name        = input.Name.Trim();
        c.Kind        = input.Kind!.Value;
        c.BuyQty      = input.Kind == CampaignKind.BuyXPayY ? input.BuyQty : null;
        c.PayQty      = input.Kind == CampaignKind.BuyXPayY ? input.PayQty : null;
        c.Percent     = input.Kind == CampaignKind.PercentOff ? input.Percent : null;
        c.ValidFrom   = input.ValidFrom;
        c.ValidTo     = input.ValidTo;
        c.Days        = input.Days is { Length: > 0 } ? input.Days.Distinct().ToArray() : null;
        c.FromTime    = input.FromTime;
        c.ToTime      = input.ToTime;
        c.ProductIds  = input.ProductIds?.Distinct().ToArray() ?? [];
        c.VariantIds  = input.VariantIds?.Distinct().ToArray() ?? [];
        c.CategoryIds = input.CategoryIds?.Distinct().ToArray() ?? [];
        c.IsActive    = input.IsActive;
    }
}

[Authorize(Permission = PricingPermissions.Read)]
public sealed record ListCampaignsQuery(bool IncludeInactive) : IRequest<IReadOnlyList<CampaignDto>>;

public sealed class ListCampaignsQueryHandler(IApplicationDbContext db) : IRequestHandler<ListCampaignsQuery, IReadOnlyList<CampaignDto>> {
    public async Task<IReadOnlyList<CampaignDto>> Handle(ListCampaignsQuery request, CancellationToken ct) {
        var query = db.Campaigns.AsNoTracking();
        if (!request.IncludeInactive) query = query.Where(c => c.IsActive);
        var rows = await query.OrderBy(c => c.Name).ToListAsync(ct);
        return rows.Select(CampaignDto.From).ToList();
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record CreateCampaignCommand : CampaignInput, IRequest<CampaignDto>;

public sealed class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand> {
    public CreateCampaignCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreateCampaignCommandHandler(IApplicationDbContext db) : IRequestHandler<CreateCampaignCommand, CampaignDto> {
    public async Task<CampaignDto> Handle(CreateCampaignCommand request, CancellationToken ct) {
        var campaign = new Campaign();
        CampaignRules.Apply(campaign, request);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return CampaignDto.From(campaign);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record UpdateCampaignCommand : CampaignInput, IRequest<CampaignDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateCampaignCommandValidator : AbstractValidator<UpdateCampaignCommand> {
    public UpdateCampaignCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateCampaignCommandHandler(IApplicationDbContext db) : IRequestHandler<UpdateCampaignCommand, CampaignDto> {
    public async Task<CampaignDto> Handle(UpdateCampaignCommand request, CancellationToken ct) {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "Campaign");
        CampaignRules.Apply(campaign, request);
        await db.SaveChangesAsync(ct);
        return CampaignDto.From(campaign);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record DeleteCampaignCommand(Guid Id) : IRequest;

public sealed class DeleteCampaignCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteCampaignCommand> {
    public async Task Handle(DeleteCampaignCommand request, CancellationToken ct) {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "Campaign");
        db.Campaigns.Remove(campaign);
        await db.SaveChangesAsync(ct);
    }
}

public sealed record EvaluateLineInput(Guid ProductId, Guid? VariantId, decimal Quantity, decimal UnitPrice);

public sealed record EvaluateCampaignsResult(IReadOnlyList<CampaignResult?> Results);

[Authorize(Permission = PricingPermissions.Read)]
public sealed record EvaluateCampaignsQuery(List<EvaluateLineInput>? Lines, DateTimeOffset? At) : IRequest<EvaluateCampaignsResult>;

public sealed class EvaluateCampaignsQueryValidator : AbstractValidator<EvaluateCampaignsQuery> {
    public EvaluateCampaignsQueryValidator() {
        RuleFor(q => q.Lines).NotNull().WithMessage("Satırlar zorunludur.");
        RuleForEach(q => q.Lines).Must(l => l.Quantity > 0m && l.UnitPrice >= 0m)
            .WithMessage("Miktar sıfırdan büyük, birim fiyat negatif olmamalıdır.");
    }
}

public sealed class EvaluateCampaignsQueryHandler(ICampaignService service, TimeProvider time)
    : IRequestHandler<EvaluateCampaignsQuery, EvaluateCampaignsResult> {
    public async Task<EvaluateCampaignsResult> Handle(EvaluateCampaignsQuery request, CancellationToken ct) {
        var lines = request.Lines!.Select(l => new CampaignLine(l.ProductId, l.VariantId, null, l.Quantity, l.UnitPrice)).ToList();
        return new EvaluateCampaignsResult(await service.EvaluateAsync(lines, request.At ?? time.GetUtcNow(), ct));
    }
}
