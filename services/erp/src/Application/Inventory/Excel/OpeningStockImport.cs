using System.Globalization;
using System.Text.Json;
using ERP.Application.Catalog.Excel;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Settings;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Excel;

/// <summary>Önizlemede saklanan ham satır; alanlar hücre metnidir (sütun dosyada yoksa boş).</summary>
public sealed record OpeningRow(int RowNumber, string Warehouse, string Product, string Variant, string Quantity, string Unit, string Cost,
                                string Lot, string Expiry, string Serial);

public sealed record OpeningRowResult(int RowNumber, string Action, string? WarehouseCode, string? ProductCode, IReadOnlyList<string> Errors);

public sealed record OpeningPreview(Guid BatchId, int Total, int Creates, int Errors, IReadOnlyList<OpeningRowResult> Rows);

public sealed record OpeningCommitResult(int Documents, int Lines);

public sealed record StoredOpening(List<OpeningRow> Rows, List<string> Actions);

public sealed record OpeningCommitOutcome(OpeningCommitResult? Done, OpeningPreview? Conflict);

/// <summary>Açılış stoğu tablosunun sütunları.</summary>
public static class OpeningSheet {
    public const string SheetName = "Açılış";
    public const string Kind      = "opening-stock";
    public const int    MaxRows   = 5000;
    public const long   MaxBytes  = 5L * 1024 * 1024;

    public const string WarehouseH = "Depo kodu";
    public const string ProductH   = "Ürün kodu";
    public const string VariantH   = "Varyant kodu";
    public const string QuantityH  = "Miktar";
    public const string UnitH      = "Birim";
    public const string CostH      = "Birim maliyet";
    public const string LotH       = "Lot";
    public const string ExpiryH    = "SKT";
    public const string SerialH    = "Seri";

    public static readonly IReadOnlyList<string> Headers = [WarehouseH, ProductH, VariantH, QuantityH, UnitH, CostH, LotH, ExpiryH, SerialH];

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy"];

    public static bool TryDate(string text, out DateOnly date) {
        return DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static List<OpeningRow> ToRows(XlsxTable table) {
        var index = new Dictionary<string, int>();
        for (var i = 0; i < table.Headers.Count; i++) index.TryAdd(TurkishText.Fold(table.Headers[i]), i);

        foreach (var required in new[] { WarehouseH, ProductH })
            if (!index.ContainsKey(TurkishText.Fold(required)))
                throw new AppException(400, "invalid_template",
                                       $"Dosyada \"{required}\" sütunu yok. Şablonu indirip başlıkları değiştirmeden kullanın.");

        string Cell(XlsxRow row, string header) {
            return index.TryGetValue(TurkishText.Fold(header), out var i) && i < row.Cells.Count ? row.Cells[i].Trim() : string.Empty;
        }

        return table.Rows.Select(r => new OpeningRow(r.RowNumber, Cell(r, WarehouseH), Cell(r, ProductH), Cell(r, VariantH), Cell(r, QuantityH),
                                                     Cell(r, UnitH), Cell(r, CostH), Cell(r, LotH), Cell(r, ExpiryH), Cell(r, SerialH))).ToList();
    }
}

public static class OpeningBatches {
    public static StoredOpening Read(ImportBatch batch) {
        return JsonSerializer.Deserialize<StoredOpening>(batch.RowsJson, SettingsRegistry.Json)!;
    }

    public static OpeningPreview ToPreview(Guid batchId, IReadOnlyList<OpeningRowResult> results) {
        return new OpeningPreview(batchId, results.Count, results.Count(r => r.Action == "create"), results.Count(r => r.Action == "error"), results);
    }

    public static ImportBatch Create(IApplicationDbContext db, ICurrentSession session, TimeProvider time, IReadOnlyList<OpeningRow> rows,
                                     IReadOnlyList<OpeningRowResult> results) {
        var now = time.GetUtcNow();
        var batch = new ImportBatch {
            Kind = OpeningSheet.Kind,
            RowsJson = JsonSerializer.Serialize(new StoredOpening(rows.ToList(), results.Select(r => r.Action).ToList()), SettingsRegistry.Json),
            CreatedAt = now, CreatedBy = session.UserId ?? throw new UnauthorizedAccessException(), ExpiresAt = now + ERP.Application.Catalog.Excel.Commands.ImportBatches.Lifetime
        };
        db.ImportBatches.Add(batch);
        return batch;
    }
}

/// <summary>
///     Satırları güncel veritabanına karşı uygular (önizleme ve uygulama aynı kodu kullanır). Çağıran işlemi açar:
///     önizlemede her zaman geri alır, uygulamada yalnızca hiçbir satır hata vermediyse onaylar. Depo başına tek açılış belgesi yazılır.
/// </summary>
public sealed class OpeningStockEngine(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    TimeProvider          time
) {
    private sealed class Parsed {
        public required OpeningRow Row         { get; init; }
        public Warehouse?          Warehouse   { get; set; }
        public ResolvedLine?       Line        { get; set; }
        public Guid?               ProductId   => Line?.ProductId;
        public List<string>        Errors      { get; } = [];
    }

    public sealed record ApplyOutcome(IReadOnlyList<OpeningRowResult> Results, int Documents, int Lines);

    public async Task<ApplyOutcome> ApplyAsync(IReadOnlyList<OpeningRow> rows, CancellationToken ct) {
        var warehouses = (await db.Warehouses.AsNoTracking().ToListAsync(ct)).Where(w => tenant.CanAccessBranch(w.BranchId))
                                                                              .GroupBy(w => TurkishText.Fold(w.Code)).ToDictionary(g => g.Key, g => g.First());
        var products = (await db.Products.AsNoTracking().Include(p => p.Variants).Include(p => p.Units).ToListAsync(ct))
                       .GroupBy(p => p.Code).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var parsed = rows.Select(r => Parse(r, warehouses, products)).ToList();

        // Aynı kalem x depo grubunda maliyetler çelişmemeli.
        foreach (var group in parsed.Where(p => p.Errors.Count == 0).GroupBy(p => (p.Warehouse!.Id, p.Line!.ProductId, p.Line.VariantId))) {
            var costs = group.Where(p => p.Line!.BaseUnitCost is not null).Select(p => p.Line!.BaseUnitCost).Distinct().ToList();
            if (costs.Count > 1)
                foreach (var p in group) p.Errors.Add("Aynı kalemin satırlarında birim maliyetler farklı.");
        }

        if (parsed.All(p => p.Errors.Count == 0)) await WriteAsync(parsed, ct);

        var results = parsed.Select(p => new OpeningRowResult(p.Row.RowNumber, p.Errors.Count > 0 ? "error" : "create", p.Row.Warehouse.Length > 0 ? p.Row.Warehouse : null,
                                                              p.Row.Product.Length > 0 ? p.Row.Product : null, p.Errors)).ToList();
        var written = parsed.Where(p => p.Errors.Count == 0).ToList();
        return new ApplyOutcome(results, written.Select(p => p.Warehouse!.Id).Distinct().Count(),
                                written.GroupBy(p => (p.Warehouse!.Id, p.Line!.ProductId, p.Line.VariantId)).Count());
    }

    private Parsed Parse(OpeningRow row, Dictionary<string, Warehouse> warehouses, Dictionary<string, Product> products) {
        var p = new Parsed { Row = row };
        if (row.Warehouse.Length == 0) p.Errors.Add("Depo kodu boş olamaz.");
        else if (!warehouses.TryGetValue(TurkishText.Fold(row.Warehouse), out var warehouse)) p.Errors.Add($"\"{row.Warehouse}\" kodlu depo bulunamadı.");
        else if (!warehouse.IsActive) p.Errors.Add($"\"{row.Warehouse}\" deposu pasif.");
        else p.Warehouse = warehouse;

        Product? product = null;
        if (row.Product.Length == 0) p.Errors.Add("Ürün kodu boş olamaz.");
        else if (!products.TryGetValue(row.Product, out product)) p.Errors.Add($"\"{row.Product}\" kodlu ürün bulunamadı.");
        else if (product.Kind == ProductKind.Service) p.Errors.Add($"\"{row.Product}\" bir hizmet; stok girişi yapılamaz.");
        else if (product.IsBundle) p.Errors.Add($"\"{row.Product}\" set ürün; bileşenlerine giriş yapın.");

        Guid? variantId = null;
        if (product is not null && row.Variant.Length > 0) {
            var variant = product.Variants.FirstOrDefault(v => string.Equals(v.Code, row.Variant, StringComparison.OrdinalIgnoreCase));
            if (variant is null) p.Errors.Add($"\"{row.Variant}\" kodlu varyant bu üründe yok.");
            else variantId = variant.Id;
        }

        var quantity = 0m;
        if (row.Quantity.Length == 0) {
            if (row.Serial.Length > 0) quantity = 1m;
            else p.Errors.Add("Miktar boş olamaz.");
        } else if (!ProductSheet.TryDecimal(row.Quantity, out quantity) || quantity <= 0m) {
            p.Errors.Add("Miktar sıfırdan büyük bir sayı olmalıdır.");
        }

        decimal? cost = null;
        if (row.Cost.Length > 0) {
            if (ProductSheet.TryDecimal(row.Cost, out var parsedCost) && parsedCost >= 0m) cost = parsedCost;
            else p.Errors.Add("Birim maliyet sıfır veya daha büyük bir sayı olmalıdır.");
        }

        DateOnly? expiry = null;
        if (row.Expiry.Length > 0) {
            if (OpeningSheet.TryDate(row.Expiry, out var date)) expiry = date;
            else p.Errors.Add("SKT geçerli bir tarih olmalıdır (örn. 2027-12-31).");
        }

        if (p.Errors.Count > 0 || product is null) return p;

        try {
            var lots    = row.Lot.Length > 0 ? new List<LotInput> { new(row.Lot, expiry, quantity, null) } : null;
            var serials = row.Serial.Length > 0 ? new List<string> { row.Serial } : null;
            if (serials is not null && quantity != 1m) throw new AppException(400, "serial_quantity_mismatch", "Seri numaralı satırın miktarı 1 olmalıdır.");

            var unit = row.Unit.Length > 0 ? row.Unit : product.BaseUnit;
            p.Line = LineResolver.Resolve(new LineInput(product.Id, variantId, unit, quantity, cost, lots, serials), product);
        } catch (AppException ex) {
            p.Errors.Add(ex.Message);
        }

        return p;
    }

    private async Task WriteAsync(List<Parsed> parsed, CancellationToken ct) {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        foreach (var byWarehouse in parsed.GroupBy(p => p.Warehouse!.Id)) {
            var document = await StockDocumentWriter.BeginAsync(db, tenant, StockDocumentKind.Opening, today, byWarehouse.Key, "Açılış aktarımı", ct);
            await db.SaveChangesAsync(ct);
            var documentId = document.Id;

            foreach (var group in byWarehouse.GroupBy(p => (p.Line!.ProductId, p.Line.VariantId))) {
                var items = group.ToList();
                var lines = items.Select(i => i.Line!).ToList();
                var merged = new ResolvedLine(lines[0].ProductId, lines[0].VariantId, lines[0].Unit, lines.Sum(l => l.Quantity), lines.Sum(l => l.BaseQuantity),
                                              lines.Select(l => l.BaseUnitCost).FirstOrDefault(c => c is not null),
                                              lines.Any(l => l.Lots is not null) ? lines.SelectMany(l => l.Lots ?? []).ToList() : null,
                                              lines.Any(l => l.Serials is not null) ? lines.SelectMany(l => l.Serials ?? []).ToList() : null);
                try {
                    var current = await db.StockDocuments.FirstAsync(d => d.Id == documentId, ct);
                    await StockDocumentWriter.WriteLinesAsync(db, stock, current, [merged], ct);
                } catch (AppException ex) {
                    foreach (var item in items) item.Errors.Add(ex.Message);
                    db.ClearTracking(); // yarım kalan değişiklikler sonraki gruplara taşınmasın
                }
            }
        }
    }
}

public static class OpeningImportErrors {
    public static AppException AlreadyCommitted() {
        return new AppException(409, "import_already_committed", "Bu içe aktarma zaten uygulandı.");
    }
}

/// <summary>Dosyayı okur ve satırları doğrular; hiçbir şey yazmaz (işlem geri alınır).</summary>
[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record PreviewOpeningImportCommand(byte[] Content) : IRequest<OpeningPreview>;

public sealed class PreviewOpeningImportCommandHandler(
    IApplicationDbContext db,
    IXlsxService          xlsx,
    OpeningStockEngine    engine,
    ICurrentSession       session,
    TimeProvider          time
) : IRequestHandler<PreviewOpeningImportCommand, OpeningPreview> {
    public async Task<OpeningPreview> Handle(PreviewOpeningImportCommand request, CancellationToken ct) {
        if (request.Content.Length > OpeningSheet.MaxBytes)
            throw new AppException(413, "file_too_large", "Dosya çok büyük; en fazla 5 MB yüklenebilir.");

        XlsxTable table;
        try {
            using var stream = new MemoryStream(request.Content);
            table = xlsx.Read(stream, OpeningSheet.MaxRows);
        } catch (InvalidDataException) {
            throw new AppException(400, "invalid_file", "Dosya okunamadı. Geçerli bir .xlsx dosyası yükleyin.");
        }

        if (table.Rows.Count > OpeningSheet.MaxRows)
            throw new AppException(400, "too_many_rows", "Bir seferde en fazla 5.000 satır içe aktarılabilir.");

        var rows = OpeningSheet.ToRows(table);
        if (rows.Count == 0) throw new AppException(400, "empty_file", "Dosyada içe aktarılacak satır yok.");

        OpeningStockEngine.ApplyOutcome outcome;
        await using (var transaction = await db.BeginTransactionAsync(ct)) {
            outcome = await engine.ApplyAsync(rows, ct);
            await transaction.RollbackAsync(ct);
        }

        db.ClearTracking();
        var stale = await db.ImportBatches.Where(b => b.ExpiresAt < time.GetUtcNow().AddDays(-1)).ToListAsync(ct);
        db.ImportBatches.RemoveRange(stale);
        var batch = OpeningBatches.Create(db, session, time, rows, outcome.Results);
        await db.SaveChangesAsync(ct);
        return OpeningBatches.ToPreview(batch.Id, outcome.Results);
    }
}

/// <summary>
///     Hep ya da hiç: tek işlemde her satırı yeniden doğrular ve yazar; herhangi bir satır önizlemeden farklı sonuç verirse
///     işlem geri alınır ve taze önizleme döner (409 import_conflict). Eşzamanlı ikinci uygulama, işlemin ilk işi olan
///     koşullu UPDATE (CommittedAt IS NULL) ile engellenir.
/// </summary>
[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record CommitOpeningImportCommand(Guid BatchId) : IRequest<OpeningCommitOutcome>;

public sealed class CommitOpeningImportCommandHandler(
    IApplicationDbContext db,
    OpeningStockEngine    engine,
    ICurrentSession       session,
    TimeProvider          time
) : IRequestHandler<CommitOpeningImportCommand, OpeningCommitOutcome> {
    public async Task<OpeningCommitOutcome> Handle(CommitOpeningImportCommand request, CancellationToken ct) {
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BatchId && b.Kind == OpeningSheet.Kind, ct)
                    ?? throw new NotFoundException(request.BatchId.ToString(), "ImportBatch");
        if (batch.CommittedAt is not null) throw OpeningImportErrors.AlreadyCommitted();
        if (batch.ExpiresAt <= time.GetUtcNow())
            throw new AppException(410, "import_expired", "Önizlemenin süresi doldu (30 dakika). Dosyayı yeniden yükleyip önizleyin.");

        var stored = OpeningBatches.Read(batch);
        if (stored.Actions.Contains("error"))
            throw new AppException(400, "import_has_errors", "Önizlemede hatalı satırlar var. Dosyayı düzeltip yeniden yükleyin; hiçbir şey yazılmadı.");

        OpeningStockEngine.ApplyOutcome outcome;
        await using (var transaction = await db.BeginTransactionAsync(ct)) {
            var now = time.GetUtcNow();
            var claimed = await db.ImportBatches.Where(b => b.Id == request.BatchId && b.CommittedAt == null && b.ExpiresAt > now)
                                  .ExecuteUpdateAsync(u => u.SetProperty(b => b.CommittedAt, now), ct);
            if (claimed == 0) throw OpeningImportErrors.AlreadyCommitted();

            outcome = await engine.ApplyAsync(stored.Rows, ct);
            if (outcome.Results.Select(r => r.Action).SequenceEqual(stored.Actions)) {
                await transaction.CommitAsync(ct);
                return new OpeningCommitOutcome(new OpeningCommitResult(outcome.Documents, outcome.Lines), null);
            }

            await transaction.RollbackAsync(ct);
        }

        db.ClearTracking();
        var fresh = OpeningBatches.Create(db, session, time, stored.Rows, outcome.Results);
        await db.SaveChangesAsync(ct);
        return new OpeningCommitOutcome(null, OpeningBatches.ToPreview(fresh.Id, outcome.Results));
    }
}

[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record OpeningTemplateQuery : IRequest<byte[]>;

public sealed class OpeningTemplateQueryHandler(IXlsxService xlsx) : IRequestHandler<OpeningTemplateQuery, byte[]> {
    public Task<byte[]> Handle(OpeningTemplateQuery request, CancellationToken ct) {
        return Task.FromResult(xlsx.Write(OpeningSheet.SheetName, OpeningSheet.Headers, []));
    }
}
