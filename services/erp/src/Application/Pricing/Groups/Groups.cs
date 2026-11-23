using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Groups;

public abstract record CustomerGroupInput {
    public string Name            { get; init; } = string.Empty;
    public Guid?  SalePriceListId { get; init; }
}

public static class CustomerGroupRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : CustomerGroupInput {
        v.RuleFor(c => c.Name).NotEmpty().WithMessage("Grup adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Grup adı en fazla 100 karakter olabilir.");
    }

    public static AppException NameTaken() {
        return new AppException(409, "name_taken", "Bu adlı bir müşteri grubu zaten var.");
    }

    /// <summary>Grubun listesi, aktif firmanın satış listesi olmalıdır.</summary>
    public static async Task EnsureListAsync(IApplicationDbContext db, Guid? listId, CancellationToken ct) {
        if (listId is not { } id) return;

        if (!await db.PriceLists.AnyAsync(l => l.Id == id && l.Kind == PriceListKind.Sale, ct))
            throw new AppException(400, "invalid_list", "Grup için geçerli bir satış listesi seçin.");
    }
}

[Authorize(Permission = PricingPermissions.Read)]
public sealed record ListCustomerGroupsQuery : IRequest<IReadOnlyList<CustomerGroupDto>>;

public sealed class ListCustomerGroupsQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<ListCustomerGroupsQuery, IReadOnlyList<CustomerGroupDto>> {
    public async Task<IReadOnlyList<CustomerGroupDto>> Handle(ListCustomerGroupsQuery request, CancellationToken ct) {
        var counts = await db.CurrentAccounts.AsNoTracking().Where(a => a.CustomerGroupId != null)
                         .GroupBy(a => a.CustomerGroupId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
                         .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var groups = await db.CustomerGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        return groups.Select(g => new CustomerGroupDto(g.Id, g.Name, g.SalePriceListId, counts.GetValueOrDefault(g.Id))).ToList();
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record CreateCustomerGroupCommand : CustomerGroupInput, IRequest<CustomerGroupDto>;

public sealed class CreateCustomerGroupCommandValidator : AbstractValidator<CreateCustomerGroupCommand> {
    public CreateCustomerGroupCommandValidator() {
        this.AddRules();
    }
}

public sealed class CreateCustomerGroupCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<CreateCustomerGroupCommand, CustomerGroupDto> {
    public async Task<CustomerGroupDto> Handle(CreateCustomerGroupCommand request, CancellationToken ct) {
        var name = request.Name.Trim();
        if (await db.CustomerGroups.AnyAsync(g => g.Name == name, ct)) throw CustomerGroupRules.NameTaken();

        await CustomerGroupRules.EnsureListAsync(db, request.SalePriceListId, ct);
        var group = new CustomerGroup { Name = name, SalePriceListId = request.SalePriceListId };
        db.CustomerGroups.Add(group);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (PricingRules.IsUniqueViolation(ex)) {
            throw CustomerGroupRules.NameTaken();
        }

        return new CustomerGroupDto(group.Id, group.Name, group.SalePriceListId, 0);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record UpdateCustomerGroupCommand : CustomerGroupInput, IRequest<CustomerGroupDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateCustomerGroupCommandValidator : AbstractValidator<UpdateCustomerGroupCommand> {
    public UpdateCustomerGroupCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateCustomerGroupCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<UpdateCustomerGroupCommand, CustomerGroupDto> {
    public async Task<CustomerGroupDto> Handle(UpdateCustomerGroupCommand request, CancellationToken ct) {
        var group = await db.CustomerGroups.FirstOrDefaultAsync(g => g.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "CustomerGroup");
        var name = request.Name.Trim();
        if (await db.CustomerGroups.AnyAsync(g => g.Id != group.Id && g.Name == name, ct)) throw CustomerGroupRules.NameTaken();

        await CustomerGroupRules.EnsureListAsync(db, request.SalePriceListId, ct);
        group.Name            = name;
        group.SalePriceListId = request.SalePriceListId;
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (PricingRules.IsUniqueViolation(ex)) {
            throw CustomerGroupRules.NameTaken();
        }

        var count = await db.CurrentAccounts.CountAsync(a => a.CustomerGroupId == group.Id, ct);
        return new CustomerGroupDto(group.Id, group.Name, group.SalePriceListId, count);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record DeleteCustomerGroupCommand(Guid Id) : IRequest;

public sealed class DeleteCustomerGroupCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<DeleteCustomerGroupCommand> {
    public async Task Handle(DeleteCustomerGroupCommand request, CancellationToken ct) {
        var group = await db.CustomerGroups.FirstOrDefaultAsync(g => g.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "CustomerGroup");
        if (await db.CurrentAccounts.AnyAsync(a => a.CustomerGroupId == group.Id, ct))
            throw new AppException(409, "group_in_use", "Müşteri bağlı grup silinemez. Önce carilerin grubunu değiştirin.");

        db.CustomerGroups.Remove(group);
        await db.SaveChangesAsync(ct);
    }
}
