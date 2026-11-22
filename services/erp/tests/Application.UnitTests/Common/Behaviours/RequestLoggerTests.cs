using ERP.Application.Common.Behaviours;
using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace ERP.Application.UnitTests.Common.Behaviours;

public class RequestLoggerTests
{
    public sealed record SensitiveRequest(string Email, string Password);

    [Test]
    public async Task Process_NeverLogsRequestPayload()
    {
        var logger = new Mock<ILogger<SensitiveRequest>>();
        var user = new Mock<ICurrentSession>();
        var behaviour = new LoggingBehaviour<SensitiveRequest>(logger.Object, user.Object);

        await behaviour.Process(new SensitiveRequest("ali@ornek.com", "cok-gizli-sifre"), CancellationToken.None);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => LogsNameButNotPayload(state)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static bool LogsNameButNotPayload(object? state)
    {
        var text = state?.ToString() ?? string.Empty;
        var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];

        return text.Contains(nameof(SensitiveRequest))
            && !text.Contains("cok-gizli-sifre")
            && !values.Any(kv => kv.Value is SensitiveRequest);
    }
}
