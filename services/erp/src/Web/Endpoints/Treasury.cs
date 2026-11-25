using ERP.Application.Treasury;
using ERP.Application.Treasury.Accounts;
using ERP.Application.Treasury.Allocations.Commands;
using ERP.Application.Treasury.Allocations.Queries;
using ERP.Application.Treasury.ExchangeDifferences;
using ERP.Application.Treasury.ExchangeDifferences.Commands;
using ERP.Application.Treasury.ExchangeDifferences.Queries;
using ERP.Application.Treasury.ExpenseCategories;
using ERP.Application.Treasury.Instruments;
using ERP.Application.Treasury.Journal;
using ERP.Application.Treasury.Journal.Commands;
using ERP.Application.Treasury.Journal.Queries;
using ERP.Application.Treasury.Instruments.Commands;
using ERP.Application.Treasury.Instruments.Queries;
using ERP.Application.Treasury.Payments.Commands;
using ERP.Application.Treasury.Reports;
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
        groupBuilder.MapGet(ListJournalVouchers, "journal-vouchers");
        groupBuilder.MapGet(GetJournalVoucher, "journal-vouchers/{id:guid}");
        groupBuilder.MapPost(CreateJournalVoucher, "journal-vouchers");
        groupBuilder.MapPost(CancelJournalVoucher, "journal-vouchers/{id:guid}/cancel");
        groupBuilder.MapGet(GetAgingReport, "reports/aging");
        groupBuilder.MapGet(GetCashFlowReport, "reports/cash-flow");
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
    [EndpointDescription("finans.manage. Müşteri tarafındaki pozitif kur farkından KDV dahil (vatRate varsayılan 20) ExchangeDifference satış faturası taslağı; tedarikçi tarafı ve negatif fark 400 difference_not_applicable. İşlenmiş fark 409 difference_already_settled; taslak silinir ya da fatura iptal edilirse fark yeniden işlenebilir.")]
    public static async Task<Created<DifferenceInvoiceResult>> CreateDifferenceInvoice(ISender sender, CreateDifferenceInvoiceCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/invoices/{result.InvoiceId}", result);
    }

    [EndpointSummary("Kur farkı gideri")]
    [EndpointDescription("masraf.manage. Cari artığını kapatan fiş: negatif farktan kur-farki kategorili gider fişi (cariyi alacaklandırır), tedarikçi tarafındaki pozitif farktan kur-farki-geliri kategorili gelir fişi (cariyi borçlandırır). accountId verilemez (400 difference_not_applicable); müşteri tarafındaki pozitif fark ve karışık seçim 400 difference_not_applicable. İşlenmiş fark 409 difference_already_settled; fiş iptal edilirse fark yeniden işlenebilir.")]
    public static async Task<Created<DifferenceExpenseResult>> CreateDifferenceExpense(ISender sender, CreateDifferenceExpenseCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/finance-documents/{result.ExpenseId}", result);
    }

    [EndpointSummary("Mahsup fişleri")]
    [EndpointDescription("finans.read, muhasebe.manage ya da muhasebe.export. from/to, sourceType (Manual, hr, ...) ve includeCancelled (varsayılan false) filtreleri; en çok 500 kayıt.")]
    public static async Task<Ok<IReadOnlyList<VoucherDto>>> ListJournalVouchers(ISender sender, DateOnly? from, DateOnly? to, string? sourceType, bool? includeCancelled) {
        return TypedResults.Ok(await sender.Send(new ListVouchersQuery(from, to, sourceType, includeCancelled ?? false)));
    }

    [EndpointSummary("Mahsup fişi")]
    [EndpointDescription("finans.read, muhasebe.manage ya da muhasebe.export.")]
    public static async Task<Ok<VoucherDto>> GetJournalVoucher(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetVoucherQuery(id)));
    }

    [EndpointSummary("Mahsup fişi oluştur")]
    [EndpointDescription("muhasebe.manage. Satırlar { accountCode, debit, credit, description, currentAccountId? }; toplam borç = toplam alacak (400 voucher_unbalanced). Cari bağlı satır carinin defterine yazılır (yalnızca TL cari). Dönem kilidi uygulanır (409 period_locked).")]
    public static async Task<Created<VoucherResult>> CreateJournalVoucher(ISender sender, CreateVoucherCommand command) {
        var result = await sender.Send(command);
        return TypedResults.Created($"/api/erp/treasury/journal-vouchers/{result.Id}", result);
    }

    [EndpointSummary("Mahsup fişini iptal et")]
    [EndpointDescription("muhasebe.manage. Cari satırları iptal edilir ve eşleştirmeler yeniden kurulur. Dönem kilidi uygulanır; iptal edilmiş fiş 409 voucher_cancelled.")]
    public static async Task<NoContent> CancelJournalVoucher(ISender sender, Guid id, CancelVoucherBody body) {
        await sender.Send(new CancelVoucherCommand(id, body.Reason));
        return TypedResults.NoContent();
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

    [EndpointSummary("Yaşlandırma raporu")]
    [EndpointDescription("finans.read. direction: receivable | payable; currency isteğe bağlı; asOf varsayılan bugün. Cari başına gecikme dilimleri (current, 1-30, 31-60, 61-90, 90+) ve para birimi başına genel toplam.")]
    public static async Task<Ok<AgingReportDto>> GetAgingReport(ISender sender, string direction, string? currency, DateOnly? asOf) {
        return TypedResults.Ok(await sender.Send(new AgingReportQuery(direction, currency, asOf)));
    }

    [EndpointSummary("Nakit akışı")]
    [EndpointDescription("finans.read. granularity: day (en çok 90 dönem) | week (en çok 26); periods varsayılan 30 gün / 12 hafta; from varsayılan bugün. Açılış kasa+banka bakiyesi (TL); dönem başına giriş/çıkış kaynaklara göre (dueLines, instruments, recurring) ve kapanış.")]
    public static async Task<Ok<CashFlowReportDto>> GetCashFlowReport(ISender sender, string granularity, int? periods, DateOnly? from) {
        return TypedResults.Ok(await sender.Send(new CashFlowQuery(granularity, periods, from)));
    }
}

public sealed record CancelFinanceDocumentBody(string Reason);
public sealed record CancelVoucherBody(string Reason);
