using ERP.Application.Files.Commands;
using NUnit.Framework;
using Shouldly;

namespace ERP.Application.UnitTests.Files;

public class SanitizeFileNameTests {
    private const char High = (char)0xD800;
    private const char Low  = (char)0xDC00;

    private static string Pair => "😀";

    [Test]
    public void UnpairedHighSurrogate_IsDropped() {
        UploadFileCommandHandler.SanitizeFileName($"a{High}b.png", ".png").ShouldBe("ab.png");
    }

    [Test]
    public void UnpairedLowSurrogate_IsDropped() {
        UploadFileCommandHandler.SanitizeFileName($"a{Low}b.png", ".png").ShouldBe("ab.png");
    }

    [Test]
    public void ValidPair_IsKept_AndUnpairedAroundItAreDropped() {
        UploadFileCommandHandler.SanitizeFileName($"x{Low}{Pair}{High}.png", ".png").ShouldBe($"x{Pair}.png");
    }
}
