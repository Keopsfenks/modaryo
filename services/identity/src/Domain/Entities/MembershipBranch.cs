namespace IdentityService.Domain.Entities;

/// <summary>Üyenin rol kapsamını daraltan şube seçimi.</summary>
public class MembershipBranch
{
    public Guid MembershipId { get; set; }

    public Guid BranchId { get; set; }
}
