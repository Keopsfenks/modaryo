using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Commands;

[Authorize(Permission = InvoicingPermissions.Manage)]
public sealed record CreateDraftCommand(InvoiceInput Input) : IRequest<InvoiceDto>;

public sealed class CreateDraftCommandValidator : AbstractValidator<CreateDraftCommand> {
    public CreateDraftCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new InvoiceInputValidator());
    }
}

public sealed class CreateDraftCommandHandler(
    IApplicationDbContext db,
    InvoiceBuilder        builder
) : IRequestHandler<CreateDraftCommand, InvoiceDto> {
    public async Task<InvoiceDto> Handle(CreateDraftCommand request, CancellationToken ct) {
        var input = request.Input;
        var built = await builder.BuildAsync(input, null, false, ct);

        var invoice = new Invoice { Status = InvoiceStatus.Draft, SourceType = "manual" };
        InvoiceBuilder.Apply(invoice, input, built);
        foreach (var line in built.Lines) {
            line.InvoiceId = invoice.Id;
            invoice.Lines.Add(line);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = invoice.Id;
            invoice.DueSchedule.Add(due);
        }

        db.Invoices.Add(invoice);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoiceErrors.ExternalNumberTaken();
        }

        return InvoiceRules.ToDto(invoice, built.Cari.Name);
    }
}
