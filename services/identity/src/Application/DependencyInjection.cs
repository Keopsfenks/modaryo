using System.Reflection;
using IdentityService.Application.Auth;
using IdentityService.Application.Common.Behaviours;
using IdentityService.Application.Companies;
using IdentityService.Application.Invitations;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAutoMapper(cfg => 
            cfg.AddMaps(Assembly.GetExecutingAssembly()));

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        builder.Services.AddScoped<ActiveTenantResolver>();
        builder.Services.AddScoped<SessionIssuer>();
        builder.Services.AddScoped<MembershipChanges>();
        builder.Services.AddScoped<CompanyAccess>();
        builder.Services.AddScoped<IdentityService.Application.Roles.RoleRules>();
        builder.Services.AddScoped<IdentityService.Application.Branches.BranchRules>();
        builder.Services.AddScoped<InvitationService>();

        builder.Services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
        });
    }
}
