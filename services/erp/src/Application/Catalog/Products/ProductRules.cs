using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ERP.Application.Catalog.CustomFields;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Files.Commands;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using FluentValidation.Results;

namespace ERP.Application.Catalog.Products;

public static class CatalogErrors {
    public static AppException CodeTaken(string code) {
        return new AppException(409, "code_taken", $"\"{code}\" kodu bu firmada zaten kullanılıyor.");
    }

    public static AppException ProductInUse() {
        return new AppException(409, "product_in_use",
                                "Kullanımda olan kart silinemez. Bunun yerine pasifleştirebilirsiniz.");
    }

    public static AppException InvalidConversion(string message) {
        return new AppException(400, "invalid_conversion", message);
    }

    public static AppException TooManyVariants() {
        return new AppException(409, "too_many_variants", "Bir üründe en fazla 500 varyant üretilebilir.");
    }

    /// <summary>Kayıt sırasında benzersiz indeks çakışması (yarış durumu) ise ilgili 409 hatasını verir.</summary>
    public static AppException? FromUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        if (inner is null || inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string != "23505") return null;

        var constraint = inner.GetType().GetProperty("ConstraintName")?.GetValue(inner) as string ?? string.Empty;
        if (constraint.Contains("barcodes", StringComparison.OrdinalIgnoreCase))
            return new AppException(409, "barcode_taken", "Barkod bu firmada zaten kullanılıyor.");

        return constraint.Contains("Code", StringComparison.Ordinal)
                   ? new AppException(409, "code_taken", "Kod bu firmada zaten kullanılıyor.")
                   : null;
    }
}

/// <summary>Ürün kodu şablonu (örn. U{00000}) ve sıradaki serbest kod.</summary>
public static partial class ProductCodes {
    [GeneratedRegex(@"^(?<p>[^{}\s]*)\{(?<z>0+)\}(?<s>[^{}\s]*)$")]
    private static partial Regex TemplateRegex();

    public static string Format(string template, int sequence) {
        var m = TemplateRegex().Match(template);
        return m.Groups["p"].Value + sequence.ToString("D" + m.Groups["z"].Length) + m.Groups["s"].Value;
    }

    public static int? ParseSequence(string template, string code) {
        var m = TemplateRegex().Match(template);
        if (!m.Success) return null;

        var prefix = m.Groups["p"].Value;
        var suffix = m.Groups["s"].Value;
        if (code.Length <= prefix.Length + suffix.Length || !code.StartsWith(prefix, StringComparison.Ordinal)
                                                         || !code.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var digits = code[prefix.Length..^suffix.Length];
        return digits.All(char.IsAsciiDigit) && int.TryParse(digits, out var n) ? n : null;
    }

    /// <summary>Sayaçtan başlayarak şablona uyan ve ürün/varyant kodlarında kullanılmayan ilk sıra.</summary>
    public static async Task<int> NextFreeSequenceAsync(IApplicationDbContext db,
                                                        CatalogSettings       settings,
                                                        CancellationToken     ct
    ) {
        var m      = TemplateRegex().Match(settings.CodeTemplate);
        var prefix = m.Groups["p"].Value;
        var suffix = m.Groups["s"].Value;
        var codes = await db.Products.AsNoTracking().Where(p => p.Code.StartsWith(prefix) && p.Code.EndsWith(suffix))
                        .Select(p => p.Code).ToListAsync(ct);
        var variantCodes = await db.ProductVariants.AsNoTracking()
                               .Where(v => v.Code.StartsWith(prefix) && v.Code.EndsWith(suffix))
                               .Select(v => v.Code).ToListAsync(ct);
        var used = codes.Concat(variantCodes).Select(c => ParseSequence(settings.CodeTemplate, c))
            .Where(n => n is not null).Select(n => n!.Value).ToHashSet();

        var next = Math.Max(1, settings.NextCodeSequence);
        while (used.Contains(next)) next++;
        return next;
    }

    public static string SeedJson(string field, int next) {
        var node = JsonSerializer.SerializeToNode(new CatalogSettings(), SettingsRegistry.Json)!.AsObject();
        node[field] = next;
        return node.ToJsonString();
    }
}

/// <summary>Ürün/hizmet kartını doğrular ve yazar; oluşturma ve güncelleme ortak kullanır.</summary>
public sealed class ProductWriter(
    IApplicationDbContext db,
    ICurrentSession       session,
    ITenantSettings       settingsService,
    BarcodeRegistry       barcodes,
    ItemUsage             usage
) {
    private const decimal MaxMoney = 99_999_999_999_999.9999m;
    private const decimal MaxDim   = 999_999_999m;

    public static readonly string[] MeasureUnits = ["m2", "m"];

    /// <summary>
    ///     existing null ise yeni kart oluşturulur. Dönen değer kartın Id'sidir. joinOpenTransaction: çağıranın açtığı işleme
    ///     katılır (kendi işlemini açmaz/onaylamaz, benzersiz indeks hatalarını da olduğu gibi yukarı bırakır).
    ///     import: Excel içe aktarma bağlamı; verilirse ayarlar, tanımlar, kodlar ve barkodlar satır başına sorgulanmaz.
    ///     Boş kod güncellemede mevcut kodu korur; yeni kartta sıradaki kod verilir. Zorunlu özel alanlar kart
    ///     oluşturulurken tümüyle, güncellemede yalnızca istekte gönderilen alanlar için denetlenir; içe aktarmada denetlenmez.
    /// </summary>
    public async Task<Guid> SaveAsync(Product?          existing,
                                      ProductInput      input,
                                      CancellationToken ct,
                                      bool              joinOpenTransaction = false,
                                      ImportContext?    import              = null) {
        var isNew = existing is null;
        if (input.TracksSerialLot && !session.HasModule("seri-lot"))
            throw new AppException(403, "module_disabled", "Seri/lot takibi için Seri-Lot modülü firmanızda kapalı.");

        var settings = import?.Settings ?? await settingsService.GetAsync<CatalogSettings>(ct);
        var f        = new List<ValidationFailure>();

        void Fail(string field, string message) {
            f.Add(new ValidationFailure(field, message));
        }

        var kind = input.Kind;
        if (!Enum.IsDefined(kind)) Fail("Kind", "Kart türü geçersiz.");
        if (existing is not null && existing.Kind != kind) Fail("Kind", "Kart türü sonradan değiştirilemez.");

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 200) Fail("Name", "Ad 1 ile 200 karakter arasında olmalıdır.");

        // Boş kod güncellemede mevcut kodu korur (sessizce yeni kod verilmez).
        var code = string.IsNullOrWhiteSpace(input.Code) ? existing?.Code : input.Code.Trim();
        if (code is { Length: > 30 }) Fail("Code", "Kod 1 ile 30 karakter arasında olmalıdır.");

        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (description is { Length: > 4000 }) Fail("Description", "Açıklama en fazla 4000 karakter olabilir.");

        var origin = string.IsNullOrWhiteSpace(input.OriginCountry) ? null : input.OriginCountry.Trim();
        if (origin is { Length: > 100 }) Fail("OriginCountry", "Menşei en fazla 100 karakter olabilir.");
        var gtip = string.IsNullOrWhiteSpace(input.Gtip) ? null : input.Gtip.Trim();
        if (gtip is { Length: > 20 }) Fail("Gtip", "GTİP en fazla 20 karakter olabilir.");

        var baseUnit = string.IsNullOrWhiteSpace(input.BaseUnit)
                           ? kind == ProductKind.Service ? settings.Units[0] : settings.Units.Contains("adet") ? "adet" : settings.Units[0]
                           : input.BaseUnit.Trim().ToLowerInvariant();
        if (!settings.Units.Contains(baseUnit)) Fail("BaseUnit", "Temel birim ayarlardaki birim listesinde olmalıdır.");

        var vat = input.VatRate ?? settings.DefaultVatRate;
        if (!CatalogSettings.VatRates.Contains(vat)) Fail("VatRate", "KDV oranı 0, 1, 10 veya 20 olmalıdır.");

        var attributesInput = isNew ? input.Attributes ?? [] : [];
        var variantsInput   = isNew ? input.Variants ?? [] : [];

        if (kind == ProductKind.Service) {
            if (input.SoldByMeasure) Fail("SoldByMeasure", "Hizmet ölçüyle satılamaz.");
            if (input.TracksSerialLot) Fail("TracksSerialLot", "Hizmette seri/lot takibi olmaz.");
            if (attributesInput.Count > 0 || variantsInput.Count > 0) Fail("Variants", "Hizmet kartında varyant olamaz.");
        }

        string? measureUnit = null;
        if (input.SoldByMeasure) {
            measureUnit = input.MeasureUnit?.Trim().ToLowerInvariant();
            if (measureUnit is null || !MeasureUnits.Contains(measureUnit))
                Fail("MeasureUnit", "Ölçü birimi m2 veya m olmalıdır.");
            else if (baseUnit != measureUnit)
                Fail("BaseUnit", "Ölçüyle satılan üründe temel birim ölçü birimiyle aynı olmalıdır.");
        }

        if (existing is { SoldByMeasure: true } && !input.SoldByMeasure && await usage.ProductInUseAsync(existing.Id, ct))
            throw new AppException(409, "measure_in_use",
                                   "Kullanımdaki ürünün ölçüyle satış ayarı kapatılamaz.");

        var barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();
        if (barcode is { Length: > 40 }) Fail("Barcode", "Barkod en fazla 40 karakter olabilir.");

        foreach (var (field, value) in new (string, decimal?)[] {
                     ("WeightKg", input.WeightKg), ("WidthCm", input.WidthCm), ("LengthCm", input.LengthCm),
                     ("HeightCm", input.HeightCm)
                 })
            if (value is < 0 or > MaxDim || value is { } v && decimal.Round(v, 4) != v)
                Fail(field, "Ölçü 0 ile 999.999.999 arasında, en fazla 4 ondalıkla girilmelidir.");

        CheckMoney(input.DefaultPurchasePrice, "DefaultPurchasePrice", Fail);
        CheckMoney(input.DefaultSalePrice,     "DefaultSalePrice",     Fail);

        var tags = (input.Tags ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (tags.Count > 20 || tags.Any(t => t.Length > 30)) Fail("Tags", "En fazla 20 etiket, her biri en fazla 30 karakter olabilir.");

        // İçe aktarma kategori ve markayı kendisi çözer/oluşturur; kimlikler zaten geçerlidir.
        if (import is null) {
            if (input.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
                Fail("CategoryId", "Kategori bulunamadı.");
            if (input.BrandId is { } brandId && !await db.Brands.AnyAsync(b => b.Id == brandId, ct))
                Fail("BrandId", "Marka bulunamadı.");
        }

        // Birimler
        var units = new List<UnitInput>();
        foreach (var u in input.Units ?? []) {
            var unit = (u.Unit ?? string.Empty).Trim().ToLowerInvariant();
            if (u.Factor <= 0 || decimal.Round(u.Factor, 6) != u.Factor)
                throw CatalogErrors.InvalidConversion("Birim çevrim katsayısı 0'dan büyük olmalı ve en fazla 6 ondalık içermelidir.");
            if (!settings.Units.Contains(unit)) Fail("Units", $"\"{unit}\" birimi ayarlardaki listede yok.");
            else if (unit == baseUnit) Fail("Units", "Temel birim için çevrim eklenemez.");
            else if (units.Any(x => x.Unit == unit)) Fail("Units", $"\"{unit}\" birimi birden fazla kez eklenmiş.");

            var unitBarcode = string.IsNullOrWhiteSpace(u.Barcode) ? null : u.Barcode.Trim();
            if (unitBarcode is { Length: > 40 }) Fail("Units", "Birim barkodu en fazla 40 karakter olabilir.");
            units.Add(new UnitInput(unit, u.Factor, unitBarcode));
        }

        // Tedarikçiler
        var suppliers = (input.Suppliers ?? []).ToList();
        if (suppliers.Select(s => s.CurrentAccountId).Distinct().Count() != suppliers.Count)
            Fail("Suppliers", "Aynı tedarikçi birden fazla kez eklenmiş.");
        if (suppliers.Count(s => s.IsDefault) > 1) Fail("Suppliers", "En fazla bir varsayılan tedarikçi olabilir.");
        if (suppliers.Any(s => s.SupplierCode is { Length: > 60 })) Fail("Suppliers", "Tedarikçi kodu en fazla 60 karakter olabilir.");
        Dictionary<Guid, string> supplierNames = new();
        if (suppliers.Count > 0 && import is null) {
            var ids  = suppliers.Select(s => s.CurrentAccountId).ToList();
            var rows = await db.CurrentAccounts.AsNoTracking().Where(a => ids.Contains(a.Id))
                           .Select(a => new { a.Id, a.IsSupplier, a.Name }).ToListAsync(ct);
            foreach (var s in suppliers) {
                var row = rows.FirstOrDefault(r => r.Id == s.CurrentAccountId);
                if (row is null || !row.IsSupplier) Fail("Suppliers", "Tedarikçi olarak işaretli bir cari seçilmelidir.");
            }
        }

        // Görseller
        var images = (input.Images ?? []).Select(i => i.FileId).Distinct().ToList();
        if (images.Count > 10) Fail("Images", "En fazla 10 görsel eklenebilir.");
        else if (images.Count > 0 && import is null
                 && await db.StoredFiles.CountAsync(x => images.Contains(x.Id) && x.Kind == FileKinds.Image, ct)
                 != images.Count)
            Fail("Images", "Görsel dosyası bulunamadı. Önce görseli yükleyin.");

        // Özel alanlar
        IReadOnlyList<CustomFieldDefinition> definitions
            = import?.Definitions ?? (IReadOnlyList<CustomFieldDefinition>)await db.CustomFieldDefinitions.AsNoTracking().ToListAsync(ct);
        var requiredMode = import is not null ? RequiredMode.None : isNew ? RequiredMode.All : RequiredMode.PresentOnly;
        var customJson   = CustomFieldRules.Normalize(input.CustomFields, definitions, kind, f, requiredMode);

        // Varyant özellikleri (yalnızca oluştururken)
        var attributes = new List<VariantAttribute>();
        foreach (var (a, index) in attributesInput.Select((a, i) => (a, i))) {
            var attrName = (a.Name ?? string.Empty).Trim();
            var values   = (a.Values ?? []).Select(v => v?.Trim() ?? string.Empty).ToList();
            if (attrName.Length is < 1 or > 50) Fail("Attributes", "Özellik adı 1 ile 50 karakter arasında olmalıdır.");
            if (attributes.Any(x => string.Equals(x.Name, attrName, StringComparison.CurrentCultureIgnoreCase)))
                Fail("Attributes", $"\"{attrName}\" özelliği birden fazla kez eklenmiş.");
            if (values.Count is < 1 or > 50) Fail("Attributes", $"\"{attrName}\" için 1 ile 50 değer girilmelidir.");
            if (values.Any(v => v.Length is < 1 or > 50)) Fail("Attributes", "Özellik değeri 1 ile 50 karakter arasında olmalıdır.");
            if (values.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != values.Count)
                Fail("Attributes", $"\"{attrName}\" değerleri benzersiz olmalıdır.");
            attributes.Add(new VariantAttribute { Name = attrName, Values = values, Sort = index });
        }

        if (attributes.Count > 3) Fail("Attributes", "En fazla 3 özellik tanımlanabilir.");
        if (variantsInput.Count > 0 && attributes.Count == 0)
            Fail("Variants", "Varyant eklemek için önce özellikleri tanımlayın.");

        if (f.Count > 0) throw new Common.Exceptions.ValidationException(f);

        var combinations = VariantGenerator.Combinations(attributes);
        if (variantsInput.Count > 500 || combinations.Count > 500) throw CatalogErrors.TooManyVariants();

        // Kod ve barkod tekilliği, sayaçlar ve kayıt tek işlemde
        await using var transaction = joinOpenTransaction ? null : await db.BeginTransactionAsync(ct);

        int? explicitSequence = null;
        if (code is null) {
            if (import is not null) {
                code = ProductCodes.Format(settings.CodeTemplate, import.TakeAutoSequence());
            } else {
                var free = await ProductCodes.NextFreeSequenceAsync(db, settings, ct);
                var seq  = await db.ReserveCatalogSequenceAsync("nextCodeSequence", free,
                                                                ProductCodes.SeedJson("nextCodeSequence", free + 1), ct);
                code = ProductCodes.Format(settings.CodeTemplate, seq);
            }
        } else if (isNew && ProductCodes.ParseSequence(settings.CodeTemplate, code) is { } given
                   && given >= settings.NextCodeSequence) {
            if (import is null)
                await db.ReserveCatalogSequenceAsync("nextCodeSequence", given,
                                                     ProductCodes.SeedJson("nextCodeSequence", given + 1), ct);
            else
                explicitSequence = given;
        }

        var product = existing ?? new Product();

        if (import is null
                ? await db.Products.AnyAsync(p => p.Code == code && p.Id != product.Id, ct)
                  || await db.ProductVariants.AnyAsync(v => v.Code == code, ct)
                : (existing is null || existing.Code != code) && import.UsedCodes.Contains(code))
            throw CatalogErrors.CodeTaken(code);

        if (explicitSequence is { } noted) import?.NoteExplicitSequence(noted);

        product.Kind                 = kind;
        product.Code                 = code;
        product.Name                 = name;
        product.Description          = description;
        product.CategoryId           = input.CategoryId;
        product.BrandId              = input.BrandId;
        product.BaseUnit             = baseUnit;
        product.VatRate              = vat;
        product.SoldByMeasure        = input.SoldByMeasure;
        product.MeasureUnit          = measureUnit;
        product.TracksSerialLot      = input.TracksSerialLot;
        product.WeightKg             = input.WeightKg;
        product.WidthCm              = input.WidthCm;
        product.LengthCm             = input.LengthCm;
        product.HeightCm             = input.HeightCm;
        product.OriginCountry        = origin;
        product.Gtip                 = gtip;
        product.DefaultPurchasePrice = input.DefaultPurchasePrice;
        product.DefaultSalePrice     = input.DefaultSalePrice;
        product.Tags                 = tags;
        product.CustomFieldsJson     = customJson;

        // Varyantlar yalnızca oluştururken (sonrası /attributes ve /variants uçlarında).
        if (isNew) {
            product.Attributes = attributes;
            var number = 0;
            if (variantsInput.Count > 0) {
                foreach (var v in variantsInput) {
                    number++;
                    var variantValues = v.Values ?? new Dictionary<string, string>();
                    if (!MatchesAttributes(variantValues, attributes))
                        throw new Common.Exceptions.ValidationException([
                            new ValidationFailure("Variants", "Varyant değerleri tanımlı özelliklerle uyuşmuyor.")
                        ]);

                    var variantBarcode = string.IsNullOrWhiteSpace(v.Barcode) ? null : v.Barcode.Trim();
                    if (variantBarcode is { Length: > 40 })
                        throw new Common.Exceptions.ValidationException([
                            new ValidationFailure("Variants", "Barkod en fazla 40 karakter olabilir.")
                        ]);

                    product.Variants.Add(NewVariant(product, v.Code, $"{code}-{number}", variantBarcode, variantValues,
                                                    v));
                }
            } else {
                foreach (var combo in combinations) {
                    number++;
                    product.Variants.Add(NewVariant(product, null, $"{code}-{number}", null,
                                                    new Dictionary<string, string>(combo), null));
                }
            }

            var variantCodes = product.Variants.Select(v => v.Code).ToList();
            if (variantCodes.Append(code).Distinct(StringComparer.Ordinal).Count() != variantCodes.Count + 1)
                throw CatalogErrors.CodeTaken(variantCodes.GroupBy(c => c).FirstOrDefault(g => g.Count() > 1)?.Key ?? code);
            var clash = import is not null
                            ? variantCodes.FirstOrDefault(import.UsedCodes.Contains)
                            : await db.Products.Where(p => variantCodes.Contains(p.Code)).Select(p => p.Code)
                                  .Concat(db.ProductVariants.Where(v => variantCodes.Contains(v.Code)).Select(v => v.Code))
                                  .FirstOrDefaultAsync(ct);
            if (clash is not null) throw CatalogErrors.CodeTaken(clash);
        }

        SyncUnits(product, units);
        SyncSuppliers(product, suppliers);
        SyncImages(product, images);

        var allBarcodes = new List<string>();
        if (barcode is not null) allBarcodes.Add(barcode);
        allBarcodes.AddRange(product.Variants.Where(v => v.Barcode is not null).Select(v => v.Barcode!));
        allBarcodes.AddRange(product.Units.Where(u => u.Barcode is not null).Select(u => u.Barcode!));
        if (import is null) await barcodes.EnsureFreeAsync(allBarcodes, product.Id, ct);
        else BarcodeRegistry.EnsureFree(allBarcodes, product.Id, import);

        if (isNew) db.Products.Add(product);
        await barcodes.SyncAsync(product, barcode, ct, isNew);

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (!joinOpenTransaction && CatalogErrors.FromUniqueViolation(ex) is { } conflict) {
            throw conflict;
        }

        if (import is not null) {
            import.Register(product, allBarcodes, barcode);
            import.SavedCard = product;
        }

        if (transaction is not null) await transaction.CommitAsync(ct);
        return product.Id;
    }

    private static ProductVariant NewVariant(Product                     product,
                                             string?                     code,
                                             string                      fallbackCode,
                                             string?                     barcode,
                                             Dictionary<string, string>  values,
                                             VariantInput?               input
    ) {
        var variantCode = string.IsNullOrWhiteSpace(code) ? fallbackCode : code.Trim();
        if (variantCode.Length > 40)
            throw new Common.Exceptions.ValidationException([
                new ValidationFailure("Variants", "Varyant kodu 1 ile 40 karakter arasında olmalıdır.")
            ]);

        return new ProductVariant {
            ProductId = product.Id, Code = variantCode, Barcode = barcode, Values = values,
            SalePriceOverride = input?.SalePriceOverride, PurchasePriceOverride = input?.PurchasePriceOverride,
            IsActive = input?.IsActive ?? true
        };
    }

    private static bool MatchesAttributes(Dictionary<string, string> values, List<VariantAttribute> attributes) {
        if (values.Count != attributes.Count) return false;
        return attributes.All(a => values.TryGetValue(a.Name, out var v) && a.Values.Contains(v));
    }

    public static void CheckMoney(Money4? money, string field, Action<string, string> fail) {
        if (money is null) return;

        if (money.Amount is < 0 or > MaxMoney || decimal.Round(money.Amount, 4) != money.Amount)
            fail(field, "Tutar 0 veya daha büyük olmalı ve en fazla 4 ondalık içermelidir.");
        if (!Money4Currencies.Contains(money.Currency))
            fail(field, "Para birimi TRY, USD, EUR veya GBP olmalıdır.");
    }

    private static readonly string[] Money4Currencies = ["TRY", "USD", "EUR", "GBP"];

    private static void SyncUnits(Product product, List<UnitInput> wanted) {
        foreach (var old in product.Units.Where(u => wanted.All(w => w.Unit != u.Unit)).ToList())
            product.Units.Remove(old);

        foreach (var w in wanted) {
            var unit = product.Units.FirstOrDefault(u => u.Unit == w.Unit);
            if (unit is null) {
                unit = new ProductUnit { ProductId = product.Id, Unit = w.Unit };
                product.Units.Add(unit);
            }

            unit.Factor  = w.Factor;
            unit.Barcode = w.Barcode;
        }
    }

    private static void SyncSuppliers(Product product, List<SupplierInput> wanted) {
        foreach (var old in product.Suppliers.Where(s => wanted.All(w => w.CurrentAccountId != s.CurrentAccountId))
                     .ToList())
            product.Suppliers.Remove(old);

        foreach (var w in wanted) {
            var supplier = product.Suppliers.FirstOrDefault(s => s.CurrentAccountId == w.CurrentAccountId);
            if (supplier is null) {
                supplier = new ProductSupplier { ProductId = product.Id, CurrentAccountId = w.CurrentAccountId };
                product.Suppliers.Add(supplier);
            }

            supplier.SupplierCode = string.IsNullOrWhiteSpace(w.SupplierCode) ? null : w.SupplierCode.Trim();
            supplier.IsDefault    = w.IsDefault;
        }
    }

    private static void SyncImages(Product product, List<Guid> wanted) {
        foreach (var old in product.Images.Where(i => !wanted.Contains(i.FileId)).ToList()) product.Images.Remove(old);

        for (var index = 0; index < wanted.Count; index++) {
            var image = product.Images.FirstOrDefault(i => i.FileId == wanted[index]);
            if (image is null) {
                image = new ProductImage { ProductId = product.Id, FileId = wanted[index] };
                product.Images.Add(image);
            }

            image.Sort = index;
        }
    }
}
