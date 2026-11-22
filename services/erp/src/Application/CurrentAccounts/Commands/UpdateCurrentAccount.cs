using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record UpdateCurrentAccountCommand : IRequest<CurrentAccountDto>, ICardFields
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public AccountKind Kind { get; init; }

    public string? TaxNumber { get; init; }

    public string? TaxOffice { get; init; }

    public bool IsEInvoiceTaxpayer { get; init; }

    public bool IsCustomer { get; init; }

    public bool IsSupplier { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public string? Notes { get; init; }

    public string Currency { get; init; } = "TRY";

    public int DefaultDueDays { get; init; } = 30;

    public decimal? CreditLimit { get; init; }
}

public sealed class UpdateCurrentAccountCommandValidator : AbstractValidator<UpdateCurrentAccountCommand>
{
    public UpdateCurrentAccountCommandValidator() => this.AddCardRules();
}

public sealed class UpdateCurrentAccountCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<UpdateCurrentAccountCommand, CurrentAccountDto>
{
    public async Task<CurrentAccountDto> Handle(UpdateCurrentAccountCommand request, CancellationToken ct)
    {
        var account = await db.CurrentAccounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new NotFoundException(request.Id.ToString(), "CurrentAccount");

        var code = request.Code.Trim();
        var taxNumber = CardRules.Clean(request.TaxNumber);

        if (await db.CurrentAccounts.AnyAsync(a => a.Id != account.Id && a.Code == code, ct))
        {
            throw CariErrors.CodeTaken();
        }

        if (taxNumber is not null && await db.CurrentAccounts.AnyAsync(a => a.Id != account.Id && a.TaxNumber == taxNumber, ct))
        {
            throw CariErrors.TaxNumberTaken();
        }

        if (request.Currency != account.Currency && await db.LedgerEntries.AnyAsync(e => e.CurrentAccountId == account.Id, ct))
        {
            throw CariErrors.CurrencyLocked();
        }

        CardRules.Apply(account, request);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CariErrors.FromUniqueViolation(ex) is { } conflict)
        {
            throw conflict;
        }

        return await CurrentAccountReader.GetDtoAsync(db, account.Id, CurrentAccountClock.Today(time), ct);
    }
}
