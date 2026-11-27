using ERP.Domain.Sales;

namespace ERP.Application.Sales.Recurring;

public sealed record RecurringLineInput(
    Guid?    ProductId,
    Guid?    VariantId,
    string?  Description,
    string?  Unit,
    decimal  Quantity,
    decimal  UnitPrice,
    bool     VatIncluded,
    decimal? DiscountPercent,
    int?     VatRate,
    bool     FixedPrice = true
);

public sealed record RecurringInput(
    string?                          Name,
    Guid                             CurrentAccountId,
    Guid                             BranchId,
    string                           Currency,
    RecurrenceInterval               Interval,
    int                              DayOfMonth,
    DateOnly                         StartDate,
    DateOnly?                        EndDate,
    RecurringMode                    Mode,
    string?                          Notes,
    IReadOnlyList<RecurringLineInput> Lines
);

public sealed record RecurringLineDto(
    Guid     Id,
    int      Sort,
    Guid?    ProductId,
    Guid?    VariantId,
    string   Description,
    string   Unit,
    decimal  Quantity,
    decimal  UnitPrice,
    bool     VatIncluded,
    decimal? DiscountPercent,
    int      VatRate,
    bool     FixedPrice
);

public sealed record RecurringDto(
    Guid                              Id,
    string                            Number,
    string                            Name,
    Guid                              CurrentAccountId,
    string                            CurrentAccountName,
    Guid                              BranchId,
    string                            Currency,
    RecurrenceInterval                Interval,
    int                               DayOfMonth,
    DateOnly                          StartDate,
    DateOnly?                         EndDate,
    RecurringMode                     Mode,
    bool                              IsActive,
    string?                           Notes,
    decimal                           Net,
    decimal                           Vat,
    decimal                           Total,
    DateOnly?                         NextPeriod,
    DateOnly?                         LastPeriod,
    IReadOnlyList<RecurringLineDto>   Lines
);

public sealed record RecurringListItem(
    Guid               Id,
    string             Number,
    string             Name,
    Guid               CurrentAccountId,
    string             CurrentAccountName,
    string             Currency,
    RecurrenceInterval Interval,
    int                DayOfMonth,
    DateOnly           StartDate,
    DateOnly?          EndDate,
    RecurringMode      Mode,
    bool               IsActive,
    decimal            Total,
    DateOnly?          NextPeriod,
    DateOnly?          LastPeriod
);

public sealed record RecurringRunDto(
    Guid            Id,
    DateOnly        Period,
    Guid            InvoiceId,
    string?         InvoiceNumber,
    string          InvoiceStatus,
    decimal?        Payable,
    DateTimeOffset  GeneratedAt
);
