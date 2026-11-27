using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Recurring;

[Authorize(Permission = SalesPermissions.Recurring)]
public sealed record CreateRecurringCommand(RecurringInput Input) : IRequest<RecurringDto>;

public sealed class CreateRecurringCommandValidator : AbstractValidator<CreateRecurringCommand> {
    public CreateRecurringCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new RecurringInputValidator());
    }
}

public sealed class CreateRecurringCommandHandler(
    IApplicationDbContext db,
    RecurringWriter       writer,
    RecurringMapper       mapper
) : IRequestHandler<CreateRecurringCommand, RecurringDto> {
    public async Task<RecurringDto> Handle(CreateRecurringCommand request, CancellationToken ct) {
        var template = new RecurringTemplate();
        await writer.ApplyAsync(template, request.Input, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        template.Number = $"AB{await db.NextDocumentNumberAsync("AB", ct):0000}";
        db.RecurringTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(template, ct);
    }
}

[Authorize(Permission = SalesPermissions.Recurring)]
public sealed record UpdateRecurringCommand(Guid Id, RecurringInput Input) : IRequest<RecurringDto>;

public sealed class UpdateRecurringCommandValidator : AbstractValidator<UpdateRecurringCommand> {
    public UpdateRecurringCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new RecurringInputValidator());
    }
}

/// <summary>Şablon değişikliği yalnızca sonraki üretimleri etkiler; üretilmiş faturalara dokunulmaz. Üretimle çakışmamak için şablon satırı kilitlenir.</summary>
public sealed class UpdateRecurringCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    RecurringWriter       writer,
    RecurringMapper       mapper,
    TimeProvider          clock
) : IRequestHandler<UpdateRecurringCommand, RecurringDto> {
    public async Task<RecurringDto> Handle(UpdateRecurringCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockRecurringTemplateAsync(request.Id, ct)) throw new NotFoundException(request.Id.ToString(), "RecurringTemplate");

        var template = await db.RecurringTemplates.Include(t => t.Lines).FirstAsync(t => t.Id == request.Id, ct);
        BranchGuard.Ensure(tenant, template.BranchId);
        db.RecurringTemplateLines.RemoveRange(template.Lines);
        template.Lines.Clear();
        await db.SaveChangesAsync(ct);

        await writer.ApplyAsync(template, request.Input, ct);
        template.LastModified = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(template, ct);
    }
}

[Authorize(Permission = SalesPermissions.Recurring)]
public sealed record SetRecurringActiveCommand(Guid Id, bool Active) : IRequest<RecurringDto>;

public sealed class SetRecurringActiveCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    RecurringMapper       mapper,
    TimeProvider          clock
) : IRequestHandler<SetRecurringActiveCommand, RecurringDto> {
    public async Task<RecurringDto> Handle(SetRecurringActiveCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockRecurringTemplateAsync(request.Id, ct)) throw new NotFoundException(request.Id.ToString(), "RecurringTemplate");

        var template = await db.RecurringTemplates.Include(t => t.Lines).FirstAsync(t => t.Id == request.Id, ct);
        BranchGuard.Ensure(tenant, template.BranchId);
        template.IsActive     = request.Active;
        template.LastModified = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(template, ct);
    }
}
