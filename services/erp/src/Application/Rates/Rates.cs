using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Invoicing;
using Microsoft.Extensions.Logging;

namespace ERP.Application.Rates;

public sealed record RateDto(string Currency, RateType Type, decimal Rate, DateOnly EffectiveDate, string Source, bool Overridden);

public static class RateRules {
    public const int LookBackDays = 10;

    public static readonly string[] Currencies = ["USD", "EUR", "GBP"];

    /// <summary>Türkiye saatiyle (UTC+3, yaz saati yok) bugün.</summary>
    public static DateOnly Today(TimeProvider clock) {
        return DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
    }
}

[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record ListRatesQuery(DateOnly? Date) : IRequest<IReadOnlyList<RateDto>>;

public sealed class ListRatesQueryHandler(
    IApplicationDbContext db,
    IExchangeRates        rates,
    TimeProvider          clock
) : IRequestHandler<ListRatesQuery, IReadOnlyList<RateDto>> {
    public async Task<IReadOnlyList<RateDto>> Handle(ListRatesQuery request, CancellationToken ct) {
        var date      = request.Date ?? RateRules.Today(clock);
        var from      = date.AddDays(-RateRules.LookBackDays);
        var overrides = await db.TenantRateOverrides.AsNoTracking().Where(o => o.Date >= from && o.Date <= date).ToListAsync(ct);
        var result    = new List<RateDto>();
        foreach (var currency in RateRules.Currencies) {
            foreach (var type in Enum.GetValues<RateType>()) {
                try {
                    var (rate, effective) = await rates.GetAsync(currency, date, type, ct);
                    var manual = overrides.Any(o => o.Currency == currency && o.Type == type && o.Date == effective);
                    result.Add(new RateDto(currency, type, rate, effective, manual ? "manual" : "tcmb", manual));
                }
                catch (AppException e) when (e.Code == "rate_missing") {
                    // Bu tür için kur yok (örn. bazı banknot kurları); listeden çıkar.
                }
            }
        }

        return result;
    }
}

[Authorize(Permission = InvoicingPermissions.RateManage)]
public sealed record SetRateOverrideCommand(DateOnly Date, string Currency, RateType Type, decimal Rate) : IRequest<RateDto>;

public sealed class SetRateOverrideCommandValidator : AbstractValidator<SetRateOverrideCommand> {
    public SetRateOverrideCommandValidator() {
        RuleFor(c => c.Currency).Must(c => c is not null && RateRules.Currencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage("Para birimi USD, EUR ya da GBP olmalıdır.");
        RuleFor(c => c.Type).IsInEnum().WithMessage("Kur türü geçersiz.");
        RuleFor(c => c.Rate).GreaterThan(0m).WithMessage("Kur sıfırdan büyük olmalıdır.")
            .Must(r => Math.Round(r, 6) == r).WithMessage("Kur en fazla 6 ondalık basamak içerebilir.")
            .LessThan(1_000_000m).WithMessage("Kur çok büyük.");
    }
}

public sealed class SetRateOverrideCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<SetRateOverrideCommand, RateDto> {
    public async Task<RateDto> Handle(SetRateOverrideCommand request, CancellationToken ct) {
        var currency = request.Currency.Trim().ToUpperInvariant();
        var label    = tenant.UserFullName ?? "bilinmiyor";
        var existing = await db.TenantRateOverrides.FirstOrDefaultAsync(o => o.Date == request.Date && o.Currency == currency && o.Type == request.Type, ct);
        if (existing is null)
            db.TenantRateOverrides.Add(new TenantRateOverride { Date = request.Date, Currency = currency, Type = request.Type, Rate = request.Rate, ActorLabel = label });
        else {
            existing.Rate       = request.Rate;
            existing.ActorLabel = label;
        }

        try {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            var current = await db.TenantRateOverrides.FirstAsync(o => o.Date == request.Date && o.Currency == currency && o.Type == request.Type, ct);
            current.Rate       = request.Rate;
            current.ActorLabel = label;
            await db.SaveChangesAsync(ct);
        }

        return new RateDto(currency, request.Type, request.Rate, request.Date, "manual", true);
    }
}

public sealed record FetchRatesResult(int Stored, int Failed);

[Authorize(Permission = InvoicingPermissions.RateManage)]
public sealed record FetchRatesCommand : IRequest<FetchRatesResult>;

public sealed class FetchRatesCommandHandler(
    IApplicationDbContext             db,
    ITcmbRateSource                   source,
    TimeProvider                      clock,
    ILogger<FetchRatesCommandHandler> logger
) : IRequestHandler<FetchRatesCommand, FetchRatesResult> {
    public async Task<FetchRatesResult> Handle(FetchRatesCommand request, CancellationToken ct) {
        var result = await RateImport.ImportAsync(db, source, RateRules.Today(clock), logger, ct);
        return new FetchRatesResult(result.Stored, result.Failed);
    }
}

/// <summary>Tek kur sorgusu (fatura formu varsayılan kuru için). type verilmezse finans ayarındaki kur türü kullanılır.</summary>
[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record LookupRateQuery(string Currency, DateOnly Date, RateType? Type) : IRequest<RateDto>;

public sealed class LookupRateQueryHandler(
    IApplicationDbContext db,
    IExchangeRates        rates,
    ITenantSettings       settings
) : IRequestHandler<LookupRateQuery, RateDto> {
    public async Task<RateDto> Handle(LookupRateQuery request, CancellationToken ct) {
        var currency = request.Currency.Trim().ToUpperInvariant();
        var type     = request.Type ?? (await settings.GetAsync<FinanceSettings>(ct)).RateType;
        var (rate, effective) = await rates.GetAsync(currency, request.Date, type, ct);
        var manual = currency != "TRY" && await db.TenantRateOverrides.AnyAsync(o => o.Currency == currency && o.Type == type && o.Date == effective, ct);
        return new RateDto(currency, type, rate, effective, currency == "TRY" ? "system" : manual ? "manual" : "tcmb", manual);
    }
}
