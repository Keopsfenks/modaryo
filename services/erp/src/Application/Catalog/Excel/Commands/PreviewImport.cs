using System.Text.Json;
using ERP.Application.Catalog.Products;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Excel.Commands;

/// <summary>Önizlemede saklanan içerik: ham satırlar ve her satır için önizlemedeki eylem.</summary>
public sealed record StoredBatch(bool AutoCode, List<ImportRow> Rows, List<string> Actions);

public static class ImportBatches {
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public static ImportPreview ToPreview(Guid batchId, IReadOnlyList<ImportRowResult> results) {
        return new ImportPreview(batchId, results.Count, results.Count(r => r.Action == "create"),
                                 results.Count(r => r.Action == "update"), results.Count(r => r.Action == "error"),
                                 results);
    }

    public static StoredBatch Read(ImportBatch batch) {
        return JsonSerializer.Deserialize<StoredBatch>(batch.RowsJson, SettingsRegistry.Json)!;
    }

    /// <summary>Yeni bir önizleme kaydı ekler (kaydetmez).</summary>
    public static ImportBatch Create(IApplicationDbContext          db,
                                     ICurrentSession                session,
                                     TimeProvider                   time,
                                     bool                           autoCode,
                                     IReadOnlyList<ImportRow>       rows,
                                     IReadOnlyList<ImportRowResult> results
    ) {
        var now = time.GetUtcNow();
        var batch = new ImportBatch {
            Kind = "products",
            RowsJson = JsonSerializer.Serialize(new StoredBatch(autoCode, rows.ToList(),
                                                                results.Select(r => r.Action).ToList()),
                                                SettingsRegistry.Json),
            CreatedAt = now,
            CreatedBy = session.UserId ?? throw new UnauthorizedAccessException(),
            ExpiresAt = now + Lifetime
        };
        db.ImportBatches.Add(batch);
        return batch;
    }
}

/// <summary>Dosyayı okur ve satırları doğrular; hiçbir kart, kategori veya marka yazmaz.</summary>
[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record PreviewImportCommand(byte[] Content, bool AutoCode) : IRequest<ImportPreview>;

public sealed class PreviewImportCommandHandler(
    IApplicationDbContext db,
    IXlsxService          xlsx,
    ImportEngine          engine,
    ICurrentSession       session,
    TimeProvider          time
) : IRequestHandler<PreviewImportCommand, ImportPreview> {
    public async Task<ImportPreview> Handle(PreviewImportCommand request, CancellationToken ct) {
        if (request.Content.Length > ProductSheet.MaxBytes)
            throw new AppException(413, "file_too_large", "Dosya çok büyük; en fazla 5 MB yüklenebilir.");

        XlsxTable table;
        try {
            using var stream = new MemoryStream(request.Content);
            table = xlsx.Read(stream, ProductSheet.MaxRows);
        } catch (InvalidDataException) {
            throw new AppException(400, "invalid_file", "Dosya okunamadı. Geçerli bir .xlsx dosyası yükleyin.");
        }

        if (table.Rows.Count > ProductSheet.MaxRows)
            throw new AppException(400, "too_many_rows", "Bir seferde en fazla 5.000 satır içe aktarılabilir.");

        var rows = ProductSheet.ToRows(table);
        if (rows.Count == 0) throw new AppException(400, "empty_file", "Dosyada içe aktarılacak satır yok.");

        IReadOnlyList<ImportRowResult> results;
        await using (var transaction = await db.BeginTransactionAsync(ct)) {
            try {
                results = await engine.ApplyAsync(rows, request.AutoCode, ct);
            } catch (DbUpdateException ex) when (CatalogErrors.FromUniqueViolation(ex) is { } conflict) {
                throw conflict;
            }

            await transaction.RollbackAsync(ct); // önizleme hiçbir şey yazmaz
        }

        db.ClearTracking();

        var stale = await db.ImportBatches.Where(b => b.ExpiresAt < time.GetUtcNow().AddDays(-1)).ToListAsync(ct);
        db.ImportBatches.RemoveRange(stale);
        var batch = ImportBatches.Create(db, session, time, request.AutoCode, rows, results);
        await db.SaveChangesAsync(ct);

        return ImportBatches.ToPreview(batch.Id, results);
    }
}
