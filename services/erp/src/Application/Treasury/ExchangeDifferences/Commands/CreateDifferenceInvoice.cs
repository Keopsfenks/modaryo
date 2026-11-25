using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;

namespace ERP.Application.Treasury.ExchangeDifferences.Commands;

public sealed record DifferenceInvoiceResult(Guid InvoiceId, decimal Amount, int VatRate);

/// <summary>Müşteri (alacak) tarafındaki pozitif kur farklarından KDV dahil tutarlı bir kur farkı satış faturası taslağı oluşturur (KDV varsayılan %20). Her fark bir kez işlenir; tedarikçi tarafı ve negatif farklar için fatura kesilmez.</summary>
[Authorize(Permission = TreasuryPermissions.Manage)]
public sealed record CreateDifferenceInvoiceCommand(IReadOnlyList<Guid> AllocationIds, int? VatRate) : IRequest<DifferenceInvoiceResult>;

public sealed class CreateDifferenceInvoiceCommandValidator : AbstractValidator<CreateDifferenceInvoiceCommand> {
    public CreateDifferenceInvoiceCommandValidator() {
        RuleFor(c => c.AllocationIds).NotEmpty().WithMessage("En az bir kur farkı seçilmelidir.")
            .Must(l => l.Count <= 500).WithMessage("En fazla 500 tahsis seçilebilir.");
        RuleFor(c => c.VatRate).Must(r => InvoiceCalculator.AllowedVatRates.Contains(r!.Value)).When(c => c.VatRate is not null)
            .WithMessage("KDV oranı 0, 1, 10 ya da 20 olmalıdır.");
    }
}

public sealed class CreateDifferenceInvoiceCommandHandler(
    IApplicationDbContext    db,
    ExchangeDifferenceReader reader,
    InvoiceBuilder           builder,
    IBranchDirectory         branches,
    ICurrentTenant           tenant,
    IOutbox                  outbox,
    TimeProvider             time
) : IRequestHandler<CreateDifferenceInvoiceCommand, DifferenceInvoiceResult> {
    public async Task<DifferenceInvoiceResult> Handle(CreateDifferenceInvoiceCommand request, CancellationToken ct) {
        var ids      = Settlement.Distinct(request.AllocationIds);
        var cariId   = await Settlement.CariOfAsync(db, ids, ct);
        var vatRate  = request.VatRate ?? 20;
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(cariId, ct)) throw new NotFoundException(cariId.ToString(), "CurrentAccount");

        var (rows, total, kind) = await Settlement.LoadAsync(reader, cariId, ids, ct);
        if (kind != DifferenceKind.Invoice)
            throw TreasuryErrors.DifferenceNotApplicable(kind == DifferenceKind.Expense
                                                             ? "Fatura yalnızca pozitif kur farkı için kesilir; negatif fark için kur farkı gideri girin."
                                                             : "Tedarikçi tarafındaki kur farkı için satış faturası kesilmez; kur farkı geliri fişi girin.");

        var branch = (await branches.ListAsync(tenantId, ct)).Where(b => b.IsActive).OrderByDescending(b => b.IsDefault).ThenBy(b => b.Code, StringComparer.Ordinal).FirstOrDefault()
                     ?? throw TreasuryErrors.BranchNotFound();
        var notes = $"Kur farkı: {string.Join(", ", rows.Select(r => r.Dto.InvoiceNumber).Where(n => n.Length > 0).Distinct())}";
        var input = new InvoiceInput {
                                         Kind             = InvoiceKind.Sales,
                                         BranchId         = branch.Id,
                                         CurrentAccountId = cariId,
                                         Date             = CurrentAccountClock.Today(time),
                                         Currency         = "TRY",
                                         Notes            = notes.Length > 1000 ? notes[..1000] : notes,
                                         Lines = [
                                             new LineInput { Description = "Kur farkı", Unit = "adet", Quantity = 1m, UnitPrice = total, VatIncluded = true, VatRate = vatRate }
                                         ]
                                     };
        var built   = await builder.BuildAsync(input, null, false, ct);
        var invoice = new Invoice { Status = InvoiceStatus.Draft, SourceType = "kur-farki" };
        InvoiceBuilder.Apply(invoice, input, built);
        invoice.Kind = InvoiceKind.ExchangeDifference;
        foreach (var line in built.Lines) {
            line.InvoiceId = invoice.Id;
            invoice.Lines.Add(line);
        }

        db.Invoices.Add(invoice);
        Settlement.Record(db, outbox, cariId, total, "Invoice", invoice.Id, ids, rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DifferenceInvoiceResult(invoice.Id, total, vatRate);
    }
}
