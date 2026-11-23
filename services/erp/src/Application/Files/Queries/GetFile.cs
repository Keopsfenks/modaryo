using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Files.Queries;

/// <summary>Firmadaki herhangi bir oturum sahibi okuyabilir; başka firmanın dosyası görünmez (404).</summary>
[Authorize]
public sealed record GetFileQuery(Guid Id) : IRequest<FileContent>;

/// <summary>Content akışını çağıran kapatır.</summary>
public sealed record FileContent(Stream Content, string ContentType, string FileName);

public sealed class GetFileQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    IFileStorage          storage
) : IRequestHandler<GetFileQuery, FileContent> {
    public async Task<FileContent> Handle(GetFileQuery request, CancellationToken ct) {
        if (session.TenantId is null) throw new ForbiddenAccessException();

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.Id, ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "File");

        try {
            return new FileContent(await storage.GetAsync(file.ObjectKey, ct), file.ContentType, file.FileName);
        } catch (FileNotFoundException) {
            throw new NotFoundException(request.Id.ToString(), "File");
        }
    }
}
