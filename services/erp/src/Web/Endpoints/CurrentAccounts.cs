using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Commands;
using ERP.Application.CurrentAccounts.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class CurrentAccounts : IEndpointGroup
{
    public static string? RoutePrefix => "/api/erp/current-accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(ListCurrentAccounts);
        groupBuilder.MapGet(NextCode, "next-code");
        groupBuilder.MapGet(GetCurrentAccount, "{id:guid}");
        groupBuilder.MapPost(CreateCurrentAccount);
        groupBuilder.MapPut(UpdateCurrentAccount, "{id:guid}");
        groupBuilder.MapPost(DeactivateCurrentAccount, "{id:guid}/deactivate");
        groupBuilder.MapPost(ActivateCurrentAccount, "{id:guid}/activate");
        groupBuilder.MapDelete(DeleteCurrentAccount, "{id:guid}");
        groupBuilder.MapPost(AddAddress, "{id:guid}/addresses");
        groupBuilder.MapPut(UpdateAddress, "{id:guid}/addresses/{addressId:guid}");
        groupBuilder.MapDelete(DeleteAddress, "{id:guid}/addresses/{addressId:guid}");
        groupBuilder.MapPost(AddEntry, "{id:guid}/entries");
        groupBuilder.MapPost(CancelEntry, "{id:guid}/entries/{entryId:guid}/cancel");
        groupBuilder.MapGet(GetStatement, "{id:guid}/statement");
    }

    public sealed record CancelEntryRequest(string Reason);

    [EndpointSummary("Cari hareketi gir")]
    [EndpointDescription("type: opening | debit | credit. Açılışta direction (debit | credit) zorunlu; ikinci açılış 409 opening_exists, pasif kart 409 account_inactive. Borçta dueDate boşsa tarih + varsayılan vade. creditLimitExceeded yalnızca borç sonrası limit aşılırsa true olur; kayıt yine yapılır.")]
    public static async Task<Created<AddEntryResult>> AddEntry(ISender sender, Guid id, AddEntryCommand command)
    {
        var result = await sender.Send(command with { AccountId = id });
        return TypedResults.Created($"/api/erp/current-accounts/{id}/statement", result);
    }

    [EndpointSummary("Cari hareketi iptal et")]
    [EndpointDescription("Yalnızca elle girilen hareketler iptal edilir (aksi halde 409 not_manual); zaten iptal edilmişse 409 already_cancelled. İptal bakiyeye yansımaz, ekstrede üstü çizili görünür.")]
    public static async Task<NoContent> CancelEntry(ISender sender, Guid id, Guid entryId, CancelEntryRequest request)
    {
        await sender.Send(new CancelEntryCommand(id, entryId, request.Reason));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cari ekstresi")]
    [EndpointDescription("from varsayılan yılın 1 Ocak günü, to varsayılan bugün (İstanbul). from > to ise 400.")]
    public static async Task<Ok<StatementResultDto>> GetStatement(ISender sender, Guid id, DateOnly? from, DateOnly? to) =>
        TypedResults.Ok(await sender.Send(new GetStatementQuery(id, from, to)));

    [EndpointSummary("Cari adresi ekle")]
    [EndpointDescription("Fatura ve sevk için ayrı ayrı: o türün ilk adresi otomatik varsayılan olur; yeni varsayılan seçilirse eskisinin işareti kalkar. En az biri (fatura/sevk) işaretli olmalıdır.")]
    public static async Task<Created<AddressDto>> AddAddress(ISender sender, Guid id, AddressInput address)
    {
        var created = await sender.Send(new AddAddressCommand(id, address));
        return TypedResults.Created($"/api/erp/current-accounts/{id}", created);
    }

    [EndpointSummary("Cari adresi güncelle")]
    public static async Task<Ok<AddressDto>> UpdateAddress(ISender sender, Guid id, Guid addressId, AddressInput address) =>
        TypedResults.Ok(await sender.Send(new UpdateAddressCommand(id, addressId, address)));

    [EndpointSummary("Cari adresi sil")]
    [EndpointDescription("Varsayılan adres silinirse aynı türdeki en eski adres varsayılan olur.")]
    public static async Task<NoContent> DeleteAddress(ISender sender, Guid id, Guid addressId)
    {
        await sender.Send(new DeleteAddressCommand(id, addressId));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cari listesi")]
    [EndpointDescription("search: kod, ad veya vergi no. role: customer | supplier. Pasif kartlar yalnızca includeInactive=true ile gelir. pageSize varsayılan 25, en fazla 100.")]
    public static async Task<Ok<PagedResult<CurrentAccountListItem>>> ListCurrentAccounts(
        ISender sender, string? search, string? role, bool includeInactive = false, int page = 1, int pageSize = 25) =>
        TypedResults.Ok(await sender.Send(new ListCurrentAccountsQuery(search, role, includeInactive, page, pageSize)));

    [EndpointSummary("Sonraki cari kodu")]
    public static async Task<Ok<NextCodeDto>> NextCode(ISender sender) =>
        TypedResults.Ok(await sender.Send(new NextCodeQuery()));

    [EndpointSummary("Cari kart")]
    public static async Task<Ok<CurrentAccountDto>> GetCurrentAccount(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetCurrentAccountQuery(id)));

    [EndpointSummary("Cari kart oluştur")]
    [EndpointDescription("İsteğe bağlı ilk adres ve açılış bakiyesi aynı istekte gelebilir. Kod çakışırsa 409 code_taken, vergi numarası çakışırsa 409 tax_number_taken.")]
    public static async Task<Created<CurrentAccountDto>> CreateCurrentAccount(ISender sender, CreateCurrentAccountCommand command)
    {
        var account = await sender.Send(command);
        return TypedResults.Created($"/api/erp/current-accounts/{account.Id}", account);
    }

    [EndpointSummary("Cari kartı güncelle")]
    [EndpointDescription("Adresler hariç kart alanlarını günceller. Hareketi olan kartta para birimi değişirse 409 currency_locked.")]
    public static async Task<Ok<CurrentAccountDto>> UpdateCurrentAccount(ISender sender, Guid id, UpdateCurrentAccountCommand command) =>
        TypedResults.Ok(await sender.Send(command with { Id = id }));

    [EndpointSummary("Cariyi pasifleştir")]
    public static async Task<NoContent> DeactivateCurrentAccount(ISender sender, Guid id)
    {
        await sender.Send(new SetCurrentAccountActiveCommand(id, false));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cariyi aktifleştir")]
    public static async Task<NoContent> ActivateCurrentAccount(ISender sender, Guid id)
    {
        await sender.Send(new SetCurrentAccountActiveCommand(id, true));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cari kartı sil")]
    [EndpointDescription("Hiç hareketi (iptal edilmiş dahil) olmayan kart silinir; aksi halde 409 has_movements.")]
    public static async Task<NoContent> DeleteCurrentAccount(ISender sender, Guid id)
    {
        await sender.Send(new DeleteCurrentAccountCommand(id));
        return TypedResults.NoContent();
    }
}
