using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing.Documents;
using ERP.Application.Sales.Shipments;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Application.Shipping.Dispatch;

namespace ERP.Application.Shipping.Notifications;

/// <summary>
///     "shipment.shipped" olayında müşteriye sevk bildirimi e-postası gönderir (geçici sahip: G0c Task 5 bu işleyiciyi emekli edip müşteri mesajlarının tek sahibi olacak;
///     rıza orada denetlenir). Yalnızca ayar NotifyCustomerOnShip açıkken ve carinin e-posta adresi varsa gönderir; adres yoksa deneme yapılmaz (arayüz sevk yanıtındaki
///     customerEmailMissing alanıyla uyarır). Outbox aboneliğidir: gönderim sevk işleminden sonra, hata durumunda geri çekilmeyle yeniden denenerek yapılır;
///     hata sevki geri almaz. Şablon değişkenleri: {alici}, {belgeNo}, {kargo}, {takipNo}, {takipLink}.
/// </summary>
public sealed partial class ShipmentEmailHandler(
    IApplicationDbContext db,
    ITenantSettings       settings,
    IDocumentMailer       mailer
) : IOutboxSubscriber {
    public const string SubscriberName = "shipment-email";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static bool Handles(string type) {
        return type == ShipmentEvents.Shipped;
    }

    private sealed record Payload(Guid ShipmentId, string Number, Guid CurrentAccountId, string? CarrierCode, string? TrackingNo);

    public async Task<bool> HandleAsync(OutboxEvent message, CancellationToken ct) {
        var shipping = await settings.GetAsync<ShippingSettings>(ct);
        if (!shipping.NotifyCustomerOnShip) return false;

        var payload = JsonSerializer.Deserialize<Payload>(message.PayloadJson, Json) ?? throw new InvalidOperationException("Sevk olayı yükü okunamadı.");
        var cari    = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == payload.CurrentAccountId).Select(c => new { c.Name, c.Email }).FirstOrDefaultAsync(ct);
        if (cari is null || string.IsNullOrWhiteSpace(cari.Email)) return false;

        var variables = new Dictionary<string, string> {
            ["alici"]     = cari.Name,
            ["belgeNo"]   = payload.Number,
            ["kargo"]     = CarrierRules.Name(shipping, payload.CarrierCode) ?? "-",
            ["takipNo"]   = string.IsNullOrWhiteSpace(payload.TrackingNo) ? "-" : payload.TrackingNo,
            ["takipLink"] = CarrierRules.TrackingUrl(shipping, payload.CarrierCode, payload.TrackingNo) ?? "-"
        };
        var subject = Fill(shipping.ShipEmailSubject, variables).ReplaceLineEndings(" ").Trim();
        var body    = InvoiceMail.ToHtml(Fill(shipping.ShipEmailBody, variables));
        await mailer.SendAsync([cari.Email.Trim()], null, subject, body, [], ct);
        return true;
    }

    /// <summary>Değişkenleri tek geçişte doldurur (yerine konan değer yeniden genişletilmez).</summary>
    private static string Fill(string template, IReadOnlyDictionary<string, string> variables) {
        return Variable().Replace(template, m => variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    [GeneratedRegex(@"\{(alici|belgeNo|kargo|takipNo|takipLink)\}")]
    private static partial Regex Variable();
}
