using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;

namespace ERP.Application.Treasury.Journal.Commands;

/// <summary>Elle mahsup fişi (muhasebe.manage). Toplam borç = toplam alacak (400 voucher_unbalanced).</summary>
[Authorize(Permission = TreasuryPermissions.Journal)]
public sealed record CreateVoucherCommand : IRequest<VoucherResult> {
    public DateOnly Date { get; init; }

    public string? Description { get; init; }

    public List<JournalLineInput> Lines { get; init; } = [];
}

public sealed class CreateVoucherCommandValidator : AbstractValidator<CreateVoucherCommand> {
    public CreateVoucherCommandValidator() {
        RuleFor(c => c.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        RuleFor(c => c.Description).NotEmpty().WithMessage("Açıklama zorunludur.").MaximumLength(500).WithMessage("Açıklama en çok 500 karakter olabilir.");
        this.AddLineRules(c => c.Lines);
    }
}

public sealed class CreateVoucherCommandHandler(
    IApplicationDbContext db,
    JournalVoucherPoster  poster
) : IRequestHandler<CreateVoucherCommand, VoucherResult> {
    public async Task<VoucherResult> Handle(CreateVoucherCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        var result = await poster.PostAsync(new VoucherInput(request.Date, request.Description!, "Manual", null, null, request.Lines), ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
