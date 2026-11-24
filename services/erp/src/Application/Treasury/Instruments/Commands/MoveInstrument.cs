using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments.Commands;

/// <summary>
///     Durum geçişi. currentAccountId yalnızca ciroda (tedarikçi), accountId tahsile verme, geç tahsilat ve notun ödemesinde kullanılır.
///     Geçersiz geçiş 409 invalid_instrument_transition.
/// </summary>
[Authorize(Permission = TreasuryPermissions.Instrument)]
[RequireModule("cek-senet")]
public sealed record MoveInstrumentCommand : IRequest<InstrumentPostResult> {
    public Guid Id { get; init; }

    public InstrumentStatus To { get; init; }

    public DateOnly Date { get; init; }

    public Guid? CurrentAccountId { get; init; }

    public Guid? AccountId { get; init; }

    public string? Note { get; init; }
}

public sealed class MoveInstrumentCommandValidator : AbstractValidator<MoveInstrumentCommand> {
    public MoveInstrumentCommandValidator() {
        RuleFor(c => c.To).IsInEnum().WithMessage("Hedef durum geçersiz.");
        RuleFor(c => c.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        RuleFor(c => c.Note).MaximumLength(500).WithMessage("Not en çok 500 karakter olabilir.");
    }
}

public sealed class MoveInstrumentCommandHandler(InstrumentPoster poster) : IRequestHandler<MoveInstrumentCommand, InstrumentPostResult> {
    public Task<InstrumentPostResult> Handle(MoveInstrumentCommand request, CancellationToken ct) {
        return poster.MoveAsync(request.Id, request.To, request.Date, request.CurrentAccountId, request.AccountId, request.Note, ct);
    }
}
