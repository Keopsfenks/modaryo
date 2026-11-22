namespace IdentityService.Domain.Constants;

/// <summary>Üyelik türleri: çalışan, bayi, self servis.</summary>
public static class MembershipKinds
{
    public const string Employee = "employee";
    public const string Dealer = "dealer";
    public const string SelfService = "self_service";

    public static bool IsKnown(string kind) => kind is Employee or Dealer or SelfService;
}
