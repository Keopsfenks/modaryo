using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Payments.Commands;

/// <summary>Tahsilat: hesaba giriş, cari alacak. Avans bir raporlama bayrağıdır (SalesOrderId sipariş modülü gelince doldurulur).</summary>
[Authorize(Permission = TreasuryPermissions.Collection)]
public sealed record CreateCollectionCommand : PaymentInput, IRequest<PostResult> {
    public Guid? SalesOrderId { get; init; }
}

public sealed class CreateCollectionCommandValidator : AbstractValidator<CreateCollectionCommand> {
    public CreateCollectionCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreateCollectionCommandHandler(FinancePoster poster) : IRequestHandler<CreateCollectionCommand, PostResult> {
    public Task<PostResult> Handle(CreateCollectionCommand request, CancellationToken ct) {
        return poster.PostAsync(FinanceDocumentKind.Collection, request, request.SalesOrderId, null, ct);
    }
}
