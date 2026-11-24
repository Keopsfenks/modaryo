using ERP.Application.Common.Security;

namespace ERP.Application.Treasury.Payments.Commands;

/// <summary>Belgeyi iptal eder: hesap hareketi, defter hareketi ve eşleştirmeler geri alınır. İzin belge türüne göre denetlenir.</summary>
[Authorize]
public sealed record CancelFinanceDocumentCommand(Guid Id, string Reason) : IRequest;

public sealed class CancelFinanceDocumentCommandValidator : AbstractValidator<CancelFinanceDocumentCommand> {
    public CancelFinanceDocumentCommandValidator() {
        RuleFor(c => c.Reason).Must(r => r is not null && r.Trim().Length is >= 2 and <= 200)
            .WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelFinanceDocumentCommandHandler(FinancePoster poster) : IRequestHandler<CancelFinanceDocumentCommand> {
    public Task Handle(CancelFinanceDocumentCommand request, CancellationToken ct) {
        return poster.CancelAsync(request.Id, request.Reason.Trim(), ct);
    }
}
