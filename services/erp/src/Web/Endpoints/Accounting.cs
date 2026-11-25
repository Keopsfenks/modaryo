using ERP.Application.Accounting.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Accounting : IEndpointGroup {
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static string? RoutePrefix => "/api/erp/accounting";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ExportJournal, "journal.xlsx");
    }

    [EndpointSummary("Yevmiye aktarımı (Excel)")]
    [EndpointDescription("muhasebe.export. from/to (dahil, en çok 1 yıl). Satır başına bir fiş satırı: Fiş no, Tarih, Hesap kodu, Açıklama, Borç, Alacak, Belge türü, Belge no. Fatura, tahsilat/ödeme, virman, masraf/gelir, çek/senet ve mahsup fişleri; iptal edilenler için ters fiş. Hesap kodları finans ayarlarındaki eşlemeden gelir.")]
    public static async Task<FileContentHttpResult> ExportJournal(ISender sender, DateOnly from, DateOnly to) {
        return TypedResults.File(await sender.Send(new ExportJournalQuery(from, to)), XlsxType, $"yevmiye-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }
}
