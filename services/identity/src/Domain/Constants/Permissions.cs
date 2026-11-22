namespace IdentityService.Domain.Constants;

/// <summary>
/// İzin adları "modül.eylem" biçimindedir. Kod rol adına değil izne bakar.
/// </summary>
public static class Permissions
{
    /// <summary>Tüm izinleri karşılar (Sahip rolü).</summary>
    public const string All = "*";

    public const string CompanyUpdate = "company.update";
    public const string MembersRead = "members.read";
    public const string MembersInvite = "members.invite";
    public const string MembersManage = "members.manage";
}
