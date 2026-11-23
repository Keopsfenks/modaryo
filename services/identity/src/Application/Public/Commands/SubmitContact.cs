using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Validation;
using Microsoft.Extensions.Options;

namespace IdentityService.Application.Public.Commands;

/// <summary>Satış ekibinin gelen kutusu. Yapılandırma: "Site:SalesEmail".</summary>
public sealed class SiteOptions
{
    public const string Section = "Site";

    public string SalesEmail { get; set; } = "satis@modaryo.local";
}

/// <summary>
/// Herkese açık tanıtım sitesinden demo/iletişim talebi. Oturum gerektirmez.
/// <paramref name="Website"/> gizli bir tuzak alandır; dolu gelirse istek sessizce yok sayılır (bot).
/// </summary>
public record SubmitContactCommand(
    string Name,
    string? Company,
    string Email,
    string? Phone,
    string Topic,
    string? Interest,
    string? Message,
    bool KvkkConsent,
    string? Website) : IRequest;

public class SubmitContactCommandValidator : AbstractValidator<SubmitContactCommand>
{
    public SubmitContactCommandValidator()
    {
        // Tuzak dolduysa doğrulama hatası da vermeyiz: botlar neyin yakalandığını öğrenmesin.
        When(c => string.IsNullOrWhiteSpace(c.Website), () =>
        {
            RuleFor(c => c.Name).ValidFullName();
            RuleFor(c => c.Email).ValidEmail();
            RuleFor(c => c.Company).MaximumLength(100).WithMessage("Firma adı en fazla 100 karakter olabilir.");
            RuleFor(c => c.Phone).MaximumLength(20).WithMessage("Telefon en fazla 20 karakter olabilir.");
            RuleFor(c => c.Topic).Must(t => t is "demo" or "iletisim").WithMessage("Konu demo ya da iletisim olmalıdır.");
            RuleFor(c => c.Interest).MaximumLength(50).WithMessage("İlgi alanı en fazla 50 karakter olabilir.");
            RuleFor(c => c.Message).MaximumLength(2000).WithMessage("Mesaj en fazla 2000 karakter olabilir.");
            RuleFor(c => c.KvkkConsent).Equal(true).WithMessage("Devam etmek için KVKK aydınlatma metnini onaylamalısınız.");
        });
    }
}

public class SubmitContactCommandHandler(IEmailComposer composer, IEmailSender sender, IOptions<SiteOptions> options)
    : IRequestHandler<SubmitContactCommand>
{
    public async Task Handle(SubmitContactCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            return;
        }

        await sender.SendAsync(composer.ContactRequest(options.Value.SalesEmail, request), ct);
    }
}
