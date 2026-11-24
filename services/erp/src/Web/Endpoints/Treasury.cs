using ERP.Application.Treasury.Accounts;
using ERP.Domain.Treasury;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Treasury : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/treasury";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListAccounts, "accounts");
        groupBuilder.MapGet(GetAccount, "accounts/{id:guid}");
        groupBuilder.MapPost(CreateAccount, "accounts");
        groupBuilder.MapPut(UpdateAccount, "accounts/{id:guid}");
        groupBuilder.MapPost(ActivateAccount, "accounts/{id:guid}/activate");
        groupBuilder.MapPost(DeactivateAccount, "accounts/{id:guid}/deactivate");
        groupBuilder.MapDelete(DeleteAccount, "accounts/{id:guid}");
        groupBuilder.MapGet(GetAccountMovements, "accounts/{id:guid}/movements");
    }

    [EndpointSummary("Kasa ve banka hesapları")]
    [EndpointDescription("finans.read. Yalnızca oturumun erişebildiği şubelerin hesapları gelir; balance hareketlerin toplamıdır (iptal edilenler hariç). kind ile süzülebilir.")]
    public static async Task<Ok<IReadOnlyList<TreasuryAccountDto>>> ListAccounts(ISender sender, TreasuryAccountKind? kind, bool? includeInactive) {
        return TypedResults.Ok(await sender.Send(new ListAccountsQuery(kind, includeInactive ?? true)));
    }

    [EndpointSummary("Hesap")]
    [EndpointDescription("finans.read. Erişilemeyen şube 403 branch_forbidden.")]
    public static async Task<Ok<TreasuryAccountDto>> GetAccount(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetAccountQuery(id)));
    }

    [EndpointSummary("Hesap oluştur")]
    [EndpointDescription("finans.manage. kind: gövdede cash | bank | cardPos, sorguda Cash | Bank | CardPos. Banka hesabında IBAN (TR, 26 hane, sağlama toplamı) ve banka adı zorunludur (400). Ad firmada benzersizdir (409 name_taken). Şube erişimi yoksa 403 branch_forbidden.")]
    public static async Task<Created<TreasuryAccountDto>> CreateAccount(ISender sender, CreateAccountCommand command) {
        var account = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/accounts/{account.Id}", account);
    }

    [EndpointSummary("Hesabı güncelle")]
    [EndpointDescription("finans.manage. Hareketi olan hesabın para birimi ve türü değiştirilemez (409 currency_locked).")]
    public static async Task<Ok<TreasuryAccountDto>> UpdateAccount(ISender sender, Guid id, UpdateAccountCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Hesabı etkinleştir")]
    public static async Task<NoContent> ActivateAccount(ISender sender, Guid id) {
        await sender.Send(new SetAccountActiveCommand(id, true));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Hesabı pasifleştir")]
    public static async Task<NoContent> DeactivateAccount(ISender sender, Guid id) {
        await sender.Send(new SetAccountActiveCommand(id, false));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Hesabı sil")]
    [EndpointDescription("Hareketi ya da belgesi olan hesap 409 account_in_use.")]
    public static async Task<NoContent> DeleteAccount(ISender sender, Guid id) {
        await sender.Send(new DeleteAccountCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Hesap hareketleri")]
    [EndpointDescription("finans.read. from öncesi açılış bakiyesi (openingBalance); satırlarda yürüyen bakiye ve kaynak belge (documentId, documentKind, documentNumber). İptal edilen hareketler isCancelled ile gelir, bakiyeye girmez.")]
    public static async Task<Ok<AccountMovementsDto>> GetAccountMovements(ISender sender, Guid id, DateOnly? from, DateOnly? to, int? page, int? pageSize) {
        return TypedResults.Ok(await sender.Send(new GetAccountMovementsQuery(id, from, to, page ?? 1, pageSize ?? 50)));
    }
}
