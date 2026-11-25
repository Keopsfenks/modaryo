using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Purchasing.Receipts;

namespace ERP.Application.Purchasing.Returns;

public sealed record ReturnLineInput(Guid ReceiptLineId, decimal Quantity, IReadOnlyList<LotInput>? Lots, IReadOnlyList<string>? Serials);

public sealed record ReturnInput(Guid ReceiptId, IReadOnlyList<ReturnLineInput> Lines, DateOnly Date, string Reason, bool CreateInvoice);

public sealed record ReturnResultDto(Guid ReturnId, Guid ReceiptId, Guid? InvoiceId, string? InvoiceNumber);

/// <summary>Alış iadesi. Kaynak depo: önce karantinadaki açık miktar, kalanı mal kabulün hedef deposu. Miktarlar mal kabul satırının biriminde.</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record CreateReturnCommand(ReturnInput Input) : IRequest<ReturnResultDto>;

public sealed class CreateReturnCommandValidator : AbstractValidator<CreateReturnCommand> {
    public CreateReturnCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ReceiptId).NotEmpty().WithMessage("Mal kabul seçilmelidir.");
        RuleFor(c => c.Input.Date).Must(d => d != default).WithMessage("İade tarihi zorunludur.");
        RuleFor(c => c.Input.Reason).NotEmpty().WithMessage("İade nedeni zorunludur.").MaximumLength(200).WithMessage("İade nedeni en fazla 200 karakter olabilir.");
        RuleFor(c => c.Input.Lines).NotEmpty().WithMessage("İadede en az bir satır olmalıdır.");
        RuleForEach(c => c.Input.Lines).ChildRules(l => {
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("İade miktarı sıfırdan büyük olmalıdır.")
                .Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
        });
    }
}

public sealed class CreateReturnCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    PurchaseReturnService service
) : IRequestHandler<CreateReturnCommand, ReturnResultDto> {
    public async Task<ReturnResultDto> Handle(CreateReturnCommand request, CancellationToken ct) {
        var input      = request.Input;
        var head       = await ReceiptRules.LoadAsync(db, tenant, input.ReceiptId, false, ct);
        var productIds = head.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        var wanted = head.Lines.Where(l => input.Lines.Any(i => i.ReceiptLineId == l.Id)).ToList();
        await db.LockStockAsync(PurchaseReturnService.KeysFor(wanted, products), ct);

        // Miktar denetimleri kilitten sonra: mal kabul satırları kilit altında yeniden okunur.
        var receipt = await ReceiptRules.LoadAsync(db, tenant, input.ReceiptId, true, ct);
        var outcome = await service.ReturnAsync(receipt, input.Lines.Select(l => new ReturnLineRequest(l.ReceiptLineId, l.Quantity, l.Lots, l.Serials)).ToList(),
                                                input.Date, input.Reason.Trim(), input.CreateInvoice, false, ct);
        await tx.CommitAsync(ct);
        return new ReturnResultDto(outcome.ReturnId, receipt.Id, outcome.InvoiceId, outcome.InvoiceNumber);
    }
}
