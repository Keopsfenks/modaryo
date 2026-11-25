namespace ERP.Application.Common.Interfaces;

/// <summary>Belge PDF'i üretir. Yetki denetimi çağıranındır; hizmet yalnızca aktif firmanın faturasını okur.</summary>
public interface IDocumentPdf {
    /// <summary>Faturanın A4 PDF'i. Taslak ise numarasız ve "TASLAK" filigranlıdır; iptalde "İPTAL" filigranı vardır.</summary>
    Task<byte[]> RenderInvoiceAsync(Guid invoiceId, CancellationToken ct);

    /// <summary>Belge ayarlarının canlı önizlemesi için örnek veriyle (firma bilgileri, logo ve ayarlar gerçek) üretilen örnek fatura PDF'i.</summary>
    Task<byte[]> RenderSampleAsync(CancellationToken ct);

    /// <summary>
    ///     Satın alma siparişinin A4 PDF'i (satırlar, beklenen tarihler). showPrices false ise fiyat sütunları ve toplamlar basılmaz.
    ///     Yetki ve şube denetimi çağıranındır.
    /// </summary>
    Task<byte[]> RenderPurchaseOrderAsync(Guid purchaseOrderId, bool showPrices, CancellationToken ct);
}
