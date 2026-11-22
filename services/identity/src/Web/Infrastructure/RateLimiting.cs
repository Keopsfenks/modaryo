using System.Threading.RateLimiting;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace IdentityService.Web.Infrastructure;

/// <summary>
/// İstemci IP'sine göre sabit pencereli sınırlar (spec 7.5). Değerler "RateLimits:{Politika}:PermitLimit|WindowSeconds"
/// ile ezilebilir; "RateLimits:Enabled=false" hepsini kapatır (testler için).
/// </summary>
public static class RateLimiting
{
    public const string Login = "login";
    public const string Register = "register";
    public const string EmailSensitive = "email-sensitive";

    public static IServiceCollection AddIdentityRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue("RateLimits:Enabled", true);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var problem = TypedResults.Problem(
                    title: "Çok fazla deneme yaptınız. Lütfen biraz sonra tekrar deneyin.",
                    statusCode: StatusCodes.Status429TooManyRequests,
                    extensions: new Dictionary<string, object?> { ["code"] = AuthErrorCodes.RateLimited });
                await problem.ExecuteAsync(context.HttpContext);
            };

            AddPolicy(options, configuration, enabled, Login, permitLimit: 10, window: TimeSpan.FromMinutes(1));
            AddPolicy(options, configuration, enabled, Register, permitLimit: 5, window: TimeSpan.FromMinutes(10));
            AddPolicy(options, configuration, enabled, EmailSensitive, permitLimit: 5, window: TimeSpan.FromMinutes(10));
        });

        return services;
    }

    private static void AddPolicy(RateLimiterOptions options, IConfiguration configuration, bool enabled, string name, int permitLimit, TimeSpan window)
    {
        var section = configuration.GetSection($"RateLimits:{name}");
        var limit = section.GetValue("PermitLimit", permitLimit);
        var windowLength = TimeSpan.FromSeconds(section.GetValue("WindowSeconds", (int)window.TotalSeconds));

        options.AddPolicy(name, context =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return enabled
                ? RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = windowLength,
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter(ip);
        });
    }
}
