using ERP.Application.Common.Security;

namespace ERP.Application.Pos.Returns;

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Return)]
public sealed record CreateReturnCommand(ReturnInput Input) : IRequest<ReturnResultDto>;

public sealed class CreateReturnCommandValidator : AbstractValidator<CreateReturnCommand> {
    public CreateReturnCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.SaleNumber).NotEmpty().WithMessage("Fiş numarası zorunludur.");
        RuleFor(c => c.Input.Lines).NotEmpty().WithMessage("İade için en az bir satır seçilmelidir.");
        RuleFor(c => c.Input.Reason).MaximumLength(500).WithMessage("İade nedeni en fazla 500 karakter olabilir.");
    }
}

public sealed class CreateReturnCommandHandler(ReturnPoster poster) : IRequestHandler<CreateReturnCommand, ReturnResultDto> {
    public Task<ReturnResultDto> Handle(CreateReturnCommand request, CancellationToken ct) {
        return poster.CreateAsync(request.Input, ct);
    }
}
