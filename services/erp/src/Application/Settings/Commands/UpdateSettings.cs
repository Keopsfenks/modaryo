using System.Text.Json;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Files.Commands;
using ERP.Application.Settings.Categories;
using ERP.Domain.Pricing;
using ERP.Domain.Settings;
using FluentValidation.Results;

namespace ERP.Application.Settings.Commands;

/// <summary>Kategorinin kendi izni (ör. ayarlar.firma) işleyicide denetlenir.</summary>
[Authorize]
public sealed record UpdateSettingsCommand(
    string      Category,
    int         Version,
    JsonElement Value
) : IRequest<SettingsDto>;

public sealed class UpdateSettingsCommandValidator : AbstractValidator<UpdateSettingsCommand> {
    public UpdateSettingsCommandValidator() {
        RuleFor(c => c.Version).GreaterThanOrEqualTo(0).WithMessage("Sürüm 0 veya daha büyük olmalıdır.");
        RuleFor(c => c.Value).Must(v => v.ValueKind == JsonValueKind.Object)
            .WithMessage("Ayar değeri bir nesne olmalıdır.");
    }
}

public sealed class UpdateSettingsCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ICurrentTenant        tenant,
    TimeProvider          time
) : IRequestHandler<UpdateSettingsCommand, SettingsDto> {
    private const string ConflictMessage =
        "Ayarlar siz düzenlerken başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.";

    public async Task<SettingsDto> Handle(UpdateSettingsCommand request, CancellationToken ct) {
        var info = SettingsSupport.Resolve(request.Category, session);

        object value;
        try {
            value = info.Normalize(info.Deserialize(request.Value));
        } catch (JsonException) {
            throw new Common.Exceptions.ValidationException([new ValidationFailure("value", "Ayar değeri okunamadı.")]);
        }

        var failures = info.Validate(value).ToList();
        if (failures.Count > 0) throw new Common.Exceptions.ValidationException(failures);

        // Logo, aynı firmaya ait bir "logo" dosyası olmalı (başka firmanın dosyası sorgu filtresiyle görünmez).
        if (value is CompanyProfileSettings { LogoFileId: { } logoId }
            && !await db.StoredFiles.AnyAsync(f => f.Id == logoId && f.Kind == FileKinds.Logo, ct))
            throw new Common.Exceptions.ValidationException([
                new ValidationFailure("LogoFileId", "Logo dosyası bulunamadı. Önce logoyu yükleyip dosyayı seçin.")
            ]);

        // Varsayılan listeler aktif firmanın, doğru türdeki listeleri olmalı (başka firmanın listesi görünmez).
        if (value is PricingSettings pricing) {
            if (pricing.DefaultSaleListId is { } saleId
                && !await db.PriceLists.AnyAsync(l => l.Id == saleId && l.Kind == PriceListKind.Sale, ct))
                throw DefaultListKind("Varsayılan satış listesi bir satış fiyat listesi olmalıdır.");

            if (pricing.DefaultPurchaseListId is { } purchaseId
                && !await db.PriceLists.AnyAsync(l => l.Id == purchaseId && l.Kind == PriceListKind.Purchase, ct))
                throw DefaultListKind("Varsayılan alış listesi bir alış fiyat listesi olmalıdır.");
        }

        var row   = await db.TenantSettings.FirstOrDefaultAsync(s => s.Category == info.Key, ct);
        if (value is StockSettings stock) {
            var current = row is null
                              ? new StockSettings()
                              : JsonSerializer.Deserialize<StockSettings>(row.Json, SettingsRegistry.Json) ?? new StockSettings();
            await StockSettingsRules.EnsureMethodUnlockedAsync(db, current, stock, ct);
        }

        var stored = row?.Version ?? 0;
        if (request.Version != stored) throw VersionConflict();

        var now    = time.GetUtcNow();
        var actor  = tenant.UserFullName ?? tenant.UserId?.ToString() ?? "-";
        var before = row?.Json ?? JsonSerializer.Serialize(info.CreateDefault(), SettingsRegistry.Json);
        var after  = JsonSerializer.Serialize(value, SettingsRegistry.Json);

        if (row is null) {
            row = new TenantSetting { Category = info.Key };
            db.TenantSettings.Add(row);
        }

        row.Json      = after;
        row.Version   = stored + 1;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;

        db.AuditEntries.Add(new ErpAuditEntry {
                                At          = now,
                                ActorUserId = tenant.UserId,
                                ActorLabel  = actor,
                                Action      = "settings.updated",
                                EntityType  = "settings",
                                EntityId    = info.Key,
                                Before      = before,
                                After       = after
                            });

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            throw VersionConflict();
        } catch (DbUpdateException ex) when (IsUniqueViolation(ex)) {
            // Aynı anda ilk kaydı yazan başka bir istek kazandı.
            throw VersionConflict();
        }

        return new SettingsDto(row.Version, value);
    }

    private static AppException DefaultListKind(string message) {
        return new AppException(400, "default_list_required_kind", message);
    }

    private static AppException VersionConflict() {
        return new AppException(409, "version_conflict", ConflictMessage);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner?.GetType().Name == "PostgresException"
               && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}
