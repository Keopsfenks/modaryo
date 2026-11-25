using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Documents;
using ERP.Application.Purchasing.Invoices;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.LandedCosts;

public sealed record LandedSourceInput(Guid? InvoiceId, decimal? ManualAmountTl, string? Description, string CostType);

public sealed record LandedCostInput(DateOnly Date, AllocationKey Key, IReadOnlyList<Guid>? ReceiptIds, Guid? ImportFileId, string? Notes, IReadOnlyList<LandedSourceInput> Sources);

public static class LandedCostRules {
    public const string StockSource = "landed-cost";

    public static readonly string[] CostTypes = ["navlun", "gumruk", "sigorta", "diger"];

    public static string Label(string? type) {
        return type switch { "navlun" => "Navlun", "gumruk" => "Gümrük", "sigorta" => "Sigorta", "diger" => "Diğer", _ => type ?? "Diğer" };
    }

    public static async Task<StockMovement?> InboundAsync(IApplicationDbContext db, Guid receiptId, Guid receiptLineId, CancellationToken ct) {
        return await db.StockMovements.AsNoTracking()
                       .Where(m => m.SourceType == ReceiptInvoiceLinks.Source && m.SourceId == receiptId && m.SourceLineId == receiptLineId && m.Quantity > 0m)
                       .OrderBy(m => m.Sequence).FirstOrDefaultAsync(ct);
    }

    /// <summary>Kaynak başına dağıtım: her masraf kaynağı kendi içinde kuruşu kuruşuna dağıtılır, hedef payları toplanır.</summary>
    public static Dictionary<Guid, Dictionary<string, decimal>> AllocateBySource(IEnumerable<LandedCostSource> sources, AllocationKey key, IReadOnlyList<LandedTarget> targets) {
        var result = targets.ToDictionary(t => t.ReceiptLineId, _ => new Dictionary<string, decimal>());
        foreach (var source in sources) {
            var shares = LandedCostAllocator.Allocate(source.AmountTl, key, targets);
            var type   = source.CostType ?? "diger";
            foreach (var (lineId, amount) in shares)
                result[lineId][type] = result[lineId].GetValueOrDefault(type) + amount;
        }

        return result;
    }
}

/// <summary>
///     Masraf dağıtımı taslağı. Masraf kaynakları (kesilmiş alış faturası ya da TL tutar) seçilen anahtara göre mal kabul satırlarına
///     kuruşu kuruşuna (en büyük kalan) dağıtılır; ağırlık anahtarında ağırlığı olmayan kalem varsa 409 weight_missing ve hiçbir şey yazılmaz.
///     Hedef mal kabuller ya da ithalat dosyasına bağlı mal kabuller verilir. Maliyet etkisi yalnızca işlendiğinde (post) yazılır.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record CreateLandedCostCommand(LandedCostInput Input) : IRequest<LandedCostDto>;

public sealed class CreateLandedCostCommandValidator : AbstractValidator<CreateLandedCostCommand> {
    public CreateLandedCostCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Date).Must(d => d != default).WithMessage("Tarih zorunludur.");
        RuleFor(c => c.Input.Key).IsInEnum().WithMessage("Dağıtım anahtarı geçersiz.");
        RuleFor(c => c.Input.Sources).NotEmpty().WithMessage("En az bir masraf kaynağı girilmelidir.");
        RuleForEach(c => c.Input.Sources).ChildRules(s => {
            s.RuleFor(x => x.CostType).Must(t => LandedCostRules.CostTypes.Contains(t)).WithMessage("Masraf türü navlun, gumruk, sigorta ya da diger olmalıdır.");
            s.RuleFor(x => x).Must(x => (x.InvoiceId is null) != (x.ManualAmountTl is null)).WithMessage("Her kaynak için fatura ya da elle tutar (biri) girilmelidir.");
            s.RuleFor(x => x.ManualAmountTl).Must(a => a is null || (a > 0m && Math.Round(a.Value, 2) == a)).WithMessage("Tutar sıfırdan büyük olmalı ve en fazla 2 ondalık içermelidir.");
            s.RuleFor(x => x.Description).MaximumLength(200).WithMessage("Açıklama en fazla 200 karakter olabilir.");
        });
    }
}

public sealed class CreateLandedCostCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    LandedCostMapper      mapper
) : IRequestHandler<CreateLandedCostCommand, LandedCostDto> {
    public async Task<LandedCostDto> Handle(CreateLandedCostCommand request, CancellationToken ct) {
        var input  = request.Input;
        var landed = await BuildAsync(input, ct);
        landed.Number = await DocumentNumbers.NextAsync(db, "MD", input.Date, ct);
        db.LandedCosts.Add(landed);
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(landed, ct);
    }

    /// <summary>Doğrulama ve dağıtım hesabı; kayıt yazmaz. Önizleme ve oluşturma aynı yolu kullanır.</summary>
    internal async Task<LandedCost> BuildAsync(LandedCostInput input, CancellationToken ct) {

        // Hedef mal kabuller: verilenler + ithalat dosyasına bağlı olanlar.
        var receiptIds = (input.ReceiptIds ?? []).ToList();
        if (input.ImportFileId is { } fileId) {
            var file = await db.ImportFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct) ?? throw new NotFoundException(fileId.ToString(), "ImportFile");
            if (file.Status == ImportFileStatus.Closed) throw PurchasingErrors.Conflict("import_file_closed", "Kapalı ithalat dosyasına masraf dağıtılamaz.");
            receiptIds.AddRange(await db.ImportFileLinks.AsNoTracking().Where(l => l.ImportFileId == fileId && l.Kind == ImportLinkKind.GoodsReceipt).Select(l => l.RefId).ToListAsync(ct));
        }

        receiptIds = receiptIds.Distinct().ToList();
        if (receiptIds.Count == 0) throw PurchasingErrors.Bad("target_required", "Masraf dağıtımı için en az bir mal kabul seçilmelidir.");
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines).Where(r => receiptIds.Contains(r.Id)).ToListAsync(ct);
        if (receipts.Count != receiptIds.Count) throw new NotFoundException(string.Join(",", receiptIds.Except(receipts.Select(r => r.Id))), "GoodsReceipt");
        foreach (var receipt in receipts) BranchGuard.Ensure(tenant, receipt.BranchId);
        if (receipts.Any(r => r.Status == GoodsReceiptStatus.Cancelled)) throw PurchasingErrors.LandedCostTargetCancelled();

        var productIds = receipts.SelectMany(r => r.Lines).Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var targetRows = new List<LandedCostTarget>();
        var targets    = new List<LandedTarget>();
        var lineLabels = new Dictionary<Guid, string>();
        foreach (var receipt in receipts.OrderBy(r => r.Date).ThenBy(r => r.Number))
            foreach (var line in receipt.Lines.OrderBy(l => l.Sort)) {
                var product = products[line.ProductId];
                var value   = Math.Round((line.UnitCostTl ?? 0m) * line.BaseQuantity, 2, MidpointRounding.AwayFromZero);
                targetRows.Add(new LandedCostTarget {
                    GoodsReceiptId = receipt.Id, ReceiptLineId = line.Id, TlValue = value, BaseQuantity = line.BaseQuantity, WeightKgPerUnit = product.WeightKg
                });
                targets.Add(new LandedTarget(line.Id, value, line.BaseQuantity, product.WeightKg));
                lineLabels[line.Id] = product.Code;
            }

        // Kaynaklar.
        var invoiceIds = input.Sources.Where(s => s.InvoiceId is not null).Select(s => s.InvoiceId!.Value).ToList();
        var invoices   = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var used = invoiceIds.Count == 0 ? [] : await db.LandedCostSources.AsNoTracking()
                                                         .Where(s => s.InvoiceId != null && invoiceIds.Contains(s.InvoiceId.Value)
                                                                     && db.LandedCosts.Any(c => c.Id == s.LandedCostId && c.Status != LandedCostStatus.Cancelled))
                                                         .Select(s => s.InvoiceId!.Value).ToListAsync(ct);
        var sources = new List<LandedCostSource>();
        foreach (var s in input.Sources) {
            decimal amount;
            string  description;
            if (s.InvoiceId is { } invoiceId) {
                var invoice = invoices.GetValueOrDefault(invoiceId) ?? throw new NotFoundException(invoiceId.ToString(), "Invoice");
                BranchGuard.Ensure(tenant, invoice.BranchId);
                EnsureSourceInvoice(invoice);
                if (used.Contains(invoiceId)) throw PurchasingErrors.Conflict("invoice_already_allocated", "Bu fatura başka bir masraf dağıtımında kullanılıyor.");
                amount      = invoice.NetTl + invoice.OtvTl;
                description = string.IsNullOrWhiteSpace(s.Description) ? "Fatura " + (invoice.Number ?? invoice.ExternalNumber) : s.Description.Trim();
            } else {
                amount      = s.ManualAmountTl!.Value;
                description = string.IsNullOrWhiteSpace(s.Description) ? LandedCostRules.Label(s.CostType) : s.Description.Trim();
            }

            sources.Add(new LandedCostSource { InvoiceId = s.InvoiceId, Description = description, CostType = s.CostType, AmountTl = amount });
        }

        Dictionary<Guid, Dictionary<string, decimal>> shares;
        try {
            shares = LandedCostRules.AllocateBySource(sources, input.Key, targets);
        } catch (WeightMissingException ex) {
            var codes = ex.ReceiptLineIds.Select(id => lineLabels[id]).Distinct().Order().ToList();
            throw PurchasingErrors.WeightMissing($"Ağırlık anahtarı için kalem ağırlığı tanımlı olmalıdır. Ağırlığı eksik kalemler: {string.Join(", ", codes)}");
        } catch (ArgumentException) {
            throw PurchasingErrors.Bad("allocation_basis_zero", "Seçilen anahtara göre dağıtım tabanı sıfır; dağıtım yapılamaz.");
        }

        foreach (var row in targetRows) row.AllocatedTl = shares[row.ReceiptLineId].Values.Sum();

        var landed = new LandedCost {
            Status = LandedCostStatus.Draft, Date = input.Date, Key = input.Key, ImportFileId = input.ImportFileId, TotalTl = sources.Sum(s => s.AmountTl),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(), Sources = sources, Targets = targetRows
        };
        return landed;
    }

    internal static void EnsureSourceInvoice(Invoice invoice) {
        if (invoice.Kind != InvoiceKind.Purchase) throw PurchasingErrors.Bad("landed_cost_source_invalid", "Masraf kaynağı bir alış faturası olmalıdır.");
        if (invoice.SourceType == ReceiptInvoiceLinks.Source)
            throw PurchasingErrors.Conflict("landed_cost_source_invalid", "Mal kabule bağlı mal faturası masraf kaynağı olamaz; hizmet/masraf faturası seçin.");
        if (invoice.Status == InvoiceStatus.Cancelled) throw PurchasingErrors.Conflict("landed_cost_source_cancelled", "İptal edilmiş fatura masraf kaynağı olamaz.");
        if (invoice.Status != InvoiceStatus.Issued) throw PurchasingErrors.Conflict("landed_cost_source_not_issued", "Yalnızca kesilmiş fatura masraf kaynağı olabilir.");
    }
}

/// <summary>Masraf dağıtımı önizlemesi: oluşturma ile aynı doğrulama ve dağıtım, hiçbir şey yazılmaz (numara boş, kimlik boş).</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record PreviewLandedCostCommand(LandedCostInput Input) : IRequest<LandedCostDto>;

public sealed class PreviewLandedCostCommandValidator : AbstractValidator<PreviewLandedCostCommand> {
    public PreviewLandedCostCommandValidator() {
        RuleFor(c => new CreateLandedCostCommand(c.Input)).SetValidator(new CreateLandedCostCommandValidator());
    }
}

public sealed class PreviewLandedCostCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    LandedCostMapper      mapper
) : IRequestHandler<PreviewLandedCostCommand, LandedCostDto> {
    public async Task<LandedCostDto> Handle(PreviewLandedCostCommand request, CancellationToken ct) {
        var landed = await new CreateLandedCostCommandHandler(db, tenant, mapper).BuildAsync(request.Input, ct);
        return await mapper.ToDtoAsync(landed, ct);
    }
}

/// <summary>
///     Masraf dağıtımını işler: her hedef satırın payı mal kabul tarihine P3 maliyet düzeltmesi (CorrectCostAsync, yeniden maliyetlendirme
///     işi kuyruğa girer) olarak yazılır. Sıra: açık işlem, dönem kilidi ve TÜM stok kilitleri tek seferde, sonra durumun kilit altında yeniden okunması.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record PostLandedCostCommand(Guid Id) : IRequest<LandedCostDto>;

public sealed class PostLandedCostCommandHandler(
    IApplicationDbContext db,
    IPeriodLock           periodLock,
    CostCorrection        correction,
    IOutbox               outbox,
    LandedCostMapper      mapper
) : IRequestHandler<PostLandedCostCommand, LandedCostDto> {
    public async Task<LandedCostDto> Handle(PostLandedCostCommand request, CancellationToken ct) {
        var head = await db.LandedCosts.AsNoTracking().Include(c => c.Targets).FirstOrDefaultAsync(c => c.Id == request.Id, ct) ?? throw new NotFoundException(request.Id.ToString(), "LandedCost");

        await using var tx = await db.BeginTransactionAsync(ct);

        // Kilit planı: hedef satırların giriş hareketleri (depo x kalem) ve mal kabul tarihleri.
        var receiptIds = head.Targets.Select(t => t.GoodsReceiptId).Distinct().ToList();
        var receipts   = await db.GoodsReceipts.AsNoTracking().Where(r => receiptIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var keys       = new List<(Guid, Guid, Guid?)>();
        foreach (var target in head.Targets.Where(t => t.AllocatedTl != 0m)) {
            var inbound = await LandedCostRules.InboundAsync(db, target.GoodsReceiptId, target.ReceiptLineId, ct);
            if (inbound is not null) keys.Add((inbound.WarehouseId, inbound.ProductId, inbound.VariantId));
        }

        foreach (var date in receipts.Values.Select(r => r.Date).Distinct()) await periodLock.EnsureOpenAsync(date, ct);
        await db.LockStockAsync(keys, ct);

        var landed = await db.LandedCosts.Include(c => c.Sources).Include(c => c.Targets).FirstAsync(c => c.Id == request.Id, ct);
        if (landed.Status != LandedCostStatus.Draft) throw PurchasingErrors.Conflict("landed_cost_not_draft", "Yalnızca taslak masraf dağıtımı işlenebilir.");
        if (receipts.Values.Any(r => r.Status == GoodsReceiptStatus.Cancelled)) throw PurchasingErrors.LandedCostTargetCancelled();

        var invoiceIds = landed.Sources.Where(s => s.InvoiceId != null).Select(s => s.InvoiceId!.Value).ToList();
        foreach (var invoice in await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToListAsync(ct))
            CreateLandedCostCommandHandler.EnsureSourceInvoice(invoice);

        var lines = await db.GoodsReceiptLines.Where(l => receiptIds.Contains(l.GoodsReceiptId)).ToDictionaryAsync(l => l.Id, ct);
        foreach (var target in landed.Targets.Where(t => t.AllocatedTl != 0m)) {
            var inbound = await LandedCostRules.InboundAsync(db, target.GoodsReceiptId, target.ReceiptLineId, ct);
            if (inbound is null) continue;
            await correction.CorrectAsync(inbound, lines[target.ReceiptLineId], receipts[target.GoodsReceiptId].Date, target.AllocatedTl, LandedCostRules.StockSource, landed.Id, ct);
        }

        landed.Status = LandedCostStatus.Posted;
        outbox.Enqueue("purchase.landed-cost-posted", new { landedCostId = landed.Id, number = landed.Number, totalTl = landed.TotalTl });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(landed, ct);
    }
}

public sealed record LandedCostCancelBody(string Reason);

/// <summary>İşlenmiş masraf dağıtımını iptal eder: yazılan maliyet düzeltmeleri ters yönde yazılır (dönem kilidi mal kabul tarihlerinde geçerlidir).</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record CancelLandedCostCommand(Guid Id, string Reason) : IRequest<LandedCostDto>;

public sealed class CancelLandedCostCommandValidator : AbstractValidator<CancelLandedCostCommand> {
    public CancelLandedCostCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.").Length(2, 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelLandedCostCommandHandler(
    IApplicationDbContext db,
    CostCorrection        correction,
    IOutbox               outbox,
    LandedCostMapper      mapper
) : IRequestHandler<CancelLandedCostCommand, LandedCostDto> {
    public async Task<LandedCostDto> Handle(CancelLandedCostCommand request, CancellationToken ct) {
        var exists = await db.LandedCosts.AsNoTracking().AnyAsync(c => c.Id == request.Id, ct);
        if (!exists) throw new NotFoundException(request.Id.ToString(), "LandedCost");

        await using var tx = await db.BeginTransactionAsync(ct);
        var keys = await correction.ReverseKeysAsync(LandedCostRules.StockSource, request.Id, ct);
        if (keys.Count > 0) await db.LockStockAsync(keys, ct);

        var landed = await db.LandedCosts.FirstAsync(c => c.Id == request.Id, ct);
        if (landed.Status == LandedCostStatus.Cancelled) throw PurchasingErrors.Conflict("landed_cost_cancelled", "Masraf dağıtımı zaten iptal edilmiş.");
        if (landed.Status == LandedCostStatus.Posted) await correction.ReverseAsync(LandedCostRules.StockSource, landed.Id, ct);

        landed.Status       = LandedCostStatus.Cancelled;
        landed.CancelReason = request.Reason.Trim();
        outbox.Enqueue("purchase.landed-cost-cancelled", new { landedCostId = landed.Id, number = landed.Number, reason = landed.CancelReason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(landed, ct);
    }
}
