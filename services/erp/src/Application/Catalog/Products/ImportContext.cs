using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Products;

/// <summary>
///     Excel içe aktarma için bir kez yüklenen ve satırlar ilerledikçe bellekte güncellenen arama verisi: ayarlar, özel alan
///     tanımları, kullanılan kod ve sıra numaraları, barkod sahipleri. ProductWriter bu bağlam verilince satır başına veritabanı
///     taraması yapmaz. Kod sayacı (tenant_settings) satırlar bittiğinde tek ifadeyle ilerletilir; böylece sayaç satırının
///     kilidi işlem boyunca değil yalnızca sonunda tutulur.
/// </summary>
public sealed class ImportContext {
    private int _cursor;

    private ImportContext(CatalogSettings settings, List<CustomFieldDefinition> definitions) {
        Settings    = settings;
        Definitions = definitions;
        _cursor     = Math.Max(1, settings.NextCodeSequence);
    }

    public CatalogSettings             Settings    { get; }
    public List<CustomFieldDefinition> Definitions { get; }

    /// <summary>Firmadaki tüm ürün ve varyant kodları.</summary>
    public HashSet<string> UsedCodes { get; } = new(StringComparer.Ordinal);

    /// <summary>Kod şablonuna uyan kodların sıra numaraları.</summary>
    public HashSet<int> UsedSequences { get; } = [];

    /// <summary>Barkod değeri → kartın kimliği.</summary>
    public Dictionary<string, Guid> BarcodeOwners { get; } = new(StringComparer.Ordinal);

    public Dictionary<Guid, HashSet<string>> BarcodesByProduct { get; } = [];

    /// <summary>Kartın kendi (ürün düzeyindeki) barkodu.</summary>
    public Dictionary<Guid, string> ProductBarcodes { get; } = [];

    /// <summary>Bu içe aktarmada verilen en büyük kod sırası; 0 ise sayaç ilerletilmez.</summary>
    public int MaxSequence { get; private set; }

    /// <summary>ProductWriter'ın son kaydettiği kart (yeni kartı yeniden sorgulamadan almak için).</summary>
    public Product? SavedCard { get; set; }

    public static async Task<ImportContext> LoadAsync(IApplicationDbContext db,
                                                      CatalogSettings       settings,
                                                      CancellationToken     ct
    ) {
        var definitions = await db.CustomFieldDefinitions.AsNoTracking().ToListAsync(ct);
        var context     = new ImportContext(settings, definitions);

        foreach (var code in await db.Products.AsNoTracking().Select(p => p.Code).ToListAsync(ct)) context.AddCode(code);
        foreach (var code in await db.ProductVariants.AsNoTracking().Select(v => v.Code).ToListAsync(ct)) context.AddCode(code);

        var rows = await db.Barcodes.AsNoTracking().Select(b => new { b.Value, b.ProductId, b.Owner }).ToListAsync(ct);
        foreach (var row in rows) {
            context.BarcodeOwners[row.Value] = row.ProductId;
            if (!context.BarcodesByProduct.TryGetValue(row.ProductId, out var set))
                context.BarcodesByProduct[row.ProductId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(row.Value);
            if (row.Owner == BarcodeOwner.Product) context.ProductBarcodes[row.ProductId] = row.Value;
        }

        return context;
    }

    private void AddCode(string code) {
        UsedCodes.Add(code);
        if (ProductCodes.ParseSequence(Settings.CodeTemplate, code) is { } n) UsedSequences.Add(n);
    }

    /// <summary>Sayaçtan başlayarak kullanılmayan ilk sırayı bellekte ayırır (veritabanı sayacına dokunmaz).</summary>
    public int TakeAutoSequence() {
        while (UsedSequences.Contains(_cursor)) _cursor++;
        var sequence = _cursor++;
        UsedSequences.Add(sequence);
        MaxSequence = Math.Max(MaxSequence, sequence);
        return sequence;
    }

    /// <summary>Şablona uyan, elle verilmiş yeni bir kodun sırası sayacı geçtiyse sayaç bu sıranın ötesine ilerler.</summary>
    public void NoteExplicitSequence(int sequence) {
        if (sequence >= Settings.NextCodeSequence) MaxSequence = Math.Max(MaxSequence, sequence);
    }

    /// <summary>Kaydedilen kartın kodlarını ve barkodlarını bellekteki kümelere işler.</summary>
    public void Register(Product product, IReadOnlyCollection<string> barcodes, string? productBarcode) {
        AddCode(product.Code);
        foreach (var variant in product.Variants) AddCode(variant.Code);

        if (BarcodesByProduct.Remove(product.Id, out var old))
            foreach (var value in old)
                BarcodeOwners.Remove(value);
        ProductBarcodes.Remove(product.Id);

        if (barcodes.Count > 0) BarcodesByProduct[product.Id] = new HashSet<string>(barcodes, StringComparer.Ordinal);
        foreach (var value in barcodes) BarcodeOwners[value] = product.Id;
        if (productBarcode is not null) ProductBarcodes[product.Id] = productBarcode;
    }
}
