using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Application.Settings.Categories;
using FluentValidation.Results;

namespace ERP.Application.Settings;

/// <summary>Bir ayar kategorisi: URL anahtarı ve okuma/yazma izni.</summary>
public interface ISettingsCategory<T> where T : class, new() {
    static abstract string Key { get; }

    static abstract string Permission { get; }
}

/// <summary>Diğer ERP kodunun tip güvenli ayar okuması için (izin denetimi yapmaz). Kayıt yoksa varsayılanları döner.</summary>
public interface ITenantSettings {
    Task<T> GetAsync<T>(CancellationToken ct) where T : class, ISettingsCategory<T>, new();
}

/// <summary>Kategori kaydı: anahtardan tipe, doğrulamaya ve normalleştirmeye giden tek yer.</summary>
public sealed class SettingsCategoryInfo(
    string                                       key,
    string                                       permission,
    Func<object>                                 createDefault,
    Func<JsonElement, object>                    deserialize,
    Func<object, object>                         normalize,
    Func<object, IEnumerable<ValidationFailure>> validate
) {
    public string Key { get; } = key;

    public string Permission { get; } = permission;

    public object CreateDefault() {
        return createDefault();
    }

    public object Deserialize(JsonElement json) {
        return deserialize(json);
    }

    public object Normalize(object value) {
        return normalize(value);
    }

    public IEnumerable<ValidationFailure> Validate(object value) {
        return validate(value);
    }
}

public static class SettingsRegistry {
    /// <summary>Hem saklama hem API gövdesi için ortak seçenekler (camelCase, enum'lar camelCase metin).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly Dictionary<string, SettingsCategoryInfo> Categories = new(StringComparer.Ordinal) {
        [CompanyProfileSettings.Key] = Create<CompanyProfileSettings>(CompanyProfileSettings.Normalize,
                                                                       new CompanyProfileSettingsValidator()),
        [GeneralSettings.Key] = Create<GeneralSettings>(GeneralSettings.Normalize, new GeneralSettingsValidator()),
        [FinanceSettings.Key] = Create<FinanceSettings>(FinanceSettings.Normalize, new FinanceSettingsValidator()),
        [SetupSettings.Key]   = Create<SetupSettings>(s => s, new SetupSettingsValidator()),
        [CatalogSettings.Key] = Create<CatalogSettings>(CatalogSettings.Normalize, new CatalogSettingsValidator()),
        [PricingSettings.Key] = Create<PricingSettings>(s => s, new PricingSettingsValidator()),
        [StockSettings.Key]   = Create<StockSettings>(s => s, new StockSettingsValidator()),
        [DocumentSettings.Key] = Create<DocumentSettings>(s => s, new DocumentSettingsValidator()),
        [EBelgeSettings.Key]   = Create<EBelgeSettings>(s => s, new EBelgeSettingsValidator())
    };

    public static SettingsCategoryInfo? Find(string key) {
        return Categories.GetValueOrDefault(key);
    }

    public static SettingsCategoryInfo For<T>() where T : class, ISettingsCategory<T>, new() {
        return Categories[T.Key];
    }

    private static SettingsCategoryInfo Create<T>(Func<T, T> normalize, IValidator<T> validator)
        where T : class, ISettingsCategory<T>, new() {
        return new SettingsCategoryInfo(T.Key, T.Permission,
                                        () => new T(),
                                        json => json.Deserialize<T>(Json) ?? new T(),
                                        value => normalize((T)value),
                                        value => validator.Validate((T)value).Errors);
    }
}
