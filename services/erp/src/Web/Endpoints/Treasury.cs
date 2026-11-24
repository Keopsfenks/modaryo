using ERP.Application.Treasury;
using ERP.Application.Treasury.Accounts;
using ERP.Application.Treasury.Allocations.Commands;
using ERP.Application.Treasury.Allocations.Queries;
using ERP.Application.Treasury.ExchangeDifferences;
using ERP.Application.Treasury.ExchangeDifferences.Commands;
using ERP.Application.Treasury.ExchangeDifferences.Queries;
using ERP.Application.Treasury.ExpenseCategories;
using ERP.Application.Treasury.Instruments;
using ERP.Application.Treasury.Instruments.Commands;
using ERP.Application.Treasury.Instruments.Queries;
using ERP.Application.Treasury.Payments.Commands;
using ERP.Application.Treasury.Transfers;
using ERP.Application.Treasury.Vouchers;
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
        groupBuilder.MapPost(CreateCollection, "collections");
        groupBuilder.MapPost(CreatePayment, "payments");
        groupBuilder.MapPost(CancelFinanceDocument, "finance-documents/{id:guid}/cancel");
        groupBuilder.MapGet(GetOpenItems, "open-items");
        groupBuilder.MapPost(Allocate, "allocations");
        groupBuilder.MapDelete(Unallocate, "allocations/{id:guid}");
        groupBuilder.MapPost(CreateAccountTransfer, "transfers");
        groupBuilder.MapGet(ListExpenseCategories, "expense-categories");
        groupBuilder.MapPost(CreateExpenseCategory, "expense-categories");
        groupBuilder.MapPut(UpdateExpenseCategory, "expense-categories/{id:guid}");
        groupBuilder.MapDelete(DeleteExpenseCategory, "expense-categories/{id:guid}");
        groupBuilder.MapPost(CreateExpense, "expenses");
        groupBuilder.MapPost(ReceiveInstrument, "instruments/receive");
        groupBuilder.MapPost(IssueInstrument, "instruments/issue");
        groupBuilder.MapPost(MoveInstrument, "instruments/{id:guid}/move");
        groupBuilder.MapGet(ListInstruments, "instruments");
        groupBuilder.MapGet(GetInstrument, "instruments/{id:guid}");
        groupBuilder.MapGet(GetPortfolio, "instruments/portfolio");
        groupBuilder.MapGet(GetCalendar, "instruments/calendar");
        groupBuilder.MapGet(ListExchangeDifferences, "exchange-differences");
        groupBuilder.MapPost(CreateDifferenceInvoice, "exchange-differences/invoice");
        groupBuilder.MapPost(CreateDifferenceExpense, "exchange-differences/expense");
    }

    [EndpointSummary("Virman")]
    [EndpointDescription("virman.manage. Tutar çıkış hesabının para biriminde. Para birimleri farklıysa kur (yabancı paranın TL karşılığı) zorunludur (400 rate_required); iki yabancı para arasında virman yoktur (400 transfer_currency_pair). Aynı hesap 400. Dönem kilitliyse 409. warnings: cash_negative.")]
    public static async Task<Created<PostResult>> CreateAccountTransfer(ISender sender, CreateTransferCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/finance-documents/{result.Id}", result);
    }

    [EndpointSummary("Masraf/gelir kategorileri")]
    [EndpointDescription("masraf.manage ya da finans.read. İlk kullanımda varsayılan kategoriler açılır (hesap kodları finans ayarlarından).")]
    public static async Task<Ok<IReadOnlyList<ExpenseCategoryDto>>> ListExpenseCategories(ISender sender, bool? isIncome, bool? includeInactive) {
        return TypedResults.Ok(await sender.Send(new ListExpenseCategoriesQuery(isIncome, includeInactive ?? true)));
    }

    [EndpointSummary("Kategori oluştur")]
    [EndpointDescription("masraf.manage. Kod addan üretilir; aynı kod 409 name_taken.")]
    public static async Task<Created<ExpenseCategoryDto>> CreateExpenseCategory(ISender sender, CreateExpenseCategoryCommand command) {
        var category = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/expense-categories/{category.Id}", category);
    }

    [EndpointSummary("Kategoriyi güncelle")]
    [EndpointDescription("masraf.manage. Ad, hesap kodu ve etkinlik değişir.")]
    public static async Task<Ok<ExpenseCategoryDto>> UpdateExpenseCategory(ISender sender, Guid id, UpdateExpenseCategoryCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Kategoriyi sil")]
    [EndpointDescription("masraf.manage. Sistem kategorisi 409 category_system, belgede kullanılan 409 category_in_use.")]
    public static async Task<NoContent> DeleteExpenseCategory(ISender sender, Guid id) {
        await sender.Send(new DeleteExpenseCategoryCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Masraf/gelir fişi")]
    [EndpointDescription("masraf.manage. kind: expense | income. Tutar KDV dahildir; netAmount/vatAmount ayrılır. accountId (hesap hareketi) ya da currentAccountId (masraf cariyi alacaklandırır, gelir borçlandırır) tam biri verilir (400 expense_target). Kategori türü fişe uymalıdır (400 category_kind_mismatch).")]
    public static async Task<Created<ExpenseResult>> CreateExpense(ISender sender, CreateExpenseCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/finance-documents/{result.Id}", result);
    }

    [EndpointSummary("Kur farkları")]
    [EndpointDescription("finans.read. TL carilerde yabancı para kaynaklı fatura ve ödeme tahsislerinin kur farkı: döviz tutarı x (alacak kuru - borç kuru). İşlenmiş ve sıfır farklar listelenmez. from/to tarih aralığı, currentAccountId isteğe bağlı.")]
    public static async Task<Ok<IReadOnlyList<ExchangeDifferenceDto>>> ListExchangeDifferences(ISender sender, Guid? currentAccountId, DateOnly? from, DateOnly? to) {
        return TypedResults.Ok(await sender.Send(new ListExchangeDifferencesQuery(currentAccountId, from, to)));
    }

    [EndpointSummary("Kur farkı faturası")]
    [EndpointDescription("finans.manage. Pozitif kur farkından KDV dahil (vatRate varsayılan 20) ExchangeDifference satış faturası taslağı. İşlenmiş fark 409 difference_already_settled.")]
    public static async Task<Created<DifferenceInvoiceResult>> CreateDifferenceInvoice(ISender sender, CreateDifferenceInvoiceCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/invoices/{result.InvoiceId}", result);
    }

    [EndpointSummary("Kur farkı gideri")]
    [EndpointDescription("masraf.manage. Negatif kur farkından kur-farki kategorili gider fişi; accountId verilmezse cariyi alacaklandırır. İşlenmiş fark 409 difference_already_settled.")]
    public static async Task<Created<DifferenceExpenseResult>> CreateDifferenceExpense(ISender sender, CreateDifferenceExpenseCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/finance-documents/{result.ExpenseId}", result);
    }

    [EndpointSummary("Çek/senet al")]
    [EndpointDescription("ceksenet.manage, cek-senet modülü. Müşteri carisi alacaklanır. Numara (tür, yön, banka, numara) benzersizdir (409 instrument_number_taken).")]
    public static async Task<Created<InstrumentPostResult>> ReceiveInstrument(ISender sender, ReceiveInstrumentCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/instruments/{result.InstrumentId}", result);
    }

    [EndpointSummary("Çek/senet ver")]
    [EndpointDescription("ceksenet.manage, cek-senet modülü. Tedarikçi carisi borçlanır; çekte bankAccountId zorunludur.")]
    public static async Task<Created<InstrumentPostResult>> IssueInstrument(ISender sender, IssueInstrumentCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/instruments/{result.InstrumentId}", result);
    }

    [EndpointSummary("Çek/senet durum değiştir")]
    [EndpointDescription("ceksenet.manage, cek-senet modülü. to: endorsed (currentAccountId tedarikçi), inCollection (accountId banka), returnedToCustomer, collected (geç tahsilatta accountId), bounced, paid (notta accountId), returnedFromSupplier. Geçersiz geçiş 409 invalid_instrument_transition; dönem kilitli 409.")]
    public static async Task<Ok<InstrumentPostResult>> MoveInstrument(ISender sender, Guid id, MoveInstrumentCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Çek/senet listesi")]
    [EndpointDescription("finans.read, cek-senet modülü. Sorguda enum değerleri PascalCase (Received, InPortfolio).")]
    public static async Task<Ok<IReadOnlyList<InstrumentDto>>> ListInstruments(ISender sender, InstrumentDirection? direction, InstrumentStatus? status, DateOnly? dueFrom, DateOnly? dueTo, Guid? currentAccountId) {
        return TypedResults.Ok(await sender.Send(new ListInstrumentsQuery(direction, status, dueFrom, dueTo, currentAccountId)));
    }

    [EndpointSummary("Çek/senet ve geçmişi")]
    public static async Task<Ok<InstrumentDetailDto>> GetInstrument(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetInstrumentQuery(id)));
    }

    [EndpointSummary("Portföy özeti")]
    [EndpointDescription("Yön, durum ve para birimine göre adet ve toplam.")]
    public static async Task<Ok<IReadOnlyList<PortfolioRow>>> GetPortfolio(ISender sender) {
        return TypedResults.Ok(await sender.Send(new PortfolioQuery()));
    }

    [EndpointSummary("Vade takvimi")]
    [EndpointDescription("Açık (portföyde, tahsilde, verilmiş) çek/senetler vade gününe göre.")]
    public static async Task<Ok<IReadOnlyList<CalendarDay>>> GetCalendar(ISender sender, DateOnly? from, DateOnly? to) {
        return TypedResults.Ok(await sender.Send(new CalendarQuery(from, to)));
    }

    [EndpointSummary("Tahsilat")]
    [EndpointDescription("tahsilat.manage. Hesaba giriş, cari alacak. method: cash (kasa) | transfer (banka) | creditCard (kart POS hesabı) (400 account_method_mismatch). Hesap ve cari para birimi farklıysa kur (yabancı paranın TL karşılığı) verilir; verilmezse TCMB kuru kullanılır, o da yoksa 400 rate_required. allocations verilirse elle eşleştirilir (400 allocation_exceeds_open / allocation_other_account); kalan tutar FIFO ile eşleşir. Dönem kilitliyse 409. warnings: cash_negative yalnızca ödemede.")]
    public static async Task<Created<PostResult>> CreateCollection(ISender sender, CreateCollectionCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/finance-documents/{result.Id}", result);
    }

    [EndpointSummary("Ödeme")]
    [EndpointDescription("odeme.manage. Hesaptan çıkış, cari borç; tahsilatla aynı kurallar. Kasa bakiyesi eksiye düşerse warnings içinde cash_negative gelir (banka hesabı ekside olabilir).")]
    public static async Task<Created<PostResult>> CreatePayment(ISender sender, CreatePaymentCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/finance-documents/{result.Id}", result);
    }

    [EndpointSummary("Belgeyi iptal et")]
    [EndpointDescription("Belge türünün izni (tahsilat.manage / odeme.manage). Hesap hareketi, defter hareketi ve eşleştirmeler geri alınır; 409 document_cancelled / dönem kilitli.")]
    public static async Task<NoContent> CancelFinanceDocument(ISender sender, Guid id, CancelFinanceDocumentBody body) {
        await sender.Send(new CancelFinanceDocumentCommand(id, body.Reason));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Eşleştirme için açık kalemler")]
    [EndpointDescription("finans.read ya da tahsilat/ödeme izni. open: tüm eşleştirmeler sonrası kalan; allocatable: elle eşleştirilebilecek tutar (otomatik eşleştirmeler hariç).")]
    public static async Task<Ok<IReadOnlyList<OpenItemForAllocationDto>>> GetOpenItems(ISender sender, Guid currentAccountId) {
        return TypedResults.Ok(await sender.Send(new OpenItemsForAllocationQuery(currentAccountId)));
    }

    [EndpointSummary("Elle eşleştir")]
    [EndpointDescription("tahsilat.manage ya da odeme.manage. creditLineId (vade satırı) ya da paymentId (belge) kaynaktır; allocations karşı yöndeki satırlardır. Aynı cari (400 allocation_other_account), toplam <= kaynağın eşleştirilmemiş tutarı ve her satır <= açık tutarı (400 allocation_exceeds_open), yön karşıt (400 allocation_direction), iptal edilmiş satır 409 allocation_line_cancelled. Elle eşleştirme sonrası otomatik eşleştirmeler yeniden kurulur.")]
    public static async Task<Created<IReadOnlyList<AllocationResult>>> Allocate(ISender sender, AllocateCommand command) {
        return TypedResults.Created("/api/erp/treasury/allocations", await sender.Send(command));
    }

    [EndpointSummary("Elle eşleştirmeyi kaldır")]
    [EndpointDescription("Yalnızca elle eşleştirmeler kaldırılır (409 allocation_not_manual).")]
    public static async Task<NoContent> Unallocate(ISender sender, Guid id) {
        await sender.Send(new UnallocateCommand(id));
        return TypedResults.NoContent();
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

public sealed record CancelFinanceDocumentBody(string Reason);
