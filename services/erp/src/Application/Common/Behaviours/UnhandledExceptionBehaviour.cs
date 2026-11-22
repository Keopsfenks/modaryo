using Microsoft.Extensions.Logging;

namespace ERP.Application.Common.Behaviours;

public class UnhandledExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;

    public UnhandledExceptionBehaviour(ILogger<TRequest> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;

            // İstek içeriği bilerek loglanmaz: kayıt/giriş komutları şifre taşır.
            _logger.LogError(ex, "ERP Request: Unhandled Exception for Request {Name}", requestName);

            throw;
        }
    }
}
