using System.Net.Http.Headers;
using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Shipping;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

public class DeliveryTests : TestBase {
    private static async Task<Guid> UploadImage(Env env, string kind = "image") {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(DispatchSupport.Png);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", "imza.png");
        using var upload = await env.Client.PostAsync($"/api/erp/files?kind={kind}", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        return (await upload.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Act(Env env, Guid shipment, string action, object? body = null) {
        return env.Client.PostAsJsonAsync($"{Api}/shipments/{shipment}/{action}", body ?? new { });
    }

    private static async Task<JsonElement> ActOk(Env env, Guid shipment, string action, object? body = null) {
        using var response = await Act(env, shipment, action, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static object FailBody(string reason = "Adres bulunamadı") => new { reason, at = "2026-11-21T10:00:00+03:00" };

    [Test]
    public async Task Deliver_StoresProof() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment);
        var photo = await UploadImage(env);

        var delivered = await ActOk(env, shipment, "deliver", new { receivedBy = "Ayşe Yılmaz", deliveredAt = "2026-11-21T15:20:00+03:00", proofFileId = photo, note = "Kapıcıya bırakıldı" });

        delivered.GetProperty("status").GetString().ShouldBe("delivered");
        delivered.GetProperty("receivedBy").GetString().ShouldBe("Ayşe Yılmaz");
        delivered.GetProperty("attempts")[0].GetProperty("outcome").GetString().ShouldBe("Delivered");
        await using var db = PricingTestSupport.Db(env.Tenant);
        var proof = await db.DeliveryProofs.AsNoTracking().SingleAsync(p => p.ShipmentId == shipment);
        proof.ReceivedBy.ShouldBe("Ayşe Yılmaz");
        proof.PhotoFileId.ShouldBe(photo);
        proof.Note.ShouldBe("Kapıcıya bırakıldı");
        proof.DeliveredAt.ShouldBe(DateTimeOffset.Parse("2026-11-21T15:20:00+03:00"));
        delivered.GetProperty("proof").GetProperty("photoFileId").GetGuid().ShouldBe(photo);
    }

    [Test]
    public async Task Deliver_Validation() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);

        // Sevk edilmemiş sevk teslim edilemez.
        using var early = await Act(env, shipment, "deliver", new { receivedBy = "Ali Veli" });
        await Fail(early, HttpStatusCode.Conflict, "invalid_shipment_transition");
        await DispatchSupport.ShipOk(env, shipment);

        using var empty = await Act(env, shipment, "deliver", new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await empty.Content.ReadAsStringAsync());
        using var shortName = await Act(env, shipment, "deliver", new { receivedBy = "A" });
        shortName.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await shortName.Content.ReadAsStringAsync());
        using var longName = await Act(env, shipment, "deliver", new { receivedBy = new string('x', 101) });
        longName.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await longName.Content.ReadAsStringAsync());

        using var unknown = await Act(env, shipment, "deliver", new { receivedBy = "Ali Veli", proofFileId = Guid.CreateVersion7() });
        await Fail(unknown, HttpStatusCode.BadRequest, "proof_file_invalid");
        var attachment = await UploadImage(env, "attachment");
        using var wrongKind = await Act(env, shipment, "deliver", new { receivedBy = "Ali Veli", proofFileId = attachment });
        await Fail(wrongKind, HttpStatusCode.BadRequest, "proof_file_invalid");

        await ActOk(env, shipment, "deliver", new { receivedBy = "Ali Veli" });
        using var again = await Act(env, shipment, "deliver", new { receivedBy = "Ali Veli" });
        await Fail(again, HttpStatusCode.Conflict, "invalid_shipment_transition");
    }

    [Test]
    public async Task Deliver_RequiresDeliverPermission() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment);
        using var limited = await OtherClient(env, "sevkiyat.read", "sevkiyat.ship");

        using var response = await limited.PostAsJsonAsync($"{Api}/shipments/{shipment}/deliver", new { receivedBy = "Ali Veli" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task FailedResend_NoDoubleStockIssue() {
        var (env, order, shipment, _, warehouse) = await DispatchSupport.Packed(3m);
        var first = await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "yurtici", trackingNo = "ILK-1" });

        var failed = await ActOk(env, shipment, "fail", FailBody());

        failed.GetProperty("status").GetString().ShouldBe("deliveryFailed");
        failed.GetProperty("attempts")[0].GetProperty("outcome").GetString().ShouldBe("Failed");
        failed.GetProperty("attempts")[0].GetProperty("failureReason").GetString().ShouldBe("Adres bulunamadı");
        using var blockedDeliver = await Act(env, shipment, "deliver", new { receivedBy = "Ali Veli" });
        await Fail(blockedDeliver, HttpStatusCode.Conflict, "invalid_shipment_transition");

        var resent = await ActOk(env, shipment, "resend", new { trackingNo = "IKINCI-2" });

        resent.GetProperty("status").GetString().ShouldBe("shipped");
        resent.GetProperty("trackingNo").GetString().ShouldBe("IKINCI-2");
        resent.GetProperty("carrierCode").GetString().ShouldBe("yurtici");
        resent.GetProperty("irsaliyeNo").GetString().ShouldBe(first.GetProperty("irsaliyeNo").GetString());
        var attempts = resent.GetProperty("attempts");
        attempts.GetArrayLength().ShouldBe(2);
        attempts[1].GetProperty("attemptNo").GetInt32().ShouldBe(2);
        attempts[1].GetProperty("outcome").GetString().ShouldBe("Pending");
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-3m);
        (await Physical(env, warehouse)).ShouldBe(0m);
        LineQty(await GetOrder(env.Client, order), "shippedQuantity").ShouldBe(3m);

        var delivered = await ActOk(env, shipment, "deliver", new { receivedBy = "Ali Veli" });
        delivered.GetProperty("status").GetString().ShouldBe("delivered");
        delivered.GetProperty("attempts")[1].GetProperty("outcome").GetString().ShouldBe("Delivered");

        // Başarısız bildirimi ancak sevk edilmiş sevke yapılır; yeniden gönderim yalnızca başarısızdan.
        using var failAgain = await Act(env, shipment, "fail", FailBody());
        await Fail(failAgain, HttpStatusCode.Conflict, "invalid_shipment_transition");
        using var resendAgain = await Act(env, shipment, "resend", new { });
        await Fail(resendAgain, HttpStatusCode.Conflict, "invalid_shipment_transition");
    }

    [Test]
    public async Task Resend_CarrierChangeNeedsTracking() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "kendi-arac" });
        await ActOk(env, shipment, "fail", FailBody());

        using var response = await Act(env, shipment, "resend", new { carrierCode = "aras" });
        await Fail(response, HttpStatusCode.Conflict, "tracking_required");

        var resent = await ActOk(env, shipment, "resend", new { carrierCode = "aras", trackingNo = "AR-9" });
        resent.GetProperty("carrierCode").GetString().ShouldBe("aras");
    }

    [Test]
    public async Task ReturnedNotInvoiced_ReversesStock() {
        var (env, order, shipment, _, warehouse) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env, shipment);
        (await Physical(env, warehouse)).ShouldBe(0m);
        using var early = await Act(env, shipment, "return", new { reason = "Müşteri almadı" });
        await Fail(early, HttpStatusCode.Conflict, "invalid_shipment_transition");
        await ActOk(env, shipment, "fail", FailBody());

        var returned = await ActOk(env, shipment, "return", new { reason = "Müşteri almadı" });

        returned.GetProperty("status").GetString().ShouldBe("returned");
        (await Physical(env, warehouse)).ShouldBe(4m);
        var movements = await Movements(env, "sales-shipment");
        movements.Sum(m => m.Quantity).ShouldBe(0m);
        var approved = await GetOrder(env.Client, order);
        LineQty(approved, "shippedQuantity").ShouldBe(0m);
        approved.GetProperty("status").GetString().ShouldBe("approved");
        (await Reservations(env)).Where(r => r.Status == ReservationStatus.Active).Sum(r => r.Quantity).ShouldBe(4m);
        // Sevk kapanmıştır: tekrar iade edilemez, iptal edilemez.
        using var again = await Act(env, shipment, "return", new { reason = "Tekrar" });
        await Fail(again, HttpStatusCode.Conflict, "invalid_shipment_transition");
        using var cancel = await Act(env, shipment, "cancel", new { reason = "Geri alınamaz" });
        await Fail(cancel, HttpStatusCode.Conflict, "shipment_not_cancellable");
    }

    [Test]
    public async Task ReturnedInvoiced_CreatesReturnDraft() {
        var (env, order, shipment, _, warehouse) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env, shipment);
        var invoice = await Issue(env.Client, (await Draft(env.Client, shipment)).GetProperty("id").GetGuid());
        await ActOk(env, shipment, "fail", FailBody());

        var returned = await ActOk(env, shipment, "return", new { reason = "Müşteri almadı" });

        returned.GetProperty("status").GetString().ShouldBe("returned");
        returned.GetProperty("returnId").GetGuid().ShouldNotBe(Guid.Empty);
        await using var db = PricingTestSupport.Db(env.Tenant);
        var ret = await db.SalesReturns.AsNoTracking().Include(r => r.Lines).SingleAsync();
        ret.InvoiceId.ShouldBe(invoice.GetProperty("id").GetGuid());
        ret.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(4m);
        ret.WarehouseId.ShouldBe(warehouse);
        ret.ReturnInvoiceId.ShouldNotBeNull();
        returned.GetProperty("returnId").GetGuid().ShouldBe(ret.Id);
        var draft = await GetInvoice(env.Client, ret.ReturnInvoiceId!.Value);
        draft.GetProperty("kind").GetString().ShouldBe("salesReturn");
        draft.GetProperty("status").GetString().ShouldBe("draft");
        // Stok sevk çıkışı geri alınmaz; iade belgesi girişi yazar (çift sayım yok).
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-4m);
        (await Movements(env, "sales-return")).ShouldHaveSingleItem().Quantity.ShouldBe(4m);
        (await Physical(env, warehouse)).ShouldBe(4m);
        LineQty(await GetOrder(env.Client, order), "returnedQuantity").ShouldBe(4m);
    }

    [Test]
    public async Task Returned_InvoicedDraftOrPartial_Rejected() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env, shipment);
        var draft = await Draft(env.Client, shipment);
        await ActOk(env, shipment, "fail", FailBody());

        // Kesilmemiş taslak fatura varken iade yolu: önce taslak silinmelidir.
        using var withDraft = await Act(env, shipment, "return", new { reason = "x1" });
        await Fail(withDraft, HttpStatusCode.Conflict, "shipment_invoice_draft");

        // Teslim edilemeyen (DeliveryFailed) sevkin taslağı artık kesilemez (yalnızca Shipped/Delivered faturalanır).
        using var issueFailed = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);
        await Fail(issueFailed, HttpStatusCode.Conflict, "shipment_not_invoiceable");

        // Kısmen faturalanmış sevk: karışık durum desteklenmez (kısmi fatura sevk edilmişken kesilir, sonra teslimat başarısız olur).
        var (env2, _, shipment2, _, _) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env2, shipment2);
        var draft2 = await Draft(env2.Client, shipment2);
        using var put = await PutDraft(env2, draft2, 1m);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var current = await GetInvoice(env2.Client, draft2.GetProperty("id").GetGuid());
        await Issue(env2.Client, current.GetProperty("id").GetGuid());
        await ActOk(env2, shipment2, "fail", FailBody());
        using var partial = await Act(env2, shipment2, "return", new { reason = "x2" });
        await Fail(partial, HttpStatusCode.Conflict, "shipment_partially_invoiced");
        (await PickSupport.GetShipment(env2, shipment2)).GetProperty("status").GetString().ShouldBe("deliveryFailed");
    }

    [Test]
    public async Task Attempts_LegacyDirectShipment_GetsAttemptRow() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 2m);
        var shipment = (await Ship(env.Client, order, LineId(approved), 2m)).GetProperty("id").GetGuid();

        var failed = await ActOk(env, shipment, "fail", FailBody("Kapalı"));

        failed.GetProperty("attempts").GetArrayLength().ShouldBe(1);
        failed.GetProperty("attempts")[0].GetProperty("outcome").GetString().ShouldBe("Failed");
    }
}
