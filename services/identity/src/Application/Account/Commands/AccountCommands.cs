using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using FluentValidationFailure = FluentValidation.Results.ValidationFailure;
using AppValidationException = IdentityService.Application.Common.Exceptions.ValidationException;

namespace IdentityService.Application.Account.Commands;

/// <summary>Şifre değiştirir; bu oturum dışındaki tüm oturumlar kapanır.</summary>
[Authorize]
public record ChangePasswordCommand : IRequest
{
    public string CurrentPassword { get; init; } = string.Empty;

    public string NewPassword { get; init; } = string.Empty;
}

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().WithMessage("Mevcut şifre gereklidir.");
        RuleFor(c => c.NewPassword).ValidNewPassword();
    }
}

public class ChangePasswordCommandHandler(IUserAccounts users, ISessionStore sessions, ICurrentSession current)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var userId = current.UserId!.Value;

        if (!await users.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword))
        {
            throw new AppValidationException([new FluentValidationFailure(nameof(request.CurrentPassword), "Mevcut şifre hatalı.")]);
        }

        await sessions.DeleteAllForUserAsync(userId, current.SidHash, ct);
    }
}

[Authorize]
public record UpdateProfileCommand : IRequest
{
    public string FullName { get; init; } = string.Empty;
}

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(c => c.FullName).ValidFullName();
    }
}

/// <summary>
/// Adı günceller. Açık oturum kayıtlarındaki full_name değişmez; GET /auth/session adı veritabanından okur,
/// diğer servisler yeni girişte güncel adı görür (bilinçli sadeleştirme).
/// </summary>
public class UpdateProfileCommandHandler(IUserAccounts users, ICurrentSession current) : IRequestHandler<UpdateProfileCommand>
{
    public Task Handle(UpdateProfileCommand request, CancellationToken ct) =>
        users.UpdateProfileAsync(current.UserId!.Value, request.FullName);
}

/// <summary>Kullanıcının kendi oturumlarından birini kapatır.</summary>
[Authorize]
public record RevokeSessionCommand(string Id) : IRequest;

public class RevokeSessionCommandHandler(ISessionStore sessions, ICurrentSession current) : IRequestHandler<RevokeSessionCommand>
{
    public async Task Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        if (!await sessions.DeleteByPublicIdAsync(current.UserId!.Value, request.Id, ct))
        {
            throw new NotFoundException(request.Id, "Session");
        }
    }
}
