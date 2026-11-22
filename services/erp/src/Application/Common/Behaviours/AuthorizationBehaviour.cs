using System.Reflection;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Common.Behaviours;

/// <summary>
/// [Authorize] taşıyan isteklerde: oturum yoksa 401; izin istenmiş ve aktif firma yoksa ya da izin yoksa 403.
/// </summary>
public class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentSession _session;

    public AuthorizationBehaviour(ICurrentSession session)
    {
        _session = session;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attributes = request.GetType().GetCustomAttributes<AuthorizeAttribute>().ToArray();

        if (attributes.Length > 0)
        {
            if (_session.UserId is null)
            {
                throw new UnauthorizedAccessException();
            }

            foreach (var permission in attributes.Select(a => a.Permission).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (_session.TenantId is null || !_session.HasPermission(permission))
                {
                    throw new ForbiddenAccessException();
                }
            }
        }

        return await next();
    }
}
