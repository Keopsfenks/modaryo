using System.Globalization;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;

namespace ERP.Infrastructure.Settings;

public sealed class PeriodLock(
    ITenantSettings settings
) : IPeriodLock {
    public async Task EnsureOpenAsync(DateOnly date, CancellationToken ct) {
        var lockDate = (await settings.GetAsync<FinanceSettings>(ct)).PeriodLockDate;
        if (lockDate is { } locked && date <= locked)
            throw new AppException(409, "period_locked",
                                   $"{locked.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} ve öncesi kilitli dönemde.");
    }
}
