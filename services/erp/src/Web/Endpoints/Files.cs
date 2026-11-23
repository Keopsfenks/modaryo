using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Files.Commands;
using ERP.Application.Files.Queries;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Files : IEndpointGroup {
    /// <summary>En büyük dosya (10 MB) + multipart zarfı payı: kendi 413 yanıtımız Kestrel'inkinden önce çalışsın.</summary>
    private const long MaxRequestBytes = FileKinds.MaxBytes + 1024 * 1024;

    public static string? RoutePrefix => "/api/erp/files";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapPost(UploadFile).DisableAntiforgery(); // CSRF koruması X-Modaryo-Csrf başlığıyla yapılır
        groupBuilder.MapGet(DownloadFile, "{id:guid}");
    }

    [EndpointSummary("Dosya yükle")]
    [EndpointDescription("multipart/form-data, alan adı \"file\"; kind: logo (en fazla 2 MB, ayarlar.firma gerekir) | attachment (PNG, JPEG, WEBP, PDF) | image (PNG, JPEG, WEBP). Diğer türler için firma üyeliği yeterlidir. Genel sınır 10 MB (413 file_too_large). İçerik türü dosyanın içeriğinden belirlenir; izin verilmeyen tür 415 file_type_not_allowed, bilinmeyen kind 400.")]
    public static async Task<Created<UploadedFileDto>> UploadFile(
        HttpContext     context,
        ISender         sender,
        ICurrentSession session,
        string?         kind
    ) {
        if (session.UserId is null) throw new UnauthorizedAccessException();

        var request = context.Request;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = MaxRequestBytes;

        if (request.ContentLength > MaxRequestBytes) throw TooLarge();
        if (!request.HasFormContentType)
            throw new AppException(400, "file_required", "Dosya multipart/form-data olarak, \"file\" alanında gönderilmelidir.");

        IFormCollection form;
        try {
            form = await request.ReadFormAsync(context.RequestAborted);
        } catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) {
            throw TooLarge();
        } catch (InvalidDataException) {
            throw new AppException(400, "file_required", "Dosya okunamadı.");
        }

        var file = form.Files.GetFile("file")
                   ?? throw new AppException(400, "file_required", "\"file\" alanında bir dosya gönderilmelidir.");

        await using var content = file.OpenReadStream();
        var result = await sender.Send(new UploadFileCommand(kind ?? string.Empty, file.FileName, content, file.Length));
        return TypedResults.Created($"/api/erp/files/{result.Id}", result);
    }

    [EndpointSummary("Dosyayı indir")]
    [EndpointDescription("Firmanın oturum sahibi herhangi bir üyesi indirebilir; başka firmanın dosyası ve olmayan dosya 404. Görsel ve PDF'ler inline sunulur; X-Content-Type-Options: nosniff ve Content-Security-Policy: sandbox eklenir.")]
    public static async Task<IResult> DownloadFile(HttpContext context, ISender sender, Guid id) {
        var file = await sender.Send(new GetFileQuery(id));

        context.Response.Headers.XContentTypeOptions  = "nosniff";
        context.Response.Headers.CacheControl          = "private, no-cache";
        context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        context.Response.Headers.ContentDisposition    = ContentDisposition(file.FileName);
        context.Response.RegisterForDispose(file.Content);

        return TypedResults.Stream(file.Content, file.ContentType);
    }

    private static AppException TooLarge() {
        return new AppException(413, "file_too_large", "Dosya çok büyük; en fazla 10 MB yüklenebilir.");
    }

    /// <summary>inline; ASCII olmayan adlarda RFC 5987 filename* eklenir.</summary>
    internal static string ContentDisposition(string fileName) {
        var fallback = string.Concat(fileName.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' and not '%' ? c : '_'));
        return $"inline; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
