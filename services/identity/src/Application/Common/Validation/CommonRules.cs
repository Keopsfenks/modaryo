namespace IdentityService.Application.Common.Validation;

/// <summary>Kimlik formlarında ortak doğrulama kuralları (Türkçe mesajlarla).</summary>
public static class CommonRules
{
    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("E-posta adresi gereklidir.")
            .Must(e => e is null || e.Trim().Length <= 256).WithMessage("E-posta adresi en fazla 256 karakter olabilir.")
            .Must(e => e is null || IsEmail(e.Trim())).WithMessage("Geçerli bir e-posta adresi girin.");

    public static IRuleBuilderOptions<T, string> ValidNewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Şifre gereklidir.")
            .MinimumLength(10).WithMessage("Şifre en az 10 karakter olmalıdır.")
            .MaximumLength(128).WithMessage("Şifre en fazla 128 karakter olabilir.");

    public static IRuleBuilderOptions<T, string> ValidFullName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(n => n is not null && n.Trim().Length >= 2).WithMessage("Ad soyad en az 2 karakter olmalıdır.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Ad soyad en fazla 100 karakter olabilir.");

    public static IRuleBuilderOptions<T, string> ValidCompanyName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(n => n is not null && n.Trim().Length >= 2).WithMessage("Firma adı en az 2 karakter olmalıdır.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Firma adı en fazla 100 karakter olabilir.");

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 3
            && value.IndexOf('.', at) > at + 1
            && !value.EndsWith('.')
            && !value.Any(char.IsWhiteSpace);
    }
}
