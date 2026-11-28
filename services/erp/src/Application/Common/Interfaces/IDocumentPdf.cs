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

    /// <summary>Teklifin A4 PDF'i (revision verilmezse geçerli revizyon). Yetki ve şube denetimi çağıranındır.</summary>
    Task<byte[]> RenderQuoteAsync(Guid quoteId, int? revision, CancellationToken ct);

    /// <summary>Satış siparişinin A4 PDF'i: proforma false ise sipariş onayı, true ise proforma fatura.</summary>
    Task<byte[]> RenderSalesOrderAsync(Guid orderId, bool proforma, CancellationToken ct);

    /// <summary>Toplama listesinin A4 PDF'i (kalem, miktar, konum, önerilen lotlar ve işaret kutusu). Yetki ve şube denetimi çağıranındır.</summary>
    Task<byte[]> RenderPickListAsync(Guid pickListId, CancellationToken ct);

    /// <summary>
    ///     Sevk irsaliyesinin A4 PDF'i: satıcı ve alıcı, teslimat adresi, kalemler (lot/seri), araç ve sürücü, sevk tarihi-saati. İrsaliye numarası verilmiş sevk gerekir.
    ///     Yetki ve şube denetimi çağıranındır.
    /// </summary>
    Task<byte[]> RenderIrsaliyeAsync(Guid shipmentId, CancellationToken ct);

    /// <summary>Teslimat planının A4 rota föyü: duraklar sırayla (müşteri, adres, telefon, kalemler) ve imza satırı. Yetki ve şube denetimi çağıranındır.</summary>
    Task<byte[]> RenderDeliveryPlanAsync(Guid planId, CancellationToken ct);
}
