using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts;

public sealed record AddressDto(Guid Id, string Title, string Line, string? District, string? City, string? PostalCode, string Country,
    bool IsBilling, bool IsShipping, bool IsDefaultBilling, bool IsDefaultShipping);

public sealed record CurrentAccountListItem(Guid Id, string Code, string Name, bool IsCustomer, bool IsSupplier, string Currency,
    decimal Balance, bool OverLimit, decimal OverdueAmount, bool IsActive);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

public sealed record CurrentAccountDto(Guid Id, string Code, string Name, AccountKind Kind, string? TaxNumber, string? TaxOffice,
    bool IsEInvoiceTaxpayer, bool IsCustomer, bool IsSupplier, string? Phone, string? Email, string? Notes, string Currency,
    int DefaultDueDays, decimal? CreditLimit, bool IsActive, decimal Balance, decimal OverdueAmount, bool HasMovements,
    IReadOnlyList<AddressDto> Addresses);

public sealed record NextCodeDto(string Code);

/// <summary>Cari kartın düzenlenebilir alanları (oluşturma ve güncelleme istekleri ortak kullanır).</summary>
public interface ICardFields
{
    string Code { get; }

    string Name { get; }

    AccountKind Kind { get; }

    string? TaxNumber { get; }

    string? TaxOffice { get; }

    bool IsEInvoiceTaxpayer { get; }

    bool IsCustomer { get; }

    bool IsSupplier { get; }

    string? Phone { get; }

    string? Email { get; }

    string? Notes { get; }

    string Currency { get; }

    int DefaultDueDays { get; }

    decimal? CreditLimit { get; }
}

public sealed record AddressInput
{
    public string Title { get; init; } = string.Empty;

    public string Line { get; init; } = string.Empty;

    public string? District { get; init; }

    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string Country { get; init; } = "Türkiye";

    public bool IsBilling { get; init; }

    public bool IsShipping { get; init; }

    public bool IsDefaultBilling { get; init; }

    public bool IsDefaultShipping { get; init; }
}

public sealed record OpeningBalanceInput
{
    public decimal Amount { get; init; }

    /// <summary>"debit" (borç) veya "credit" (alacak).</summary>
    public string Direction { get; init; } = string.Empty;

    public DateOnly Date { get; init; }
}
