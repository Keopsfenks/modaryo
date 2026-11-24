using ERP.Application.Catalog.Products;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Catalog.Excel.Commands;

/// <summary>Done doluysa uygulandı; Conflict doluysa hiçbir şey yazılmadı ve taze önizleme döndü.</summary>
public sealed record CommitImportOutcome(ImportCommitResult? Done, ImportPreview? Conflict);

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record CommitImportCommand(Guid BatchId) : IRequest<CommitImportOutcome>;

/// <summary>
///     Tek işlemde her satırı güncel veritabanına karşı yeniden doğrular ve uygular. Önizlemeden sonra herhangi bir
///     satır farklı sonuç verirse (örn. kod arada alındı) işlem geri alınır: hiçbir şey yazılmaz.
///     Aynı önizlemenin eşzamanlı ikinci uygulaması engellenir: işlemin ilk işi, önizlemeyi koşullu tek bir UPDATE ile
///     "uygulandı" diye sahiplenmektir (CommittedAt IS NULL); etkilenen satır yoksa 409 import_already_committed.
///     Bekleyen ikinci istek, birincinin onayından sonra 0 satır görür; birincinin işlemi geri alınırsa sahiplenme de
///     geri gider ve ikincisi devam edebilir.
/// </summary>
public sealed class CommitImportCommandHandler(
    IApplicationDbContext db,
    ImportEngine          engine,
    ICurrentSession       session,
    TimeProvider          time
) : IRequestHandler<CommitImportCommand, CommitImportOutcome> {
    public async Task<CommitImportOutcome> Handle(CommitImportCommand request, CancellationToken ct) {
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BatchId && b.Kind == "products", ct)
                    ?? throw new NotFoundException(request.BatchId.ToString(), "ImportBatch");

        if (batch.CommittedAt is not null)
            throw new AppException(409, "import_already_committed", "Bu içe aktarma zaten uygulandı.");
        if (batch.ExpiresAt <= time.GetUtcNow())
            throw new AppException(410, "import_expired",
                                   "Önizlemenin süresi doldu (30 dakika). Dosyayı yeniden yükleyip önizleyin.");

        var stored = ImportBatches.Read(batch);
        if (stored.Actions.Contains("error"))
            throw new AppException(400, "import_has_errors",
                                   "Önizlemede hatalı satırlar var. Dosyayı düzeltip yeniden yükleyin; hiçbir şey yazılmadı.");

        IReadOnlyList<ImportRowResult> results;
        await using (var transaction = await db.BeginTransactionAsync(ct)) {
            try {
                var now = time.GetUtcNow();
                var claimed = await db.ImportBatches
                                  .Where(b => b.Id == request.BatchId && b.CommittedAt == null && b.ExpiresAt > now)
                                  .ExecuteUpdateAsync(u => u.SetProperty(b => b.CommittedAt, now), ct);
                if (claimed == 0)
                    throw new AppException(409, "import_already_committed", "Bu içe aktarma zaten uygulandı.");

                results = await engine.ApplyAsync(stored.Rows, stored.AutoCode, ct);
                var changed = results.Where((r, i) => r.Action != stored.Actions[i]).Any();
                if (!changed) {
                    await transaction.CommitAsync(ct);
                    return new CommitImportOutcome(
                        new ImportCommitResult(results.Count(r => r.Action == "create"),
                                               results.Count(r => r.Action == "update")), null);
                }
            } catch (DbUpdateException ex) when (CatalogErrors.FromUniqueViolation(ex) is { } conflict) {
                throw conflict;
            }

            await transaction.RollbackAsync(ct);
        }

        // Çakışma: sonuçlar zaten güncel veritabanına karşı üretildi, taze önizleme olarak saklanır.
        db.ClearTracking();
        var fresh = ImportBatches.Create(db, session, time, stored.AutoCode, stored.Rows, results);
        await db.SaveChangesAsync(ct);
        return new CommitImportOutcome(null, ImportBatches.ToPreview(fresh.Id, results));
    }
}
