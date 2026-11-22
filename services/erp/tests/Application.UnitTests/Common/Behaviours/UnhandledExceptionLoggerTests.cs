using ERP.Application.Common.Behaviours;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace ERP.Application.UnitTests.Common.Behaviours;

public class UnhandledExceptionLoggerTests {
    [Test]
    public async Task Handle_OnException_LogsNameButNotPayload() {
        var logger    = new Mock<ILogger<SensitiveRequest>>();
        var behaviour = new UnhandledExceptionBehaviour<SensitiveRequest, int>(logger.Object);

        await Should.ThrowAsync<InvalidOperationException>(() =>
                                                               behaviour.Handle(new SensitiveRequest("ali@ornek.com",
                                                                                        "cok-gizli-sifre"),
                                                                                    _ =>
                                                                                        throw new
                                                                                            InvalidOperationException(),
                                                                                    CancellationToken.None));

        logger.Verify(l => l.Log(
                                 LogLevel.Error,
                                 It.IsAny<EventId>(),
                                 It.Is<It.IsAnyType>((state, _) => LogsNameButNotPayload(state)),
                                 It.IsAny<Exception?>(),
                                 It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static bool LogsNameButNotPayload(object? state) {
        var text   = state?.ToString()                                   ?? string.Empty;
        var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];

        return text.Contains(nameof(SensitiveRequest))
               && !text.Contains("cok-gizli-sifre")
               && !values.Any(kv => kv.Value is SensitiveRequest);
    }

    public sealed record SensitiveRequest(
        string Email,
        string Password
    ) : IRequest<int>;
}