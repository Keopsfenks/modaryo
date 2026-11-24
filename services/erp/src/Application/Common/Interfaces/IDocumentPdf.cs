namespace ERP.Application.Common.Interfaces;

/// <summary>Belge PDF'i üretir. Yetki denetimi çağıranındır; hizmet yalnızca aktif firmanın faturasını okur.</summary>
public interface IDocumentPdf {
    /// <summary>Faturanın A4 PDF'i. Taslak ise numarasız ve "TASLAK" filigranlıdır; iptalde "İPTAL" filigranı vardır.</summary>
    Task<byte[]> RenderInvoiceAsync(Guid invoiceId, CancellationToken ct);
}
