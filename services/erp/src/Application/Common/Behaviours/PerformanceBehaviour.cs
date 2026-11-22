using System.Diagnostics;
using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.Application.Common.Behaviours;

/// <summary>500 ms'yi aşan istekleri loglar. İstek içeriği bilerek loglanmaz (şifre taşıyabilir).</summary>
public class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;
    private readonly ICurrentSession _session;

    public PerformanceBehaviour(ILogger<TRequest> logger, ICurrentSession session)
    {
        _logger = logger;
        _session = session;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var response = await next();
        timer.Stop();

        if (timer.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning("ERP Long Running Request: {Name} ({ElapsedMilliseconds} milliseconds) {@UserId}",
                typeof(TRequest).Name, timer.ElapsedMilliseconds, _session.UserId?.ToString() ?? string.Empty);
        }

        return response;
    }
}
