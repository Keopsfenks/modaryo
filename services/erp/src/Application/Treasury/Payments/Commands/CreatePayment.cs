using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Payments.Commands;

/// <summary>Ödeme: hesaptan çıkış, cari borç. Avans bir raporlama bayrağıdır (PurchaseOrderId sipariş modülü gelince doldurulur).</summary>
[Authorize(Permission = TreasuryPermissions.Payment)]
public sealed record CreatePaymentCommand : PaymentInput, IRequest<PostResult> {
    public Guid? PurchaseOrderId { get; init; }
}

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand> {
    public CreatePaymentCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreatePaymentCommandHandler(FinancePoster poster) : IRequestHandler<CreatePaymentCommand, PostResult> {
    public Task<PostResult> Handle(CreatePaymentCommand request, CancellationToken ct) {
        return poster.PostAsync(FinanceDocumentKind.Payment, request, null, request.PurchaseOrderId, ct);
    }
}
