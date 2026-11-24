using System.Text.RegularExpressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Series;

public sealed record SeriesDto(
    Guid        Id,
    string      KindGroup,
    Guid        BranchId,
    InvoiceMode Mode,
    string      Prefix,
    int         Year,
    long        NextNumber,
    int         MinWidth,
    bool        IsDefault,
    bool        IsActive,
    string      NextPreview
);

public abstract record SeriesInput {
    /// <summary>"sales" | "purchase"</summary>
    public string      KindGroup { get; init; } = "sales";
    public InvoiceMode Mode      { get; init; } = InvoiceMode.Paper;
    public string      Prefix    { get; init; } = string.Empty;
    public int?        Year      { get; init; }
    public int?        MinWidth  { get; init; }
    public bool        IsDefault { get; init; }
    public bool        IsActive  { get; init; } = true;
}

public static partial class SeriesRules {
    public static readonly string[] KindGroups = ["sales", "purchase"];

    public static void AddRules<T>(this AbstractValidator<T> v) where T : SeriesInput {
        v.RuleFor(s => s.KindGroup).Must(g => KindGroups.Contains(g)).WithMessage("Belge grubu \"sales\" ya da \"purchase\" olmalıdır.");
        v.RuleFor(s => s.Mode).IsInEnum().WithMessage("Belge modu geçersiz.");
        v.RuleFor(s => s.Prefix).NotEmpty().WithMessage("Seri öneki zorunludur.");
        // e-Belge: tam 3 karakter [A-Z0-9]; kâğıt: 1-10 karakter, boşluksuz.
        v.RuleFor(s => s).Must(s => s.Mode == InvoiceMode.Paper || EBelgePrefix().IsMatch(s.Prefix ?? string.Empty))
            .WithName("Prefix").WithMessage("e-Belge serisinin öneki 3 karakter (A-Z, 0-9) olmalıdır.");
        v.RuleFor(s => s).Must(s => s.Mode != InvoiceMode.Paper || PaperPrefix().IsMatch(s.Prefix ?? string.Empty))
            .WithName("Prefix").WithMessage("Kâğıt serisinin öneki 1-10 karakter olmalı ve boşluk içermemelidir.");
        v.RuleFor(s => s.Year).InclusiveBetween(2000, 2100).When(s => s.Year is not null).WithMessage("Yıl geçersiz.");
        v.RuleFor(s => s.MinWidth).InclusiveBetween(1, 12).When(s => s.MinWidth is not null)
            .WithMessage("Sıra genişliği 1 ile 12 arasında olmalıdır.");
    }

    [GeneratedRegex("^[A-Z0-9]{3}$")]
    private static partial Regex EBelgePrefix();

    [GeneratedRegex(@"^\S{1,10}$")]
    private static partial Regex PaperPrefix();

    public static SeriesDto ToDto(DocumentSeries s) {
        return new SeriesDto(s.Id, s.KindGroup, s.BranchId, s.Mode, s.Prefix, s.Year, s.NextNumber, s.MinWidth, s.IsDefault, s.IsActive,
                             s.Format(s.NextNumber));
    }

    public static string NormalizePrefix(InvoiceMode mode, string prefix) {
        var p = prefix.Trim();
        return mode == InvoiceMode.Paper ? p : p.ToUpperInvariant();
    }

    /// <summary>
    ///     Aynı (grup, şube, mod) içindeki diğer varsayılanları hemen (tek SQL ile) kaldırır. Kısmi tekil indeks
    ///     (IX_document_series_default) aynı anda tek varsayılana izin verdiği için yenisi kaydedilmeden ÖNCE çağrılır;
    ///     çağıran açık bir işlem içinde olmalıdır.
    /// </summary>
    public static async Task ClearOtherDefaultsAsync(IApplicationDbContext db, DocumentSeries keep, CancellationToken ct) {
        await db.DocumentSeries
            .Where(s => s.Id != keep.Id && s.IsDefault && s.KindGroup == keep.KindGroup && s.BranchId == keep.BranchId && s.Mode == keep.Mode)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDefault, false), ct);
    }

    /// <summary>Kaydeder; benzersizlik ihlalini anlamlı hataya çevirir (önek çakışması ya da eşzamanlı varsayılan değişimi).</summary>
    public static async Task SaveAsync(IApplicationDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, string prefix,
                                       CancellationToken ct) {
        try {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoicingErrors.ConstraintName(ex) == InvoicingErrors.DefaultSeriesIndex
                      ? InvoicingErrors.SeriesDefaultConflict()
                      : InvoicingErrors.SeriesExists(prefix);
        }
    }
}

[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record ListSeriesQuery(Guid? BranchId) : IRequest<IReadOnlyList<SeriesDto>>;

public sealed class ListSeriesQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListSeriesQuery, IReadOnlyList<SeriesDto>> {
    public async Task<IReadOnlyList<SeriesDto>> Handle(ListSeriesQuery request, CancellationToken ct) {
        var query = db.DocumentSeries.AsNoTracking();
        if (request.BranchId is { } branchId) query = query.Where(s => s.BranchId == branchId);
        var rows = await query.OrderBy(s => s.KindGroup).ThenBy(s => s.Mode).ThenBy(s => s.Prefix).ThenBy(s => s.Year).ToListAsync(ct);
        return rows.Where(s => tenant.CanAccessBranch(s.BranchId)).Select(SeriesRules.ToDto).ToList();
    }
}

[Authorize(Permission = InvoicingPermissions.SettingsDocuments)]
public sealed record CreateSeriesCommand : SeriesInput, IRequest<SeriesDto> {
    public Guid BranchId { get; init; }
}

public sealed class CreateSeriesCommandValidator : AbstractValidator<CreateSeriesCommand> {
    public CreateSeriesCommandValidator() {
        this.AddRules();
        RuleFor(c => c.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
    }
}

public sealed class CreateSeriesCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IBranchDirectory      branches
) : IRequestHandler<CreateSeriesCommand, SeriesDto> {
    public async Task<SeriesDto> Handle(CreateSeriesCommand request, CancellationToken ct) {
        BranchGuard.Ensure(tenant, request.BranchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, request.BranchId, ct)) throw InvoicingErrors.BranchNotFound();

        var prefix = SeriesRules.NormalizePrefix(request.Mode, request.Prefix);
        var year   = request.Year ?? DateTime.UtcNow.Year;
        if (await db.DocumentSeries.AnyAsync(s => s.KindGroup == request.KindGroup && s.Mode == request.Mode && s.Prefix == prefix && s.Year == year, ct))
            throw InvoicingErrors.SeriesExists(prefix);

        var hasDefault = await db.DocumentSeries.AnyAsync(s => s.IsDefault && s.KindGroup == request.KindGroup
                                                               && s.BranchId == request.BranchId && s.Mode == request.Mode, ct);
        var series = new DocumentSeries {
            KindGroup = request.KindGroup, BranchId = request.BranchId, Mode = request.Mode, Prefix = prefix, Year = year,
            MinWidth  = request.Mode == InvoiceMode.Paper ? request.MinWidth ?? 6 : 9,
            IsActive  = request.IsActive,
            // Grubun ilk serisi otomatik varsayılan olur.
            IsDefault = request.IsActive && (request.IsDefault || !hasDefault)
        };
        await using var tx = await db.BeginTransactionAsync(ct);
        if (series.IsDefault) await SeriesRules.ClearOtherDefaultsAsync(db, series, ct);
        db.DocumentSeries.Add(series);
        await SeriesRules.SaveAsync(db, tx, prefix, ct);

        return SeriesRules.ToDto(series);
    }
}

[Authorize(Permission = InvoicingPermissions.SettingsDocuments)]
public sealed record UpdateSeriesCommand : SeriesInput, IRequest<SeriesDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateSeriesCommandValidator : AbstractValidator<UpdateSeriesCommand> {
    public UpdateSeriesCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateSeriesCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<UpdateSeriesCommand, SeriesDto> {
    public async Task<SeriesDto> Handle(UpdateSeriesCommand request, CancellationToken ct) {
        var series = await db.DocumentSeries.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
                     ?? throw new NotFoundException(request.Id.ToString(), "DocumentSeries");
        BranchGuard.Ensure(tenant, series.BranchId);

        var prefix = SeriesRules.NormalizePrefix(request.Mode, request.Prefix);
        var year   = request.Year ?? series.Year;
        var identityChanged = prefix != series.Prefix || year != series.Year || request.Mode != series.Mode || request.KindGroup != series.KindGroup;
        if (identityChanged) {
            // Numara alınmış serinin kimliği değişmez; numaralı belgelerle tutarlılık bozulur.
            if (series.NextNumber > 1) throw InvoicingErrors.SeriesInUse();
            if (await db.DocumentSeries.AnyAsync(s => s.Id != series.Id && s.KindGroup == request.KindGroup && s.Mode == request.Mode
                                                      && s.Prefix == prefix && s.Year == year, ct))
                throw InvoicingErrors.SeriesExists(prefix);
        }

        series.KindGroup = request.KindGroup;
        series.Mode      = request.Mode;
        series.Prefix    = prefix;
        series.Year      = year;
        series.MinWidth  = request.Mode == InvoiceMode.Paper ? request.MinWidth ?? series.MinWidth : 9;
        series.IsActive  = request.IsActive;
        // Varsayılan seri PUT ile kaldırılamaz; başka seri varsayılan yapılınca düşer. Pasif seri varsayılan olamaz.
        series.IsDefault = request.IsActive && (request.IsDefault || series.IsDefault);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (series.IsDefault) await SeriesRules.ClearOtherDefaultsAsync(db, series, ct);
        await SeriesRules.SaveAsync(db, tx, prefix, ct);

        return SeriesRules.ToDto(series);
    }
}
