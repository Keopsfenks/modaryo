using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Documents;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Picking;

[Authorize(Permission = ShippingPermissions.Pick)]
public sealed record CreatePickListCommand(Guid ShipmentId) : IRequest<PickListDto>;

internal sealed class CreatePickListCommandHandler(IApplicationDbContext db, PickListService service, PickListMapper mapper) : IRequestHandler<CreatePickListCommand, PickListDto> {
    public async Task<PickListDto> Handle(CreatePickListCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var list = await service.CreateAsync([request.ShipmentId], ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(list, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Pick)]
public sealed record BatchPickListCommand(BatchPickInput Input) : IRequest<PickListDto>;

internal sealed class BatchPickListCommandHandler(IApplicationDbContext db, PickListService service, PickListMapper mapper) : IRequestHandler<BatchPickListCommand, PickListDto> {
    public async Task<PickListDto> Handle(BatchPickListCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var list = await service.CreateAsync(request.Input.ShipmentIds, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(list, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Pick)]
public sealed record UpdatePickLineCommand(Guid ListId, Guid LineId, PickLineInput Input) : IRequest<PickListDto>;

public sealed class UpdatePickLineCommandValidator : AbstractValidator<UpdatePickLineCommand> {
    public UpdatePickLineCommandValidator() {
        RuleFor(c => c.Input).NotNull();
    }
}

internal sealed class UpdatePickLineCommandHandler(IApplicationDbContext db, PickListService service, PickListMapper mapper) : IRequestHandler<UpdatePickLineCommand, PickListDto> {
    public async Task<PickListDto> Handle(UpdatePickLineCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var list = await service.UpdateLineAsync(request.ListId, request.LineId, request.Input, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(list, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Pick)]
public sealed record CompletePickListCommand(Guid ListId, bool Force) : IRequest<PickListDto>;

internal sealed class CompletePickListCommandHandler(IApplicationDbContext db, PickListService service, PickListMapper mapper) : IRequestHandler<CompletePickListCommand, PickListDto> {
    public async Task<PickListDto> Handle(CompletePickListCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var list = await service.CompleteAsync(request.ListId, request.Force, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(list, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Pick)]
public sealed record CancelPickListCommand(Guid ListId) : IRequest<PickListDto>;

internal sealed class CancelPickListCommandHandler(IApplicationDbContext db, PickListService service, PickListMapper mapper) : IRequestHandler<CancelPickListCommand, PickListDto> {
    public async Task<PickListDto> Handle(CancelPickListCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var list = await service.CancelAsync(request.ListId, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(list, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Read)]
public sealed record GetPickListQuery(Guid Id) : IRequest<PickListDto>;

public sealed class GetPickListQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, PickListMapper mapper) : IRequestHandler<GetPickListQuery, PickListDto> {
    public async Task<PickListDto> Handle(GetPickListQuery request, CancellationToken ct) {
        var list = await db.PickLists.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "PickList");
        BranchGuard.Ensure(tenant, list.BranchId);
        return await mapper.ToDtoAsync(list, ct);
    }
}

public sealed record PickListRowDto(Guid Id, string Number, PickListStatus Status, int ShipmentCount, int LineCount, DateTimeOffset Created);

[Authorize(Permission = ShippingPermissions.Read)]
public sealed record ListPickListsQuery(PickListStatus? Status) : IRequest<IReadOnlyList<PickListRowDto>>;

public sealed class ListPickListsQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<ListPickListsQuery, IReadOnlyList<PickListRowDto>> {
    public async Task<IReadOnlyList<PickListRowDto>> Handle(ListPickListsQuery request, CancellationToken ct) {
        var query = db.PickLists.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(p => branches.Contains(p.BranchId));
        }

        if (request.Status is { } status) query = query.Where(p => p.Status == status);
        var rows = await query.OrderByDescending(p => p.Created).Take(200).Select(p => new { p.Id, p.Number, p.Status, p.ShipmentIds, Lines = p.Lines.Count, p.Created }).ToListAsync(ct);
        return rows.Select(r => new PickListRowDto(r.Id, r.Number, r.Status, r.ShipmentIds.Count, r.Lines, r.Created)).ToList();
    }
}

public sealed record PickListPdfFile(byte[] Content, string FileName);

[Authorize(Permission = ShippingPermissions.Read)]
public sealed record GetPickListPdfQuery(Guid Id) : IRequest<PickListPdfFile>;

public sealed class GetPickListPdfQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, IDocumentPdf pdf) : IRequestHandler<GetPickListPdfQuery, PickListPdfFile> {
    public async Task<PickListPdfFile> Handle(GetPickListPdfQuery request, CancellationToken ct) {
        var head = await db.PickLists.AsNoTracking().Where(p => p.Id == request.Id).Select(p => new { p.Number, p.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "PickList");
        BranchGuard.Ensure(tenant, head.BranchId);
        return new PickListPdfFile(await pdf.RenderPickListAsync(request.Id, ct), InvoiceMail.FileName(head.Number, request.Id));
    }
}
