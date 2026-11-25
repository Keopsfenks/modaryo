using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;

namespace ERP.Application.Treasury.Journal.Commands;

/// <summary>
///     İç API (hr, assets) mahsup fişi. Kullanıcı oturumu yoktur ([Authorize] aranmaz): çağıran imzalı servis olmalıdır, eylemci "sistem:{servis}"
///     olur. İdempotency kaydı fişle aynı veritabanı işleminde tamamlanır.
/// </summary>
public sealed record PostInternalVoucherCommand : IRequest<VoucherResult> {
    public DateOnly Date { get; init; }

    public string? Description { get; init; }

    public string? SourceType { get; init; }

    public Guid? SourceId { get; init; }

    public List<JournalLineInput> Lines { get; init; } = [];
}

public sealed class PostInternalVoucherCommandValidator : AbstractValidator<PostInternalVoucherCommand> {
    public PostInternalVoucherCommandValidator() {
        RuleFor(c => c.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        RuleFor(c => c.Description).NotEmpty().WithMessage("Açıklama zorunludur.").MaximumLength(500).WithMessage("Açıklama en çok 500 karakter olabilir.");
        RuleFor(c => c.SourceType).NotEmpty().WithMessage("Kaynak türü zorunludur.").MaximumLength(50).WithMessage("Kaynak türü en çok 50 karakter olabilir.");
        RuleFor(c => c.SourceId).NotNull().NotEqual(Guid.Empty).WithMessage("Kaynak kimliği zorunludur.");
        this.AddLineRules(c => c.Lines);
    }
}

public sealed class PostInternalVoucherCommandHandler(
    IApplicationDbContext db,
    JournalVoucherPoster  poster,
    IInternalRequest      request
) : IRequestHandler<PostInternalVoucherCommand, VoucherResult> {
    public async Task<VoucherResult> Handle(PostInternalVoucherCommand command, CancellationToken ct) {
        var service = request.Service ?? throw new ForbiddenAccessException();

        await using var tx = await db.BeginTransactionAsync(ct);
        var result = await poster.PostAsync(new VoucherInput(command.Date, command.Description!, command.SourceType!.Trim(), command.SourceId, service, command.Lines), ct);
        // Yanıt (201) idempotency kaydına fişle aynı işlemde yazılır; kayıt devredilmişse fiş de geri alınır.
        if (!await request.CompleteIdempotencyAsync(201, result, ct)) throw TreasuryErrors.IdempotencyLost();

        await tx.CommitAsync(ct);
        return result;
    }
}
