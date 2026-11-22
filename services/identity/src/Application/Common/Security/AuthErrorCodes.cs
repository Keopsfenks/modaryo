namespace IdentityService.Application.Common.Security;

/// <summary>Hata yanıtlarındaki makinece okunur "code" değerleri.</summary>
public static class AuthErrorCodes
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string EmailNotConfirmed = "email_not_confirmed";
    public const string AccountLocked = "account_locked";
    public const string InvalidToken = "invalid_token";
    public const string LastOwner = "last_owner";
    public const string InvitationEmailMismatch = "invitation_email_mismatch";
    public const string AlreadyMember = "already_member";
    public const string AccountExists = "account_exists";
    public const string RateLimited = "rate_limited";
    public const string UnknownModule = "unknown_module";
}
