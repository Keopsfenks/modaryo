using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Commands;

[Authorize(Permission = InvoicingPermissions.Issue)]
public sealed record IssueInvoiceCommand(Guid Id) : IRequest<InvoiceDto>;

public sealed class IssueInvoiceCommandHandler(InvoiceIssuer issuer) : IRequestHandler<IssueInvoiceCommand, InvoiceDto> {
    public async Task<InvoiceDto> Handle(IssueInvoiceCommand request, CancellationToken ct) {
        var (invoice, cariName) = await issuer.IssueAsync(request.Id, true, ct);
        return InvoiceRules.ToDto(invoice, cariName);
    }
}
