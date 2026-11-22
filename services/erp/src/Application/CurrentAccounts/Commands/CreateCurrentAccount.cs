using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record CreateCurrentAccountCommand : IRequest<CurrentAccountDto>, ICardFields
{
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

    public AddressInput? Address { get; init; }

    public OpeningBalanceInput? OpeningBalance { get; init; }
}

public sealed class CreateCurrentAccountCommandValidator : AbstractValidator<CreateCurrentAccountCommand>
{
    public CreateCurrentAccountCommandValidator()
    {
        this.AddCardRules();

        RuleFor(c => c.Address!).SetValidator(new AddressInputValidator()).When(c => c.Address is not null);

        RuleFor(c => c.OpeningBalance!.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount)
            .WithMessage("Açılış tutarı 0 değerinden büyük ve 999.999.999.999,99 değerinden küçük olmalıdır.")
            .When(c => c.OpeningBalance is not null);
        RuleFor(c => c.OpeningBalance!.Amount).Must(CardRules.HasValidScale)
            .WithMessage("Açılış tutarı en fazla 2 ondalık basamak içerebilir.")
            .When(c => c.OpeningBalance is not null);
        RuleFor(c => c.OpeningBalance!.Direction)
            .Must(d => d is "debit" or "credit").WithMessage("Açılış yönü borç (debit) veya alacak (credit) olmalıdır.")
            .When(c => c.OpeningBalance is not null);
        RuleFor(c => c.OpeningBalance!.Date).Must(CardRules.IsValidDate)
            .WithMessage(CardRules.DateMessage)
            .When(c => c.OpeningBalance is not null && c.OpeningBalance.Date != default);
        RuleFor(c => c.OpeningBalance!.Date).NotEqual(default(DateOnly)).WithMessage("Açılış tarihi zorunludur.")
            .When(c => c.OpeningBalance is not null);
    }
}

public sealed class CreateCurrentAccountCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<CreateCurrentAccountCommand, CurrentAccountDto>
{
    public async Task<CurrentAccountDto> Handle(CreateCurrentAccountCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim();
        var taxNumber = CardRules.Clean(request.TaxNumber);

        if (await db.CurrentAccounts.AnyAsync(a => a.Code == code, ct))
        {
            throw CariErrors.CodeTaken();
        }

        if (taxNumber is not null && await db.CurrentAccounts.AnyAsync(a => a.TaxNumber == taxNumber, ct))
        {
            throw CariErrors.TaxNumberTaken();
        }

        var account = new CurrentAccount();
        CardRules.Apply(account, request);
        db.CurrentAccounts.Add(account);

        if (request.Address is { } input)
        {
            var address = new CurrentAccountAddress { CurrentAccountId = account.Id };
            AddressRules.Apply(address, input);
            AddressRules.ResolveDefaults([address], address, input.IsDefaultBilling, input.IsDefaultShipping);
            db.CurrentAccountAddresses.Add(address);
        }

        if (request.OpeningBalance is { } opening)
        {
            db.LedgerEntries.Add(EntryRules.NewEntry(account, LedgerEntryType.Opening, opening.Date, null, null, null,
                opening.Amount, opening.Direction == "debit"));
        }

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
