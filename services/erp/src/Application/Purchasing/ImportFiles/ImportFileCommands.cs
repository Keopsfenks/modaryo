using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.ImportFiles;

public sealed record ImportFileInput(Guid SupplierId, string OriginCountry, Incoterm Incoterm, string Currency, string? Notes);

public sealed record ImportLinkInput(string Kind, Guid Id);

public sealed record ImportLinkDto(Guid Id, ImportLinkKind Kind, Guid RefId, string? Label);

public sealed record ImportFileDto(
    Guid                      Id,
    string                    Number,
    ImportFileStatus          Status,
    Guid                      SupplierId,
    string                    SupplierName,
    string                    OriginCountry,
    Incoterm                  Incoterm,
    string                    Currency,
    string?                   Notes,
    IReadOnlyList<ImportLinkDto> Links
);

public static class ImportFileRules {
    public const string Module = "ithalat";

    public static ImportLinkKind ParseKind(string kind) {
        return kind.Trim().ToLowerInvariant() switch {
            "po" or "purchaseorder"                     => ImportLinkKind.PurchaseOrder,
            "receipt" or "goodsreceipt"                 => ImportLinkKind.GoodsReceipt,
            "invoice" or "purchaseinvoice"              => ImportLinkKind.PurchaseInvoice,
            "landedcost"                                => ImportLinkKind.LandedCost,
            "attachment" or "file"                      => ImportLinkKind.Attachment,
            _                                           => throw PurchasingErrors.Bad("link_kind_invalid", "Bağ türü po, receipt, invoice, landedCost ya da attachment olmalıdır.")
        };
    }

    public static async Task<ImportFile> LoadAsync(IApplicationDbContext db, Guid id, bool track, CancellationToken ct) {
        var query = db.ImportFiles.Include(f => f.Links).AsQueryable();
        if (!track) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "ImportFile");
    }

    public static async Task<ImportFileDto> ToDtoAsync(IApplicationDbContext db, ImportFile file, CancellationToken ct) {
        var supplier = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == file.SupplierId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var ids      = file.Links.Select(l => l.RefId).ToList();
        var labels   = new Dictionary<Guid, string>();
        foreach (var (id, number) in await db.PurchaseOrders.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new ValueTuple<Guid, string>(x.Id, x.Number)).ToListAsync(ct)) labels[id] = number;
        foreach (var (id, number) in await db.GoodsReceipts.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new ValueTuple<Guid, string>(x.Id, x.Number)).ToListAsync(ct)) labels[id] = number;
        foreach (var (id, number) in await db.LandedCosts.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new ValueTuple<Guid, string>(x.Id, x.Number)).ToListAsync(ct)) labels[id] = number;
        foreach (var (id, number) in await db.Invoices.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new ValueTuple<Guid, string>(x.Id, x.Number ?? x.ExternalNumber ?? string.Empty)).ToListAsync(ct)) labels[id] = number;
        return new ImportFileDto(file.Id, file.Number, file.Status, file.SupplierId, supplier, file.OriginCountry, file.Incoterm, file.Currency, file.Notes,
                                 file.Links.OrderBy(l => l.Kind).ThenBy(l => l.Id).Select(l => new ImportLinkDto(l.Id, l.Kind, l.RefId, labels.GetValueOrDefault(l.RefId))).ToList());
    }

    public static async Task ValidateAsync(IApplicationDbContext db, ImportFileInput input, CancellationToken ct) {
        var supplier = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.SupplierId, ct) ?? throw new NotFoundException(input.SupplierId.ToString(), "CurrentAccount");
        if (!supplier.IsSupplier) throw PurchasingErrors.Bad("cari_not_supplier", "İthalat dosyası için cari tedarikçi olmalıdır.");
    }
}

public abstract class ImportFileInputValidator<T> : AbstractValidator<T> where T : IHasImportInput {
    protected ImportFileInputValidator() {
        RuleFor(c => c.Input.SupplierId).NotEmpty().WithMessage("Tedarikçi seçilmelidir.");
        RuleFor(c => c.Input.OriginCountry).NotEmpty().WithMessage("Menşe ülke zorunludur.").MaximumLength(80).WithMessage("Menşe ülke en fazla 80 karakter olabilir.");
        RuleFor(c => c.Input.Incoterm).IsInEnum().WithMessage("Incoterm geçersiz.");
        RuleFor(c => c.Input.Currency).NotEmpty().Length(3).WithMessage("Para birimi 3 harfli bir kod olmalıdır.");
        RuleFor(c => c.Input.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
    }
}

public interface IHasImportInput {
    ImportFileInput Input { get; }
}

[Authorize(Permission = PurchasingPermissions.Manage)]
[RequireModule(ImportFileRules.Module)]
public sealed record CreateImportFileCommand(ImportFileInput Input) : IRequest<ImportFileDto>, IHasImportInput;

public sealed class CreateImportFileCommandValidator : ImportFileInputValidator<CreateImportFileCommand>;

public sealed class CreateImportFileCommandHandler(IApplicationDbContext db) : IRequestHandler<CreateImportFileCommand, ImportFileDto> {
    public async Task<ImportFileDto> Handle(CreateImportFileCommand request, CancellationToken ct) {
        var input = request.Input;
        await ImportFileRules.ValidateAsync(db, input, ct);
        var year   = DateTime.UtcNow.AddHours(3).Year;
        var number = await db.NextDocumentNumberAsync("IT" + year, ct);
        var file = new ImportFile {
            Number = $"IT{year}{number:0000}", SupplierId = input.SupplierId, OriginCountry = input.OriginCountry.Trim(), Incoterm = input.Incoterm,
            Currency = input.Currency.Trim().ToUpperInvariant(), Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
        };
        db.ImportFiles.Add(file);
        await db.SaveChangesAsync(ct);
        return await ImportFileRules.ToDtoAsync(db, file, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Manage)]
[RequireModule(ImportFileRules.Module)]
public sealed record UpdateImportFileCommand(Guid Id, ImportFileInput Input) : IRequest<ImportFileDto>, IHasImportInput;

public sealed class UpdateImportFileCommandValidator : ImportFileInputValidator<UpdateImportFileCommand>;

public sealed class UpdateImportFileCommandHandler(IApplicationDbContext db) : IRequestHandler<UpdateImportFileCommand, ImportFileDto> {
    public async Task<ImportFileDto> Handle(UpdateImportFileCommand request, CancellationToken ct) {
        var file  = await ImportFileRules.LoadAsync(db, request.Id, true, ct);
        var input = request.Input;
        if (file.Status == ImportFileStatus.Closed) throw PurchasingErrors.Conflict("import_file_closed", "Kapalı ithalat dosyası değiştirilemez.");
        await ImportFileRules.ValidateAsync(db, input, ct);
        file.SupplierId    = input.SupplierId;
        file.OriginCountry = input.OriginCountry.Trim();
        file.Incoterm      = input.Incoterm;
        file.Currency      = input.Currency.Trim().ToUpperInvariant();
        file.Notes         = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return await ImportFileRules.ToDtoAsync(db, file, ct);
    }
}

/// <summary>İthalat dosyasına belge bağlar: sipariş, mal kabul, alış faturası, masraf dağıtımı ya da ek (P0b dosya kimliği).</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[RequireModule(ImportFileRules.Module)]
public sealed record LinkImportFileCommand(Guid Id, ImportLinkInput Input) : IRequest<ImportFileDto>;

public sealed class LinkImportFileCommandValidator : AbstractValidator<LinkImportFileCommand> {
    public LinkImportFileCommandValidator() {
        RuleFor(c => c.Input.Kind).NotEmpty().WithMessage("Bağ türü zorunludur.");
        RuleFor(c => c.Input.Id).NotEmpty().WithMessage("Belge seçilmelidir.");
    }
}

public sealed class LinkImportFileCommandHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<LinkImportFileCommand, ImportFileDto> {
    public async Task<ImportFileDto> Handle(LinkImportFileCommand request, CancellationToken ct) {
        var file = await ImportFileRules.LoadAsync(db, request.Id, true, ct);
        var kind = ImportFileRules.ParseKind(request.Input.Kind);
        var refId = request.Input.Id;
        if (file.Status == ImportFileStatus.Closed) throw PurchasingErrors.Conflict("import_file_closed", "Kapalı ithalat dosyasına belge bağlanamaz.");

        var branch = kind switch {
            ImportLinkKind.PurchaseOrder   => await db.PurchaseOrders.AsNoTracking().Where(x => x.Id == refId).Select(x => (Guid?)x.BranchId).FirstOrDefaultAsync(ct),
            ImportLinkKind.GoodsReceipt    => await db.GoodsReceipts.AsNoTracking().Where(x => x.Id == refId).Select(x => (Guid?)x.BranchId).FirstOrDefaultAsync(ct),
            ImportLinkKind.PurchaseInvoice => await db.Invoices.AsNoTracking().Where(x => x.Id == refId && x.Kind == ERP.Domain.Invoicing.InvoiceKind.Purchase).Select(x => (Guid?)x.BranchId).FirstOrDefaultAsync(ct),
            ImportLinkKind.LandedCost      => await db.LandedCosts.AsNoTracking().Where(x => x.Id == refId).Select(x => (Guid?)Guid.Empty).FirstOrDefaultAsync(ct),
            _                              => Guid.Empty
        };
        if (branch is null) throw new NotFoundException(refId.ToString(), kind.ToString());
        if (branch != Guid.Empty) BranchGuard.Ensure(tenant, branch.Value);

        if (file.Links.Any(l => l.Kind == kind && l.RefId == refId)) throw PurchasingErrors.Conflict("already_linked", "Belge bu dosyaya zaten bağlı.");
        file.Links.Add(new ImportFileLink { ImportFileId = file.Id, Kind = kind, RefId = refId });
        await db.SaveChangesAsync(ct);
        return await ImportFileRules.ToDtoAsync(db, file, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Manage)]
[RequireModule(ImportFileRules.Module)]
public sealed record CloseImportFileCommand(Guid Id) : IRequest<ImportFileDto>;

public sealed class CloseImportFileCommandHandler(IApplicationDbContext db) : IRequestHandler<CloseImportFileCommand, ImportFileDto> {
    public async Task<ImportFileDto> Handle(CloseImportFileCommand request, CancellationToken ct) {
        var file = await ImportFileRules.LoadAsync(db, request.Id, true, ct);
        file.Status = ImportFileStatus.Closed;
        await db.SaveChangesAsync(ct);
        return await ImportFileRules.ToDtoAsync(db, file, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Read)]
[RequireModule(ImportFileRules.Module)]
public sealed record GetImportFileQuery(Guid Id) : IRequest<ImportFileDto>;

public sealed class GetImportFileQueryHandler(IApplicationDbContext db) : IRequestHandler<GetImportFileQuery, ImportFileDto> {
    public async Task<ImportFileDto> Handle(GetImportFileQuery request, CancellationToken ct) {
        return await ImportFileRules.ToDtoAsync(db, await ImportFileRules.LoadAsync(db, request.Id, false, ct), ct);
    }
}

public sealed record ImportFileListItemDto(Guid Id, string Number, ImportFileStatus Status, Guid SupplierId, string SupplierName, string OriginCountry, Incoterm Incoterm, string Currency);

[Authorize(Permission = PurchasingPermissions.Read)]
[RequireModule(ImportFileRules.Module)]
public sealed record ListImportFilesQuery(ImportFileStatus? Status) : IRequest<IReadOnlyList<ImportFileListItemDto>>;

public sealed class ListImportFilesQueryHandler(IApplicationDbContext db) : IRequestHandler<ListImportFilesQuery, IReadOnlyList<ImportFileListItemDto>> {
    public async Task<IReadOnlyList<ImportFileListItemDto>> Handle(ListImportFilesQuery request, CancellationToken ct) {
        var query = db.ImportFiles.AsNoTracking().AsQueryable();
        if (request.Status is { } status) query = query.Where(f => f.Status == status);
        return await query.OrderByDescending(f => f.Number)
                          .Select(f => new ImportFileListItemDto(f.Id, f.Number, f.Status, f.SupplierId,
                                                                 db.CurrentAccounts.Where(c => c.Id == f.SupplierId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                                                                 f.OriginCountry, f.Incoterm, f.Currency)).ToListAsync(ct);
    }
}

public sealed record ImportCostDto(string CostType, decimal AmountTl);

public sealed record ImportItemDto(Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, decimal BaseQuantity, decimal GoodsValueTl, decimal AllocatedCostTl, decimal LandedUnitCostTl);

public sealed record ImportSummaryDto(Guid ImportFileId, decimal GoodsValueTl, IReadOnlyList<ImportCostDto> CostsByType, decimal TotalCostTl, decimal LandedTotalTl, IReadOnlyList<ImportItemDto> Items);

/// <summary>
///     Özet: mal değeri (bağlı mal kabullerin kabul anındaki TL maliyeti), masraf türüne göre işlenmiş masraflar ve kalem başına toplam
///     indirilmiş (landed) birim maliyet = (mal değeri + dosyanın masraflarından düşen pay) / temel birim miktarı.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Read)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
[RequireModule(ImportFileRules.Module)]
public sealed record GetImportSummaryQuery(Guid Id) : IRequest<ImportSummaryDto>;

public sealed class GetImportSummaryQueryHandler(IApplicationDbContext db) : IRequestHandler<GetImportSummaryQuery, ImportSummaryDto> {
    public async Task<ImportSummaryDto> Handle(GetImportSummaryQuery request, CancellationToken ct) {
        var file       = await ImportFileRules.LoadAsync(db, request.Id, false, ct);
        var receiptIds = file.Links.Where(l => l.Kind == ImportLinkKind.GoodsReceipt).Select(l => l.RefId).ToList();
        var linkedCost = file.Links.Where(l => l.Kind == ImportLinkKind.LandedCost).Select(l => l.RefId).ToList();

        var lines = await db.GoodsReceiptLines.AsNoTracking()
                            .Join(db.GoodsReceipts.Where(r => receiptIds.Contains(r.Id) && r.Status == GoodsReceiptStatus.Posted), l => l.GoodsReceiptId, r => r.Id, (l, _) => l).ToListAsync(ct);
        var lineIds = lines.Select(l => l.Id).ToList();

        var costs = await db.LandedCosts.AsNoTracking().Include(c => c.Sources).Include(c => c.Targets)
                            .Where(c => c.Status == LandedCostStatus.Posted && (c.ImportFileId == file.Id || linkedCost.Contains(c.Id))).ToListAsync(ct);
        var byType = costs.SelectMany(c => c.Sources).GroupBy(s => s.CostType ?? "diger").Select(g => new ImportCostDto(g.Key, g.Sum(s => s.AmountTl))).OrderBy(c => c.CostType).ToList();
        var share  = costs.SelectMany(c => c.Targets).Where(t => lineIds.Contains(t.ReceiptLineId)).GroupBy(t => t.ReceiptLineId).ToDictionary(g => g.Key, g => g.Sum(t => t.AllocatedTl));

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var items = lines.GroupBy(l => (l.ProductId, l.VariantId)).Select(g => {
            var quantity = g.Sum(l => l.BaseQuantity);
            var goods    = g.Sum(l => Math.Round((l.UnitCostTl ?? 0m) * l.BaseQuantity, 2, MidpointRounding.AwayFromZero));
            var cost     = g.Sum(l => share.GetValueOrDefault(l.Id));
            var product  = products[g.Key.ProductId];
            return new ImportItemDto(product.Id, product.Code, product.Name, g.Key.VariantId, quantity, goods, cost, quantity > 0m ? Math.Round((goods + cost) / quantity, 6, MidpointRounding.AwayFromZero) : 0m);
        }).OrderBy(i => i.ProductCode).ToList();

        var goodsValue = items.Sum(i => i.GoodsValueTl);
        return new ImportSummaryDto(file.Id, goodsValue, byType, byType.Sum(c => c.AmountTl), goodsValue + items.Sum(i => i.AllocatedCostTl), items);
    }
}
