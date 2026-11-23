using System.Text.Json;
using ERP.Application.Catalog.Categories;
using ERP.Application.Catalog.Products;
using ERP.Application.Catalog.Products.Commands;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog.Excel;

/// <summary>
///     Satırları güncel veritabanına karşı uygular. Önizleme ve uygulama aynı kodu kullanır: çağıran işlemi açar,
///     önizlemede her zaman geri alır; uygulamada yalnızca hiçbir satır hata vermediyse ve her satır önizlemedeki
///     eylemle aynı çıktıysa onaylar. Böylece doğrulama ile yazma arasında fark olamaz.
/// </summary>
public sealed class ImportEngine(
    IApplicationDbContext db,
    ProductWriter         writer,
    ITenantSettings       settingsService
) {
    private sealed class Context {
        public required List<Category>   Categories { get; init; }
        public required List<Brand>      Brands     { get; init; }
        public required HashSet<string>  SeenCodes  { get; init; }
        public required bool             AutoCode   { get; init; }
    }

    /// <summary>Açık bir işlem içinde çağrılmalıdır. Hata veren satır diğerlerini durdurmaz; sonuçlar sırayla döner.</summary>
    public async Task<IReadOnlyList<ImportRowResult>> ApplyAsync(IReadOnlyList<ImportRow> rows,
                                                                 bool                     autoCode,
                                                                 CancellationToken        ct
    ) {
        var settings = await settingsService.GetAsync<CatalogSettings>(ct);
        var context = new Context {
            Categories = await db.Categories.AsNoTracking().ToListAsync(ct),
            Brands     = await db.Brands.AsNoTracking().ToListAsync(ct),
            SeenCodes  = new HashSet<string>(StringComparer.Ordinal),
            AutoCode   = autoCode
        };

        var results = new List<ImportRowResult>(rows.Count);
        foreach (var row in rows) {
            var result = await ApplyRowAsync(row, context, settings, ct);
            if (result.Action == "error") db.ClearTracking(); // yarım kalmış izlenen değişiklikler sonraki satıra taşınmasın
            results.Add(result);
        }

        return results;
    }

    private async Task<ImportRowResult> ApplyRowAsync(ImportRow row, Context ctx, CatalogSettings settings, CancellationToken ct) {
        var errors = new List<string>();
        var name   = row.Name?.Trim();
        var code   = string.IsNullOrWhiteSpace(row.Code) ? null : row.Code.Trim();

        ImportRowResult Fail() {
            return new ImportRowResult(row.RowNumber, "error", code, name, errors);
        }

        if (code is null) {
            if (!ctx.AutoCode) errors.Add("Kod boş olamaz. Kodları otomatik vermek için içe aktarmada \"Kod otomatik\" seçeneğini açın.");
        } else if (!ctx.SeenCodes.Add(code)) {
            errors.Add($"\"{code}\" kodu dosyada birden fazla satırda geçiyor.");
        }

        if (string.IsNullOrWhiteSpace(name)) errors.Add("Ad boş olamaz.");

        var existing = code is null
                           ? null
                           : await db.Products.Include(p => p.Variants).Include(p => p.Units).Include(p => p.Suppliers)
                                 .Include(p => p.Images).FirstOrDefaultAsync(p => p.Code == code, ct);

        // Dosyadaki değerleri çözümle (veritabanına dokunmadan).
        var kind = existing?.Kind ?? ProductKind.Product;
        if (!string.IsNullOrWhiteSpace(row.Kind)) {
            switch (TurkishText.Fold(row.Kind)) {
                case "ürün" or "urun" or "product":
                    kind = ProductKind.Product;
                    break;
                case "hizmet" or "service":
                    kind = ProductKind.Service;
                    break;
                default:
                    errors.Add("Tür \"Ürün\" veya \"Hizmet\" olmalıdır.");
                    break;
            }
        }

        int? vat = existing?.VatRate;
        if (!string.IsNullOrWhiteSpace(row.Vat)) {
            if (ProductSheet.TryDecimal(row.Vat, out var parsed) && parsed == decimal.Truncate(parsed)
                                                              && parsed is >= 0 and <= 1000)
                vat = (int)parsed;
            else
                errors.Add("KDV oranı sayı olmalıdır (0, 1, 10 veya 20).");
        } else if (row.Vat is not null && existing is null) {
            vat = null; // boş: ayarlardaki varsayılan KDV
        }

        var purchase = ResolveMoney(row.PurchaseAmount, row.PurchaseCurrency, row.PurchaseVatIncluded,
                                    existing?.DefaultPurchasePrice, "Alış", errors);
        var sale = ResolveMoney(row.SaleAmount, row.SaleCurrency, row.SaleVatIncluded, existing?.DefaultSalePrice,
                                "Satış", errors);

        var active = existing?.IsActive ?? true;
        if (!string.IsNullOrWhiteSpace(row.Passive)) {
            if (ProductSheet.TryFlag(row.Passive, out var passive)) active = !passive;
            else errors.Add("Pasif \"Evet\" veya \"Hayır\" olmalıdır.");
        }

        var tags = row.Tags is null
                       ? existing?.Tags.ToList() ?? []
                       : row.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var categoryPath = row.Category?.Trim();
        var brandName    = row.Brand?.Trim();
        var categorySegments = string.IsNullOrEmpty(categoryPath)
                                   ? null
                                   : categoryPath.Split('>', StringSplitOptions.TrimEntries);
        if (categorySegments is not null) {
            if (categorySegments.Length > CategoryErrors.MaxDepth)
                errors.Add("Kategori yolu en fazla 5 seviye olabilir.");
            else if (categorySegments.Any(s => s.Length is < 1 or > 100))
                errors.Add("Kategori adları 1 ile 100 karakter arasında olmalıdır (örn. \"Giyim > Erkek\").");
        }

        if (brandName is { Length: > 100 }) errors.Add("Marka adı en fazla 100 karakter olabilir.");
        if (errors.Count > 0) return Fail();

        try {
            // Kategori ve marka eksikse oluşturulur (işlem geri alınırsa birlikte gider).
            Guid? categoryId = categorySegments is null ? null : await ResolveCategoryAsync(categorySegments, ctx, ct);
            var brandId = string.IsNullOrEmpty(brandName)
                              ? (row.Brand is null ? existing?.BrandId : (Guid?)null)
                              : await ResolveBrandAsync(brandName, ctx, ct);
            if (categorySegments is null && row.Category is null) categoryId = existing?.CategoryId;

            string? barcode = row.Barcode?.Trim();
            if (row.Barcode is null && existing is not null)
                barcode = await db.Barcodes.AsNoTracking()
                              .Where(b => b.ProductId == existing.Id && b.Owner == BarcodeOwner.Product)
                              .Select(b => b.Value).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(barcode)) barcode = null;

            var input = existing is null ? new CreateProductCommand() : FromExisting(existing);
            input = input with {
                                   Kind                 = kind,
                                   Code                 = code,
                                   Name                 = name!,
                                   CategoryId           = categoryId,
                                   BrandId              = brandId,
                                   BaseUnit             = string.IsNullOrWhiteSpace(row.Unit) ? existing?.BaseUnit : row.Unit.Trim(),
                                   VatRate              = vat,
                                   Barcode              = barcode,
                                   DefaultPurchasePrice = purchase,
                                   DefaultSalePrice     = sale,
                                   Tags                 = tags
                               };

            var id = await writer.SaveAsync(existing, input, ct, true);

            var isNew = existing is null;
            var card  = existing ?? await db.Products.FirstAsync(p => p.Id == id, ct);
            if (card.IsActive != active) {
                card.IsActive = active;
                await db.SaveChangesAsync(ct);
            }

            return new ImportRowResult(row.RowNumber, isNew ? "create" : "update", card.Code, card.Name, []);
        } catch (Common.Exceptions.ValidationException ex) {
            errors.AddRange(ex.Errors.Values.SelectMany(v => v));
        } catch (AppException ex) {
            errors.Add(ex.Message);
        }

        return Fail();
    }

    private static Money4? ResolveMoney(string?       amount,
                                        string?       currency,
                                        string?       vatIncluded,
                                        Money4?       existing,
                                        string        label,
                                        List<string>  errors
    ) {
        if (amount is null) return existing;
        if (amount.Length == 0) return null;

        if (!ProductSheet.TryDecimal(amount, out var value)) {
            errors.Add($"{label} fiyatı sayı olmalıdır.");
            return null;
        }

        var cur = currency is null ? existing?.Currency ?? "TRY" : currency.Length == 0 ? "TRY" : currency.ToUpperInvariant();
        var vat = existing?.VatIncluded ?? false;
        if (vatIncluded is not null) {
            if (vatIncluded.Length == 0) vat = false;
            else if (ProductSheet.TryFlag(vatIncluded, out var flag)) vat = flag;
            else errors.Add($"{label} KDV dahil \"Evet\" veya \"Hayır\" olmalıdır.");
        }

        return new Money4(value, cur, vat);
    }

    /// <summary>Tabloda olmayan alanlar (birimler, tedarikçiler, görseller, özel alanlar, ölçüler) güncellemede korunur.</summary>
    private static CreateProductCommand FromExisting(Product p) {
        return new CreateProductCommand {
            Kind                 = p.Kind,
            Code                 = p.Code,
            Name                 = p.Name,
            Description          = p.Description,
            CategoryId           = p.CategoryId,
            BrandId              = p.BrandId,
            BaseUnit             = p.BaseUnit,
            VatRate              = p.VatRate,
            SoldByMeasure        = p.SoldByMeasure,
            MeasureUnit          = p.MeasureUnit,
            TracksSerialLot      = p.TracksSerialLot,
            WeightKg             = p.WeightKg,
            WidthCm              = p.WidthCm,
            LengthCm             = p.LengthCm,
            HeightCm             = p.HeightCm,
            OriginCountry        = p.OriginCountry,
            Gtip                 = p.Gtip,
            DefaultPurchasePrice = p.DefaultPurchasePrice,
            DefaultSalePrice     = p.DefaultSalePrice,
            Tags                 = p.Tags.ToList(),
            CustomFields         = JsonDocument.Parse(p.CustomFieldsJson).RootElement.Clone(),
            Units                = p.Units.Select(u => new UnitInput(u.Unit, u.Factor, u.Barcode)).ToList(),
            Suppliers = p.Suppliers.Select(s => new SupplierInput(s.CurrentAccountId, s.SupplierCode, s.IsDefault))
                .ToList(),
            Images = p.Images.OrderBy(i => i.Sort).Select(i => new ImageInput(i.FileId)).ToList()
        };
    }

    private async Task<Guid> ResolveCategoryAsync(string[] segments, Context ctx, CancellationToken ct) {
        Guid? parent = null;
        foreach (var segment in segments) {
            var folded = TurkishText.Fold(segment);
            var found = ctx.Categories.FirstOrDefault(c => c.ParentId == parent && TurkishText.Fold(c.Name) == folded);
            if (found is null) {
                var sort = ctx.Categories.Where(c => c.ParentId == parent).Select(c => c.Sort).DefaultIfEmpty(-1).Max() + 1;
                found = new Category { Name = segment, ParentId = parent, Sort = sort };
                db.Categories.Add(found);
                await db.SaveChangesAsync(ct);
                ctx.Categories.Add(found);
            }

            parent = found.Id;
        }

        return parent!.Value;
    }

    private async Task<Guid> ResolveBrandAsync(string name, Context ctx, CancellationToken ct) {
        var folded = TurkishText.Fold(name);
        var found  = ctx.Brands.FirstOrDefault(b => TurkishText.Fold(b.Name) == folded);
        if (found is null) {
            found = new Brand { Name = name };
            db.Brands.Add(found);
            await db.SaveChangesAsync(ct);
            ctx.Brands.Add(found);
        }

        return found.Id;
    }
}
