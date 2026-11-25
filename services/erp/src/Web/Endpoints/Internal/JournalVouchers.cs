using ERP.Application.Treasury.Journal;
using ERP.Application.Treasury.Journal.Commands;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints.Internal;

/// <summary>İK ve duran varlık servisleri mahsup fişi yazar (bordro, amortisman). İdempotency-Key zorunludur; eylemci "sistem:{servis}".</summary>
public class InternalJournalVouchers : IEndpointGroup {
    public static string? RoutePrefix => "/internal/journal-vouchers";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.ExcludeFromDescription();
        groupBuilder.MapPost(CreateVoucher);
    }

    [EndpointSummary("İç API mahsup fişi")]
    [EndpointDescription("Yalnızca imzalı servis çağrıları (hr, assets). Firma X-Modaryo-Tenant, anahtar Idempotency-Key başlığından gelir. Kaynak (sourceType, sourceId) iptal edilmedikçe tek fiş (409 voucher_source_exists). Dönem kilidi uygulanır (409 period_locked).")]
    public static async Task<Created<VoucherResult>> CreateVoucher(ISender sender, PostInternalVoucherCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/journal-vouchers/{result.Id}", result);
    }
}
