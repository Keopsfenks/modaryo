using IdentityService.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityService.Infrastructure.Identity;

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = IdentitySetup.EmailConfirmationProvider;
        TokenLifespan = TimeSpan.FromHours(48);
    }
}

/// <summary>E-posta doğrulama kodları için ayrı ömürlü (48 saat) sağlayıcı; şifre sıfırlama varsayılan sağlayıcıyı (1 saat) kullanır.</summary>
public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);

public static class IdentitySetup
{
    public const string EmailConfirmationProvider = "EmailConfirmation";

    /// <summary>
    /// Şifre: en az 10 karakter, tür zorunluluğu yok. Kilit: 5 hatalı deneme → 15 dk.
    /// Kod ömürleri "Tokens:EmailConfirmationLifetimeSeconds" / "Tokens:PasswordResetLifetimeSeconds" ile ezilebilir.
    /// </summary>
    public static void AddModaryoIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty; // kullanıcı adı = e-posta; doğrulaması ayrı

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationProvider;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationProvider);

        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromSeconds(configuration.GetValue("Tokens:PasswordResetLifetimeSeconds", 3600)));
        services.Configure<EmailConfirmationTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromSeconds(configuration.GetValue("Tokens:EmailConfirmationLifetimeSeconds", 48 * 3600)));
    }
}
