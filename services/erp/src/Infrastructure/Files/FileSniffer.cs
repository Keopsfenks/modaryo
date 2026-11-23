using ERP.Application.Common.Interfaces;

namespace ERP.Infrastructure.Files;

/// <summary>İçerik türünü dosyanın ilk baytlarından (sihirli baytlar) tespit eder.</summary>
public sealed class FileSniffer : IFileSniffer {
    private static readonly byte[] Png  = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf  = "%PDF-"u8.ToArray();

    public SniffedType? Sniff(ReadOnlySpan<byte> head) {
        if (head.StartsWith(Png)) return new SniffedType("image/png", ".png", true);
        if (head.StartsWith(Jpeg)) return new SniffedType("image/jpeg", ".jpg", true);
        if (head.Length >= 12 && head[..4].SequenceEqual(Riff) && head.Slice(8, 4).SequenceEqual(Webp))
            return new SniffedType("image/webp", ".webp", true);
        if (head.StartsWith(Pdf)) return new SniffedType("application/pdf", ".pdf", false);

        return null;
    }
}
