using ERP.Application.Common.Interfaces;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;

namespace ERP.Application.Common.Behaviours;

/// <summary>
///     İstek adını ve kullanıcıyı loglar. İstek içeriği bilerek loglanmaz: kayıt/giriş komutları şifre taşır.
/// </summary>
public class LoggingBehaviour<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull {
    private readonly ILogger         _logger;
    private readonly ICurrentSession _session;

    public LoggingBehaviour(ILogger<TRequest> logger, ICurrentSession session) {
        _logger  = logger;
        _session = session;
    }

    public Task Process(TRequest request, CancellationToken cancellationToken) {
        _logger.LogInformation("ERP Request: {Name} {@UserId}", typeof(TRequest).Name,
                               _session.UserId?.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }
}