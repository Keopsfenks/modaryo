using System.Text.RegularExpressions;
using ERP.Application.Catalog.Products;
using ERP.Application.Catalog.Products.Queries;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;
using FluentValidation.Results;

namespace ERP.Application.Catalog.Variants.Commands;

public sealed record AttributeValuesInput(string Name, List<string>? Values);

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record SetAttributesCommand(Guid ProductId, List<AttributeValuesInput>? Attributes) : IRequest<ProductDto>;

/// <summary>
///     Özellikleri değiştirir ve kombinasyonları yeniden üretir. Değer kümesi hâlâ var olan varyantlar (kimlik, kod,
///     barkod ve fiyatlarıyla) korunur, yeni kombinasyonlar eklenir. Artık var olmayan kombinasyon kullanımdaysa işlem
///     reddedilir (variant_in_use); kullanılmayan varyantlar silinir.
/// </summary>
public sealed partial class SetAttributesCommandHandler(
    IApplicationDbContext db,
    ItemUsage             usage,
    BarcodeRegistry       barcodes,
    ICurrentSession       session
) : IRequestHandler<SetAttributesCommand, ProductDto> {
    private const int MaxVariants = 500;

    [GeneratedRegex(@"-(\d+)$")]
    private static partial Regex SuffixRegex();

    public async Task<ProductDto> Handle(SetAttributesCommand request, CancellationToken ct) {
        var product = await db.Products.Include(p => p.Variants).Include(p => p.Units)
                          .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
                      ?? throw new NotFoundException(request.ProductId.ToString(), "Product");

        var failures = new List<ValidationFailure>();
        if (product.Kind == ProductKind.Service) failures.Add(new ValidationFailure("Attributes", "Hizmet kartında varyant olamaz."));

        var attributes = new List<VariantAttribute>();
        foreach (var (input, index) in (request.Attributes ?? []).Select((a, i) => (a, i))) {
            var name   = (input.Name ?? string.Empty).Trim();
            var values = (input.Values ?? []).Select(v => v?.Trim() ?? string.Empty).ToList();
            if (name.Length is < 1 or > 50) failures.Add(new ValidationFailure("Attributes", "Özellik adı 1 ile 50 karakter arasında olmalıdır."));
            if (attributes.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                failures.Add(new ValidationFailure("Attributes", $"\"{name}\" özelliği birden fazla kez eklenmiş."));
            if (values.Count is < 1 or > 50)
                failures.Add(new ValidationFailure("Attributes", $"\"{name}\" için 1 ile 50 değer girilmelidir."));
            if (values.Any(v => v.Length is < 1 or > 50))
                failures.Add(new ValidationFailure("Attributes", "Özellik değeri 1 ile 50 karakter arasında olmalıdır."));
            if (values.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != values.Count)
                failures.Add(new ValidationFailure("Attributes", $"\"{name}\" değerleri benzersiz olmalıdır."));

            // Aynı adlı özellik kimliğini korur.
            var existingId = product.Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.CurrentCultureIgnoreCase))?.Id;
            attributes.Add(new VariantAttribute { Id = existingId ?? Guid.CreateVersion7(), Name = name, Values = values, Sort = index });
        }

        if (attributes.Count > 3) failures.Add(new ValidationFailure("Attributes", "En fazla 3 özellik tanımlanabilir."));
        if (failures.Count > 0) throw new Common.Exceptions.ValidationException(failures);

        var combinations = VariantGenerator.Combinations(attributes);
        if (combinations.Count > MaxVariants) throw CatalogErrors.TooManyVariants();

        // Mevcut varyantları kombinasyonlarla eşleştir.
        var unmatched = product.Variants.ToList();
        var toCreate  = new List<IReadOnlyDictionary<string, string>>();
        foreach (var combo in combinations) {
            var match = unmatched.FirstOrDefault(v => Same(v.Values, combo));
            if (match is null) toCreate.Add(combo);
            else unmatched.Remove(match);
        }

        // Geriye kalanlar artık var olmayan kombinasyonlardır.
        foreach (var orphan in unmatched)
            if (await usage.VariantInUseAsync(orphan.Id, ct))
                throw new AppException(409, "variant_in_use",
                                       $"\"{orphan.Code}\" varyantı kullanımda; değeri kaldırılamaz. Varyantı pasifleştirebilirsiniz.");

        await using var transaction = await db.BeginTransactionAsync(ct);

        foreach (var orphan in unmatched) {
            product.Variants.Remove(orphan);
            db.ProductVariants.Remove(orphan);
        }

        if (toCreate.Count > 0) {
            var number = product.Variants.Where(v => v.Code.StartsWith(product.Code + "-", StringComparison.Ordinal))
                             .Select(v => SuffixRegex().Match(v.Code))
                             .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value))
                             .DefaultIfEmpty(0).Max();
            var taken = (await db.Products.Select(p => p.Code)
                             .Concat(db.ProductVariants.Select(v => v.Code)).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            foreach (var v in unmatched) taken.Remove(v.Code);

            foreach (var combo in toCreate) {
                string code;
                do {
                    number++;
                    code = $"{product.Code}-{number}";
                } while (taken.Contains(code));

                taken.Add(code);
                product.Variants.Add(new ProductVariant {
                                         ProductId = product.Id, Code = code, Values = new Dictionary<string, string>(combo)
                                     });
            }
        }

        product.Attributes = attributes;

        var productBarcode = await db.Barcodes.Where(b => b.ProductId == product.Id && b.Owner == BarcodeOwner.Product)
                                 .Select(b => b.Value).FirstOrDefaultAsync(ct);
        await barcodes.SyncAsync(product, productBarcode, ct);

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (CatalogErrors.FromUniqueViolation(ex) is { } conflict) {
            throw conflict;
        }

        await transaction.CommitAsync(ct);
        return await ProductReader.GetDtoAsync(db, usage, session, product.Id, ct);
    }

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) {
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);
    }
}
