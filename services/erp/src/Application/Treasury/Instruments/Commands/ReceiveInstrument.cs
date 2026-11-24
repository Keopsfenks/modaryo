using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments.Commands;

/// <summary>Müşteriden çek/senet alınır: portföye girer, müşteri carisi alacaklanır.</summary>
[Authorize(Permission = TreasuryPermissions.Instrument)]
[RequireModule("cek-senet")]
public sealed record ReceiveInstrumentCommand : InstrumentInput, IRequest<InstrumentPostResult> {
    /// <summary>Alındığında üzerinde zaten bulunan ciro sayısı.</summary>
    public int EndorsementCount { get; init; }
}

public sealed class ReceiveInstrumentCommandValidator : AbstractValidator<ReceiveInstrumentCommand> {
    public ReceiveInstrumentCommandValidator() {
        this.AddRules();
        RuleFor(c => c.EndorsementCount).InclusiveBetween(0, 50).WithMessage("Ciro sayısı 0 ile 50 arasında olmalıdır.");
        RuleFor(c => c.BankName).NotEmpty().When(c => c.Kind == InstrumentKind.Check).WithMessage("Çek için banka adı zorunludur.");
        RuleFor(c => c.Drawer).NotEmpty().WithMessage("Keşideci/borçlu adı zorunludur.");
    }
}

public sealed class ReceiveInstrumentCommandHandler(InstrumentPoster poster) : IRequestHandler<ReceiveInstrumentCommand, InstrumentPostResult> {
    public Task<InstrumentPostResult> Handle(ReceiveInstrumentCommand request, CancellationToken ct) {
        return poster.CreateAsync(InstrumentDirection.Received, request, null, request.EndorsementCount, ct);
    }
}
