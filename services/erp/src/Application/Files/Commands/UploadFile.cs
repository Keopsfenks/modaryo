using System.Security.Cryptography;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Files;
using FluentValidation.Results;

namespace ERP.Application.Files.Commands;

public static class FileKinds {
    public const string Logo       = "logo";
    public const string Attachment = "attachment";
    public const string Image      = "image";

    public const long MaxBytes     = 10 * 1024 * 1024;
    public const long MaxLogoBytes = 2 * 1024 * 1024;

    public static bool IsValid(string? kind) {
        return kind is Logo or Attachment or Image;
    }

    public static long LimitFor(string kind) {
        return kind == Logo ? MaxLogoBytes : MaxBytes;
    }
}

/// <summary>Content ham dosya akışıdır; Length biliniyorsa baştan boyut denetimi yapılır.</summary>
[Authorize]
public sealed record UploadFileCommand(
    string Kind,
    string FileName,
    Stream Content,
    long?  Length
) : IRequest<UploadedFileDto>;

public sealed record UploadedFileDto(Guid Id, string FileName, string ContentType, long Size);

public sealed class UploadFileCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ICurrentTenant        tenant,
    IFileStorage          storage,
    IFileSniffer          sniffer,
    TimeProvider          time
) : IRequestHandler<UploadFileCommand, UploadedFileDto> {
    public const int MaxFileNameLength = 255;

    public async Task<UploadedFileDto> Handle(UploadFileCommand request, CancellationToken ct) {
        if (!FileKinds.IsValid(request.Kind))
            throw new Common.Exceptions.ValidationException([
                new ValidationFailure("kind", "Dosya türü logo, attachment veya image olmalıdır.")
            ]);

        if (session.TenantId is not { } tenantId) throw new ForbiddenAccessException();
        if (request.Kind == FileKinds.Logo && !session.HasPermission(SettingsPermissions.Company))
            throw new ForbiddenAccessException();

        var limit = FileKinds.LimitFor(request.Kind);
        if (request.Length > limit) throw TooLarge(limit);

        // Tür, dosyanın içeriğinden belirlenir; istemcinin başlığına ve dosya adına güvenilmez.
        var head = new byte[16];
        var read = await ReadFullyAsync(request.Content, head, ct);
        var type = sniffer.Sniff(head.AsSpan(0, read));
        if (type is null || (request.Kind != FileKinds.Attachment && !type.IsImage))
            throw new AppException(415, "file_type_not_allowed",
                                   request.Kind == FileKinds.Attachment
                                       ? "Yalnızca PNG, JPEG, WEBP görselleri ve PDF dosyaları yüklenebilir."
                                       : "Yalnızca PNG, JPEG veya WEBP görselleri yüklenebilir.");

        // En fazla 10 MB olduğundan bellekte tamponlanır; okurken boyut sınırı ve SHA-256 birlikte uygulanır.
        using var buffer = new MemoryStream();
        buffer.Write(head, 0, read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(head, 0, read);

        var chunk = new byte[81920];
        int n;
        while ((n = await request.Content.ReadAsync(chunk, ct)) > 0) {
            if (buffer.Length + n > limit) throw TooLarge(limit);

            buffer.Write(chunk, 0, n);
            hash.AppendData(chunk, 0, n);
        }

        buffer.Position = 0;
        var key = storage.BuildKey(tenantId, request.Kind, type.Extension);
        await storage.PutAsync(key, buffer, type.ContentType, ct);

        var file = new StoredFile {
                                      Kind        = request.Kind,
                                      FileName    = SanitizeFileName(request.FileName, type.Extension),
                                      ContentType = type.ContentType,
                                      Size        = buffer.Length,
                                      ObjectKey   = key,
                                      Sha256      = Convert.ToHexStringLower(hash.GetHashAndReset()),
                                      CreatedAt   = time.GetUtcNow(),
                                      CreatedBy   = tenant.UserId
                                  };
        db.StoredFiles.Add(file);

        try {
            await db.SaveChangesAsync(ct);
        } catch {
            // Kayıtsız nesne kalmasın (en iyi çaba).
            try {
                await storage.DeleteAsync(key, CancellationToken.None);
            } catch {
                // yut: asıl hata aşağıda yeniden fırlatılır
            }

            throw;
        }

        return new UploadedFileDto(file.Id, file.FileName, file.ContentType, file.Size);
    }

    private static AppException TooLarge(long limit) {
        return new AppException(413, "file_too_large",
                                $"Dosya çok büyük; en fazla {limit / (1024 * 1024)} MB yüklenebilir.");
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct) {
        var total = 0;
        while (total < buffer.Length) {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) break;

            total += n;
        }

        return total;
    }

    /// <summary>Yol parçalarını ve denetim karakterlerini atar, 255 karaktere kısaltır; boş kalırsa varsayılan ad verir.</summary>
    public static string SanitizeFileName(string? name, string extension) {
        var cleaned = name ?? string.Empty;
        var slash   = Math.Max(cleaned.LastIndexOf('/'), cleaned.LastIndexOf('\\'));
        if (slash >= 0) cleaned = cleaned[(slash + 1)..];

        cleaned = string.Concat(cleaned.Where(c => !char.IsControl(c))).Trim();
        if (cleaned.Length > MaxFileNameLength) cleaned = cleaned[..MaxFileNameLength];
        // Kısaltma bir vekil çiftini ortadan bölmüş olabilir.
        if (cleaned.Length > 0 && char.IsHighSurrogate(cleaned[^1])) cleaned = cleaned[..^1];

        return cleaned is "" or "." or ".." ? "dosya" + extension : cleaned;
    }
}
