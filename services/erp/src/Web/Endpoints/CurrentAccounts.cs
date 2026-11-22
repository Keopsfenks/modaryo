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
