using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;
using FluentValidation.Results;

namespace ERP.Application.Catalog.CustomFields;

public sealed record CustomFieldDto(
    Guid            Id,
    string          Key,
    string          Label,
    CustomFieldType Type,
    string[]        Options,
    bool            Required,
    bool            AppliesToProduct,
    bool            AppliesToService,
    int             Sort
);

public static partial class CustomFieldRules {
    public const int MaxDefinitions = 30;

    public static CustomFieldDto ToDto(CustomFieldDefinition d) {
        return new CustomFieldDto(d.Id, d.Key, d.Label, d.Type, d.Options, d.Required, d.AppliesToProduct,
                                  d.AppliesToService, d.Sort);
    }

    public static bool IsValidKey(string? key) {
        return key is not null && KeyRegex().IsMatch(key);
    }

    public static AppException KeyTaken() {
        return new AppException(409, "key_taken", "Bu anahtarla bir özel alan zaten tanımlı.");
    }

    public static bool IsUniqueViolation(DbUpdateException ex) {
        return ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string == "23505";
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,30}$")]
    private static partial Regex KeyRegex();

    /// <summary>
    ///     Ürün kartındaki özel alan değerlerini tanımlara göre doğrular ve normalleştirir. Karta uygulanmayan ya da
    ///     tanımı silinmiş anahtarlar sessizce atılır; boş (null) değer yok sayılır. Hatalar failures'a eklenir.
    /// </summary>
    public static string Normalize(JsonElement?                     input,
                                   IReadOnlyList<CustomFieldDefinition> definitions,
                                   ProductKind                      kind,
                                   List<ValidationFailure>          failures
    ) {
        var values = new Dictionary<string, JsonElement>();
        if (input is { ValueKind: JsonValueKind.Object } obj) {
            foreach (var property in obj.EnumerateObject())
                if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    values[property.Name] = property.Value;
        } else if (input is { } other && other.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) {
            failures.Add(new ValidationFailure("CustomFields", "Özel alanlar bir nesne olmalıdır."));
        }

        var result = new Dictionary<string, object>();
        foreach (var def in definitions.Where(d => kind == ProductKind.Product ? d.AppliesToProduct : d.AppliesToService)) {
            var field = $"CustomFields.{def.Key}";
            if (!values.TryGetValue(def.Key, out var raw)) {
                if (def.Required) failures.Add(new ValidationFailure(field, $"\"{def.Label}\" alanı zorunludur."));
                continue;
            }

            switch (def.Type) {
                case CustomFieldType.Text:
                    if (raw.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(raw.GetString())) {
                        failures.Add(new ValidationFailure(field, $"\"{def.Label}\" metin olmalıdır."));
                    } else if (raw.GetString()!.Trim().Length > 500) {
                        failures.Add(new ValidationFailure(field, $"\"{def.Label}\" en fazla 500 karakter olabilir."));
                    } else {
                        result[def.Key] = raw.GetString()!.Trim();
                    }

                    break;
                case CustomFieldType.Number:
                    if (raw.ValueKind == JsonValueKind.Number && raw.TryGetDecimal(out var number))
                        result[def.Key] = number;
                    else
                        failures.Add(new ValidationFailure(field, $"\"{def.Label}\" sayı olmalıdır."));
                    break;
                case CustomFieldType.Date:
                    if (raw.ValueKind == JsonValueKind.String
                        && DateOnly.TryParseExact(raw.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                  DateTimeStyles.None, out var date))
                        result[def.Key] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    else
                        failures.Add(new ValidationFailure(field, $"\"{def.Label}\" yyyy-AA-gg biçiminde tarih olmalıdır."));
                    break;
                case CustomFieldType.Select:
                    if (raw.ValueKind == JsonValueKind.String && def.Options.Contains(raw.GetString()))
                        result[def.Key] = raw.GetString()!;
                    else
                        failures.Add(new ValidationFailure(field, $"\"{def.Label}\" için geçerli bir seçenek seçin."));
                    break;
                case CustomFieldType.Bool:
                    if (raw.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        result[def.Key] = raw.GetBoolean();
                    else
                        failures.Add(new ValidationFailure(field, $"\"{def.Label}\" evet/hayır olmalıdır."));
                    break;
            }
        }

        return JsonSerializer.Serialize(result);
    }
}

public abstract record CustomFieldInput {
    public string          Label            { get; init; } = string.Empty;
    public string[]?       Options          { get; init; }
    public bool            Required         { get; init; }
    public bool            AppliesToProduct { get; init; } = true;
    public bool            AppliesToService { get; init; } = true;
    public int             Sort             { get; init; }
}

public static class CustomFieldInputRules {
    public static void AddLabelOptionRules<T>(this AbstractValidator<T> v, Func<T, CustomFieldType?> type)
        where T : CustomFieldInput {
        v.RuleFor(c => c.Label).NotEmpty().WithMessage("Alan adı zorunludur.")
            .Must(l => l is null || l.Trim().Length <= 100).WithMessage("Alan adı en fazla 100 karakter olabilir.");
        v.RuleFor(c => c.Sort).InclusiveBetween(0, 1000).WithMessage("Sıra 0 ile 1000 arasında olmalıdır.");
        v.RuleFor(c => c).Must(c => c.AppliesToProduct || c.AppliesToService)
            .WithName("AppliesToProduct").WithMessage("Alan ürün ya da hizmete uygulanmalıdır.");
        v.RuleFor(c => c).Must(c => c.Options is { } o && o.Length is >= 1 and <= 50
                                    && o.All(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 50)
                                    && o.Select(x => x.Trim()).Distinct().Count() == o.Length)
            .WithName("Options").WithMessage("Seçenekler 1 ile 50 adet, benzersiz ve en fazla 50 karakter olmalıdır.")
            .When(c => type(c) == CustomFieldType.Select);
    }
}

[Authorize]
public sealed record ListCustomFieldsQuery : IRequest<IReadOnlyList<CustomFieldDto>>;

/// <summary>Ürün formu tanımları okuyabilsin diye katalog.read ya da ayarlar.katalog yeterlidir.</summary>
public sealed class ListCustomFieldsQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<ListCustomFieldsQuery, IReadOnlyList<CustomFieldDto>> {
    public async Task<IReadOnlyList<CustomFieldDto>> Handle(ListCustomFieldsQuery request, CancellationToken ct) {
        if (session.TenantId is null
            || !(session.HasPermission(CatalogPermissions.Settings) || session.HasPermission(CatalogPermissions.Read)))
            throw new ForbiddenAccessException();

        var rows = await db.CustomFieldDefinitions.AsNoTracking().OrderBy(d => d.Sort).ThenBy(d => d.Key)
                       .ToListAsync(ct);
        return rows.Select(CustomFieldRules.ToDto).ToList();
    }
}

[Authorize(Permission = CatalogPermissions.Settings)]
public sealed record CreateCustomFieldCommand : CustomFieldInput, IRequest<CustomFieldDto> {
    public string          Key  { get; init; } = string.Empty;
    public CustomFieldType Type { get; init; }
}

public sealed class CreateCustomFieldCommandValidator : AbstractValidator<CreateCustomFieldCommand> {
    public CreateCustomFieldCommandValidator() {
        this.AddLabelOptionRules(c => c.Type);
        RuleFor(c => c.Key).Must(CustomFieldRules.IsValidKey)
            .WithMessage("Anahtar küçük harfle başlamalı; yalnızca küçük harf, rakam ve alt çizgi içermeli (2-31 karakter).");
        RuleFor(c => c.Type).IsInEnum().WithMessage("Alan türü geçersiz.");
    }
}

public sealed class CreateCustomFieldCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<CreateCustomFieldCommand, CustomFieldDto> {
    public async Task<CustomFieldDto> Handle(CreateCustomFieldCommand request, CancellationToken ct) {
        if (await db.CustomFieldDefinitions.AnyAsync(d => d.Key == request.Key, ct))
            throw CustomFieldRules.KeyTaken();

        if (await db.CustomFieldDefinitions.CountAsync(ct) >= CustomFieldRules.MaxDefinitions)
            throw new AppException(409, "too_many_custom_fields", "En fazla 30 özel alan tanımlanabilir.");

        var definition = new CustomFieldDefinition {
            Key = request.Key, Type = request.Type, Label = request.Label.Trim(),
            Options = request.Type == CustomFieldType.Select ? request.Options!.Select(o => o.Trim()).ToArray() : [],
            Required = request.Required, AppliesToProduct = request.AppliesToProduct,
            AppliesToService = request.AppliesToService, Sort = request.Sort
        };
        db.CustomFieldDefinitions.Add(definition);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (CustomFieldRules.IsUniqueViolation(ex)) {
            throw CustomFieldRules.KeyTaken();
        }

        return CustomFieldRules.ToDto(definition);
    }
}

/// <summary>Anahtar ve tür değişmez (mevcut ürün değerleri bozulmasın).</summary>
[Authorize(Permission = CatalogPermissions.Settings)]
public sealed record UpdateCustomFieldCommand : CustomFieldInput, IRequest<CustomFieldDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateCustomFieldCommandValidator : AbstractValidator<UpdateCustomFieldCommand> {
    public UpdateCustomFieldCommandValidator() {
        RuleFor(c => c.Label).NotEmpty().WithMessage("Alan adı zorunludur.")
            .Must(l => l is null || l.Trim().Length <= 100).WithMessage("Alan adı en fazla 100 karakter olabilir.");
        RuleFor(c => c.Sort).InclusiveBetween(0, 1000).WithMessage("Sıra 0 ile 1000 arasında olmalıdır.");
        RuleFor(c => c).Must(c => c.AppliesToProduct || c.AppliesToService)
            .WithName("AppliesToProduct").WithMessage("Alan ürün ya da hizmete uygulanmalıdır.");
    }
}

public sealed class UpdateCustomFieldCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<UpdateCustomFieldCommand, CustomFieldDto> {
    public async Task<CustomFieldDto> Handle(UpdateCustomFieldCommand request, CancellationToken ct) {
        var definition = await db.CustomFieldDefinitions.FirstOrDefaultAsync(d => d.Id == request.Id, ct)
                         ?? throw new NotFoundException(request.Id.ToString(), "CustomFieldDefinition");

        if (definition.Type == CustomFieldType.Select) {
            var options = request.Options?.Select(o => o.Trim()).ToArray() ?? [];
            if (options.Length is < 1 or > 50 || options.Any(o => o.Length is 0 or > 50)
                || options.Distinct().Count() != options.Length)
                throw new Common.Exceptions.ValidationException([
                    new ValidationFailure("Options", "Seçenekler 1 ile 50 adet, benzersiz ve en fazla 50 karakter olmalıdır.")
                ]);

            definition.Options = options;
        }

        definition.Label            = request.Label.Trim();
        definition.Required         = request.Required;
        definition.AppliesToProduct = request.AppliesToProduct;
        definition.AppliesToService = request.AppliesToService;
        definition.Sort             = request.Sort;
        await db.SaveChangesAsync(ct);
        return CustomFieldRules.ToDto(definition);
    }
}

[Authorize(Permission = CatalogPermissions.Settings)]
public sealed record DeleteCustomFieldCommand(Guid Id) : IRequest;

/// <summary>Tanım silinir; kartlarda kalan değerler bir sonraki kayıtta temizlenir.</summary>
public sealed class DeleteCustomFieldCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<DeleteCustomFieldCommand> {
    public async Task Handle(DeleteCustomFieldCommand request, CancellationToken ct) {
        var definition = await db.CustomFieldDefinitions.FirstOrDefaultAsync(d => d.Id == request.Id, ct)
                         ?? throw new NotFoundException(request.Id.ToString(), "CustomFieldDefinition");
        db.CustomFieldDefinitions.Remove(definition);
        await db.SaveChangesAsync(ct);
    }
}
