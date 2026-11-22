namespace IdentityService.Application.Common.Exceptions;

/// <summary>Belirli bir HTTP durumu ve makinece okunur kodla dönen iş kuralı hatası.</summary>
public sealed class AuthException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}
