using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments.Commands;

/// <summary>Tedarikçiye çek/senet verilir: tedarikçi carisi borçlanır (ödeme). Çekte ödeme yapılacak banka hesabı zorunludur.</summary>
[Authorize(Permission = TreasuryPermissions.Instrument)]
[RequireModule("cek-senet")]
public sealed record IssueInstrumentCommand : InstrumentInput, IRequest<InstrumentPostResult> {
    public Guid? BankAccountId { get; init; }
}

public sealed class IssueInstrumentCommandValidator : AbstractValidator<IssueInstrumentCommand> {
    public IssueInstrumentCommandValidator() {
        this.AddRules();
    }
}

public sealed class IssueInstrumentCommandHandler(InstrumentPoster poster) : IRequestHandler<IssueInstrumentCommand, InstrumentPostResult> {
    public Task<InstrumentPostResult> Handle(IssueInstrumentCommand request, CancellationToken ct) {
        return poster.CreateAsync(InstrumentDirection.Issued, request, request.BankAccountId, 0, ct);
    }
}
