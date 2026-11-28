using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Invoicing;
using ERP.Application.Invoicing.Posting;
using ERP.Application.Sales;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Application.Treasury;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Pos;
using ERP.Domain.Sales;
using ERP.Domain.Treasury;

namespace ERP.Application.Pos.Sales;

/// <summary>
///     POS faturalarının kasa dışından (fatura iptal ucu) iptal edilmesini engelleyen bayrak: kasa kendi iptalinde faturayı kendisi geri alır.
///     Kapsam isteğe bağlıdır (scoped).
/// </summary>
public sealed class PosPostingScope {
    public bool Active { get; set; }
}

/// <summary>
///     "pos-sale" / "pos-return" kaynaklı faturaların stoğu kasa belgesinde yazılır; fatura iptal ucu (P4) stok geri alımını bilmediği için bu faturaları
///     reddeder. Kasa kendi iptalinde <see cref="PosPostingScope" /> bayrağını açar.
/// </summary>
public sealed class PosInvoiceHook(PosPostingScope scope) : IInvoicePostHook {
    public const string SaleSource   = "pos-sale";
    public const string ReturnSource = "pos-return";

    public Task BeforePostAsync(Invoice invoice, bool cancelling, CancellationToken ct) {
        if (cancelling && !scope.Active && invoice.SourceType is SaleSource or ReturnSource)
            throw PosErrors.Conflict("pos_invoice_locked", "Bu fatura kasa işleminden üretildi; satışı kasadan iptal edin ya da iade yapın.");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(Guid WarehouseId, Guid ProductId, Guid? VariantId)>> StockKeysAsync(Invoice invoice, bool cancelling, CancellationToken ct) {
        return Task.FromResult<IReadOnlyList<(Guid, Guid, Guid?)>>([]);
    }

    public Task OnIssuedAsync(Invoice invoice, CancellationToken ct) {
        return Task.CompletedTask;
    }

    public Task OnCancelledAsync(Invoice invoice, CancellationToken ct) {
        return Task.CompletedTask;
    }
}

/// <summary>Satış kaydı için hazırlanmış bağlam. Çağıran açık işlemde, vardiya kilidi alınmış (ve gerekiyorsa cari kilitli) olmalıdır.</summary>
public sealed record PosSaleContext(
    Register     Register,
    Shift        Shift,
    PosSettings  Settings,
    CurrentAccount Customer,
    Guid         ClientSaleId,
    ResolvedSale Resolved,
    decimal      Cash,
    decimal      Card,
    decimal      OnAccount,
    decimal      ReturnCredit,
    DateOnly     Date,
    ReservedNumber? Reserved    = null,
    bool         StockLocked = false
);

/// <summary>
///     POS satışının tek yazıcısı. Sıra: (cari kilidi: veresiye ya da e-Arşiv modunda), vardiya kilidi, satır kaydı (tekil ClientSaleId dizini), kredi limiti,
///     yasal belge (yazar kasa: hesap hareketleri + veresiye defter satırı; e-Arşiv: tek satış faturası + tahsilatlar), stok kilitleri (tek çağrıda) ve stok çıkışı.
///     Aynı ClientSaleId ile gelen gönderim yeni satış yazmaz: kayıtlı satış döner.
/// </summary>
public sealed class PosSalePoster(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    TimeProvider          time,
    ITenantSettings       tenantSettings,
    PosSetup              setup,
    PosSaleResolver       resolver,
    FinancePoster         finance,
    InvoiceBuilder        invoiceBuilder,
    InvoiceIssuer         issuer,
    InvoicePoster         invoicePoster,
    IStockService         stock,
    AutoAllocator         allocator,
    IBranchDirectory      branches,
    IOutbox               outbox,
    PosPostingScope       scope
) {
    public const string StockSource = "pos-sale";

    // ---- giriş: POST /sales --------------------------------------------------------------------------------------

    public async Task<SaleResultDto> CreateAsync(SaleInput input, CancellationToken ct) {
        // Aynı kimlikli yeniden gönderim: vardiya/kasa durumundan bağımsız olarak kayıtlı satışı döndürür.
        if (await FindReplayAsync(input.ClientSaleId, ct) is { } replay) return replay;

        var register = await db.PosRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == input.RegisterId, ct) ?? throw new NotFoundException(input.RegisterId.ToString(), "Register");
        BranchGuard.Ensure(tenant, register.BranchId);
        if (!register.IsActive) throw PosErrors.RegisterInactive();

        var settings   = (PosSettings)(await setup.EnsureAsync(ct)).Value;
        var retail     = settings.RetailCustomerId!.Value;
        var customerId = input.CustomerId ?? retail;
        var customer   = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new NotFoundException(customerId.ToString(), "CurrentAccount");
        if (!customer.IsActive) throw PosErrors.Conflict("current_account_inactive", "Cari pasif; satış yapılamaz.");
        if (!customer.IsCustomer) throw PosErrors.Bad("cari_not_customer", "Satış için cari müşteri olmalıdır.");

        var (cash, card, onAccount) = NormalizePayments(input.Payments);
        if (onAccount > 0m && customerId == retail) throw PosErrors.Bad("veresiye_requires_customer", "Veresiye satış için perakende dışında bir müşteri seçilmelidir.");
        if (onAccount > 0m && customer.Currency != "TRY") throw PosErrors.Bad("veresiye_currency", "Veresiye yalnızca TL carilere yapılabilir.");

        if (settings.LegalDocumentMode == LegalDocumentMode.EArchiveInvoice && !(await tenantSettings.GetAsync<EBelgeSettings>(ct)).Enabled) throw PosErrors.EBelgeRequired();

        var date     = CurrentAccountClock.Today(time);
        var resolved = await resolver.ResolveAsync(input.Lines, customerId, date, ct);

        try {
            await using var tx = await db.BeginTransactionAsync(ct);

            // Kilit sırası: cari, vardiya, seri numarası, stok. Retail e-Arşiv satışları aynı cari satırında sıraya girer.
            var needsCari = settings.LegalDocumentMode == LegalDocumentMode.EArchiveInvoice || onAccount > 0m;
            if (needsCari && !await db.LockCurrentAccountAsync(customerId, ct)) throw new NotFoundException(customerId.ToString(), "CurrentAccount");
            var shift = await LockOpenShiftAsync(register.Id, ct);

            // Kilit altında yeniden: bekleyen aynı kimlikli gönderim kazananın kaydını görür.
            if (await FindReplayAsync(input.ClientSaleId, ct) is { } raced) return raced;

            var sale = await PostAsync(new PosSaleContext(register, shift, settings, customer, input.ClientSaleId, resolved, cash, card, onAccount, 0m, date), ct);
            await tx.CommitAsync(ct);
            return await ResultAsync(sale.Id, false, ct);
        } catch (DbUpdateException ex) when (PosErrors.IsUniqueViolation(ex)) {
            // Aynı kimlik eşzamanlı başka bir istekle yazıldı (işlem geri alındı): kayıtlı satışı döndür.
            db.ClearTracking();
            if (await FindReplayAsync(input.ClientSaleId, ct) is { } existing) return existing;
            throw PosErrors.Conflict("number_conflict", "Satış kaydı başka bir belgeyle çakıştı; tekrar deneyin.");
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw PosErrors.VersionConflict();
        }
    }

    /// <summary>Kasiyerin bu kasadaki açık vardiyasını kilitler; yoksa 409 shift_required. Açık işlem gerekir.</summary>
    public async Task<Shift> LockOpenShiftAsync(Guid registerId, CancellationToken ct) {
        var userId = session.UserId ?? throw new ForbiddenAccessException();
        var id = await db.PosShifts.AsNoTracking().Where(s => s.RegisterId == registerId && s.CashierUserId == userId && s.Status == ShiftStatus.Open)
                         .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct) ?? throw ShiftRequired();
        if (!await db.LockShiftAsync(id, ct)) throw ShiftRequired();

        var shift = await db.PosShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shift is not { Status: ShiftStatus.Open }) throw ShiftRequired();
        return shift;
    }

    public static AppException ShiftRequired() {
        return PosErrors.Conflict("shift_required", "Satış için bu kasada açık bir vardiyanız olmalıdır.");
    }

    // ---- çekirdek ----------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Satışı yazar (açık işlem, vardiya kilidi ve gerekiyorsa cari kilidi çağırandadır; işlemi onaylamaz). Değişim satışı da buradan geçer
    ///     (ReturnCredit: iade mahsubu).
    /// </summary>
    public async Task<PosSale> PostAsync(PosSaleContext ctx, CancellationToken ct) {
        var register = ctx.Register;
        var resolved = ctx.Resolved;
        var total    = resolved.Total;
        if (ctx.ReturnCredit < 0m || ctx.ReturnCredit > total) throw PosErrors.Bad("payment_insufficient", "Mahsup tutarı satış toplamını aşamaz.");

        decimal change, cashNet;
        try {
            (change, cashNet) = ChangeCalculator.Compute(total - ctx.ReturnCredit, ctx.Card, ctx.Cash, ctx.OnAccount);
        } catch (PosRuleException ex) {
            throw PosErrors.Bad(ex.Code, ex.Message);
        }

        var now  = time.GetUtcNow();
        var date = ctx.Date;
        var sale = new PosSale {
            Number = await NextNumberAsync(register, date, ct), Status = PosSaleStatus.Completed, RegisterId = register.Id, ShiftId = ctx.Shift.Id,
            CashierUserId = session.UserId ?? throw new ForbiddenAccessException(), CashierName = tenant.UserFullName, BranchId = register.BranchId, WarehouseId = register.WarehouseId,
            CurrentAccountId = ctx.Customer.Id, Date = date, At = now, Mode = ctx.Settings.LegalDocumentMode, ClientSaleId = ctx.ClientSaleId,
            Subtotal = resolved.Subtotal, Discount = resolved.Discount, VatTotal = resolved.Vat, Total = total, ChangeGiven = change,
            CashNet = cashNet, CardAmount = ctx.Card, OnAccountAmount = ctx.OnAccount, ReturnCreditAmount = ctx.ReturnCredit
        };
        foreach (var l in resolved.Lines)
            sale.Lines.Add(new PosSaleLine {
                SaleId = sale.Id, Sort = l.Sort, ProductId = l.Product.Id, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit, Factor = l.Factor, Quantity = l.Quantity,
                UnitPrice = l.UnitPrice, VatRate = l.VatRate, Discount = l.Discount, CampaignDiscount = l.CampaignDiscount, CampaignId = l.CampaignId, Net = l.Net, Vat = l.Vat, Total = l.Total
            });
        if (ctx.Cash > 0m) sale.Payments.Add(new PosPayment { SaleId = sale.Id, Method = PosPaymentMethod.Cash, Amount = ctx.Cash });
        if (ctx.Card > 0m) sale.Payments.Add(new PosPayment { SaleId = sale.Id, Method = PosPaymentMethod.Card, Amount = ctx.Card });
        if (ctx.OnAccount > 0m) sale.Payments.Add(new PosPayment { SaleId = sale.Id, Method = PosPaymentMethod.OnAccount, Amount = ctx.OnAccount });
        if (ctx.ReturnCredit > 0m) sale.Payments.Add(new PosPayment { SaleId = sale.Id, Method = PosPaymentMethod.ReturnCredit, Amount = ctx.ReturnCredit });

        // Tekil ClientSaleId dizini burada devreye girer: kayıt ilk yazımdır, stok ve hesap hareketlerinden önce.
        db.PosSales.Add(sale);
        await db.SaveChangesAsync(ct);

        if (ctx.OnAccount > 0m) await EnsureCreditLimitAsync(ctx.Customer, ctx.OnAccount, ct);

        var keys       = new List<(Guid, Guid, Guid?)>();
        var stockLines = PlanStock(sale, resolved, register.WarehouseId, keys);

        if (sale.Mode == LegalDocumentMode.EArchiveInvoice) {
            await PostInvoiceAsync(sale, ctx, ct);
        } else {
            await PostFiscalAsync(sale, ctx, cashNet, ct);
        }

        // Tüm stok anahtarları tek çağrıda (seri numarası kilidinden sonra), bakiye okumalarından önce. Değişimde iade ile birlikte çağıran önceden kilitler.
        if (keys.Count > 0 && !ctx.StockLocked) await db.LockStockAsync(keys, ct);
        if (stockLines.Count > 0) {
            await stock.IssueAsync(MovementType.PosSale, StockSource, sale.Id, date, stockLines, ct);
            await SetCostsAsync(sale, ct);
        }

        outbox.Enqueue("pos.sale-completed", new { saleId = sale.Id, number = sale.Number, total, registerId = register.Id, mode = sale.Mode.ToString(), invoiceId = sale.InvoiceId });
        await db.SaveChangesAsync(ct);
        return sale;
    }

    private async Task PostFiscalAsync(PosSale sale, PosSaleContext ctx, decimal cashNet, CancellationToken ct) {
        var movements = new List<FinancePoster.PosMovement>();
        if (cashNet > 0m) movements.Add(new FinancePoster.PosMovement(ctx.Register.CashAccountId, PaymentMethod.Cash, cashNet));
        if (ctx.Card > 0m) movements.Add(new FinancePoster.PosMovement(ctx.Register.CardAccountId, PaymentMethod.CreditCard, ctx.Card));
        await finance.PostPosMovementsAsync(sale.Number, sale.Date, movements, $"POS satış {sale.Number}", ct);

        // Veresiye: müşterinin cari defterine borç (yazar kasa modunda fatura yoktur).
        if (ctx.OnAccount > 0m) {
            TreasuryPosting.CariEntry(db, ctx.Customer, true, ctx.OnAccount, sale.Date, LedgerEntrySource.PosSale, sale.Id, sale.Number, $"POS veresiye satış {sale.Number}");
            await db.SaveChangesAsync(ct);
            await allocator.RebuildAsync(ctx.Customer.Id, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task PostInvoiceAsync(PosSale sale, PosSaleContext ctx, CancellationToken ct) {
        var body = new InvoiceInput {
            Kind = InvoiceKind.Sales, BranchId = sale.BranchId, CurrentAccountId = ctx.Customer.Id, Date = sale.Date, Currency = "TRY", AffectsStock = false,
            Notes = $"POS satış {sale.Number}",
            Lines = ctx.Resolved.Lines.Select(l => new LineInput {
                ProductId = l.Product.Id, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit, Quantity = l.Quantity, UnitPrice = l.UnitPrice, VatIncluded = false,
                DiscountAmount = l.Discount + l.CampaignDiscount > 0m ? l.Discount + l.CampaignDiscount : null, VatRate = l.VatRate
            }).ToList()
        };

        // Sistem üretimi: iskonto politikası POS'ta elle iskontoya ayrıca uygulanmıştır (kampanya kasiyerin sınırına bağlı değildir).
        var built = await invoiceBuilder.BuildAsync(body, null, false, ct, systemGenerated: true);
        if (built.Totals.Payable != sale.Total || built.Totals.Vat != sale.VatTotal)
            throw PosErrors.Conflict("pos_invoice_mismatch", "Fatura tutarı satış tutarıyla uyuşmuyor; satış kaydedilmedi.");

        var draft = new Invoice { Status = InvoiceStatus.Draft, SourceType = PosInvoiceHook.SaleSource, SourceId = sale.Id };
        InvoiceBuilder.Apply(draft, body, built);
        foreach (var line in built.Lines) {
            line.InvoiceId = draft.Id;
            draft.Lines.Add(line);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = draft.Id;
            draft.DueSchedule.Add(due);
        }

        db.Invoices.Add(draft);
        await db.SaveChangesAsync(ct);

        var (invoice, _) = await issuer.IssueAsync(draft.Id, false, ct, ctx.Reserved);
        sale.InvoiceId = invoice.Id;

        // Tahsilatlar: nakit (para üstü düşülmüş) ve kart, fatura borcunu kapatır; veresiye açık kalır.
        if (sale.CashNet > 0m) await CollectAsync(sale, ctx.Customer.Id, ctx.Register.CashAccountId, PaymentMethod.Cash, sale.CashNet, ct);
        if (sale.CardAmount > 0m) await CollectAsync(sale, ctx.Customer.Id, ctx.Register.CardAccountId, PaymentMethod.CreditCard, sale.CardAmount, ct);
    }

    public static string CollectionNote(string number) {
        return $"POS {number}";
    }

    private async Task CollectAsync(PosSale sale, Guid customerId, Guid accountId, PaymentMethod method, decimal amount, CancellationToken ct) {
        await finance.PostInTransactionAsync(FinanceDocumentKind.Collection, new PosCollection {
            CurrentAccountId = customerId, AccountId = accountId, Method = method, Date = sale.Date, Amount = amount, Description = CollectionNote(sale.Number)
        }, null, null, ct);
    }

    private sealed record PosCollection : PaymentInput;

    private async Task EnsureCreditLimitAsync(CurrentAccount customer, decimal onAccount, CancellationToken ct) {
        if (customer.CreditLimit is null) return;

        var balance = Statement.Balance(await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == customer.Id).ToListAsync(ct));
        if (CreditExposure.NeedsApproval(customer.CreditLimit, balance, 0m, onAccount) && !session.HasPermission(SalesPermissions.LimitOverride))
            throw PosErrors.Conflict("limit_approval_required", "Müşterinin kredi limiti aşılıyor; satış için limit aşımı onayı gerekir.");
    }

    // ---- stok ----------------------------------------------------------------------------------------------------

    /// <summary>Çözülmüş satırların stok kilit anahtarları (değişimde iade ile birlikte önceden kilitlenir).</summary>
    public static void AddStockKeys(ResolvedSale resolved, Guid warehouse, List<(Guid, Guid, Guid?)> keys) {
        foreach (var l in resolved.Lines) {
            if (l.Product.Kind == ProductKind.Service || l.BaseQuantity <= 0m) continue;
            if (l.Product.IsBundle) keys.AddRange(l.Product.BundleComponents.Select(c => (warehouse, c.ProductId, c.VariantId)));
            else keys.Add((warehouse, l.Product.Id, l.VariantId));
        }
    }

    private static List<StockLine> PlanStock(PosSale sale, ResolvedSale resolved, Guid warehouse, List<(Guid, Guid, Guid?)> keys) {
        var lines = new List<StockLine>();
        foreach (var l in resolved.Lines) {
            var product = l.Product;
            if (product.Kind == ProductKind.Service) continue;
            var baseQuantity = l.BaseQuantity;
            if (baseQuantity <= 0m) continue;

            var saleLine = sale.Lines.First(x => x.Sort == l.Sort);
            lines.Add(new StockLine(warehouse, product.Id, l.VariantId, baseQuantity, null, null, null, saleLine.Id));
            if (product.IsBundle) keys.AddRange(product.BundleComponents.Select(c => (warehouse, c.ProductId, c.VariantId)));
            else keys.Add((warehouse, product.Id, l.VariantId));
        }

        return lines;
    }

    /// <summary>Satırın birim maliyeti (temel birim): stok çıkışının toplam maliyeti / temel miktar. İade bu maliyetle girer.</summary>
    private async Task SetCostsAsync(PosSale sale, CancellationToken ct) {
        var rows = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSource && m.SourceId == sale.Id && m.Quantity < 0m)
                           .Select(m => new { m.SourceLineId, m.TotalCost }).ToListAsync(ct);
        foreach (var line in sale.Lines) {
            var cost = rows.Where(r => r.SourceLineId == line.Id).Sum(r => Math.Abs(r.TotalCost));
            var qty  = Math.Round(line.Quantity * line.Factor, 6, MidpointRounding.AwayFromZero);
            line.UnitCost = qty <= 0m ? 0m : Math.Round(cost / qty, 4, MidpointRounding.AwayFromZero);
        }
    }

    // ---- numara, ödeme ------------------------------------------------------------------------------------------------

    /// <summary>PS{kasaKodu}{yyyyMMdd}{0001}: kasa ve gün başına sıra. Vardiya kilidi altında çağrılır; iptal edilen fişlerin numarası kullanılmış kalır.</summary>
    private async Task<string> NextNumberAsync(Register register, DateOnly date, CancellationToken ct) {
        var count = await db.PosSales.CountAsync(s => s.RegisterId == register.Id && s.Date == date, ct);
        return $"PS{register.Code}{date:yyyyMMdd}{count + 1:0000}";
    }

    public static (decimal Cash, decimal Card, decimal OnAccount) NormalizePayments(IReadOnlyList<SalePaymentInput> payments) {
        decimal cash = 0m, card = 0m, onAccount = 0m;
        foreach (var p in payments) {
            if (p.Amount <= 0m || Math.Round(p.Amount, 2) != p.Amount) throw PosErrors.Bad("payment_invalid", "Ödeme tutarı sıfırdan büyük olmalı ve en fazla 2 ondalık basamak içermelidir.");
            switch (p.Method) {
                case PosPaymentMethod.Cash:      cash      += p.Amount; break;
                case PosPaymentMethod.Card:      card      += p.Amount; break;
                case PosPaymentMethod.OnAccount: onAccount += p.Amount; break;
                default: throw PosErrors.Bad("payment_invalid", "Ödeme yöntemi nakit, kart ya da veresiye olmalıdır.");
            }
        }

        return (cash, card, onAccount);
    }

    // ---- iptal --------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Aynı gün ve hâlâ açık vardiyadaki satışı iptal eder (aksi halde iade kullanılır): stok bir kez geri alınır, hesap hareketleri ve (e-Arşiv'de) fatura
    ///     ile tahsilatlar iptal edilir. İadesi olan satış iptal edilemez.
    /// </summary>
    public async Task CancelAsync(Guid saleId, string reason, CancellationToken ct) {
        var head = await db.PosSales.AsNoTracking().Where(s => s.Id == saleId)
                           .Select(s => new { s.BranchId, s.ShiftId, s.CurrentAccountId, s.Mode, s.OnAccountAmount }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(saleId.ToString(), "PosSale");
        BranchGuard.Ensure(tenant, head.BranchId);

        try {
            await using var tx = await db.BeginTransactionAsync(ct);
            if ((head.Mode == LegalDocumentMode.EArchiveInvoice || head.OnAccountAmount > 0m) && !await db.LockCurrentAccountAsync(head.CurrentAccountId, ct))
                throw new NotFoundException(head.CurrentAccountId.ToString(), "CurrentAccount");
            if (!await db.LockShiftAsync(head.ShiftId, ct)) throw new NotFoundException(head.ShiftId.ToString(), "Shift");

            var sale = await db.PosSales.Include(s => s.Lines).Include(s => s.Payments).FirstAsync(s => s.Id == saleId, ct);
            if (sale.Status != PosSaleStatus.Completed) throw PosErrors.Conflict("sale_cancelled", "Satış zaten iptal edilmiş.");

            var shift = await db.PosShifts.AsNoTracking().FirstAsync(s => s.Id == sale.ShiftId, ct);
            var today = CurrentAccountClock.Today(time);
            if (sale.Date != today || shift.Status != ShiftStatus.Open)
                throw PosErrors.Conflict("cancel_not_allowed", "Satış yalnızca aynı gün ve açık vardiyada iptal edilebilir; iade yapın.");
            if (await db.PosReturns.AnyAsync(r => r.SaleId == sale.Id, ct)) throw PosErrors.Conflict("sale_has_returns", "Satışın iadesi var; iptal edilemez.");

            var now = time.GetUtcNow();

            // Stok kilitleri tek çağrıda, geri alma yazımlarından önce.
            var keys = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSource && m.SourceId == sale.Id && m.Quantity != 0m)
                               .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId }).Distinct().ToListAsync(ct);
            if (keys.Count > 0) await db.LockStockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId)), ct);

            if (sale.Mode == LegalDocumentMode.EArchiveInvoice) {
                await CancelInvoiceAsync(sale, reason, now, ct);
            } else {
                await finance.CancelPosMovementsAsync(sale.Number, now, reason, ct);
                foreach (var entry in await db.LedgerEntries.Where(e => e.Source == LedgerEntrySource.PosSale && e.SourceId == sale.Id && e.CancelledAt == null).ToListAsync(ct)) {
                    entry.CancelledAt  = now;
                    entry.CancelledBy  = session.UserId?.ToString();
                    entry.CancelReason = reason;
                }

                await db.SaveChangesAsync(ct);
                if (sale.OnAccountAmount > 0m) {
                    await allocator.RebuildAsync(sale.CurrentAccountId, ct);
                    await db.SaveChangesAsync(ct);
                }
            }

            if (keys.Count > 0) await stock.ReverseAsync(StockSource, sale.Id, today, ct);

            sale.Status            = PosSaleStatus.Cancelled;
            sale.CancelReason      = reason;
            sale.CancelledAt       = now;
            sale.CancelledByUserId = session.UserId;
            outbox.Enqueue("pos.sale-cancelled", new { saleId = sale.Id, number = sale.Number, reason });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw PosErrors.VersionConflict();
        }
    }

    private async Task CancelInvoiceAsync(PosSale sale, string reason, DateTimeOffset now, CancellationToken ct) {
        var invoice = sale.InvoiceId is { } id ? await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct) : null;
        if (invoice is { Status: InvoiceStatus.Issued }) {
            invoice.Status       = InvoiceStatus.Cancelled;
            invoice.CancelReason = reason;
            invoice.CancelledAt  = now;
            invoice.LastModified = now;
            scope.Active         = true;
            try {
                await invoicePoster.ReverseAsync(invoice, reason, ct);
            } finally {
                scope.Active = false;
            }
        }

        // Tahsilatlar (nakit/kart) kaynak fiş numarasından bulunur ve geri alınır.
        var note = CollectionNote(sale.Number);
        var docs = await db.FinanceDocuments.AsNoTracking().Where(d => d.Kind == FinanceDocumentKind.Collection && d.CurrentAccountId == sale.CurrentAccountId && d.Description == note && d.CancelledAt == null)
                           .Select(d => d.Id).ToListAsync(ct);
        foreach (var documentId in docs) await finance.CancelPosCollectionAsync(documentId, reason, ct);
        await db.SaveChangesAsync(ct);
    }

    // ---- sorgu ----------------------------------------------------------------------------------------------------

    private async Task<SaleResultDto?> FindReplayAsync(Guid clientSaleId, CancellationToken ct) {
        var id = await db.PosSales.AsNoTracking().Where(s => s.ClientSaleId == clientSaleId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await ResultAsync(id.Value, true, ct);
    }

    public async Task<SaleResultDto> ResultAsync(Guid saleId, bool replayed, CancellationToken ct) {
        var sale = await db.PosSales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments).FirstAsync(s => s.Id == saleId, ct);
        BranchGuard.Ensure(tenant, sale.BranchId);
        var slip = await SlipAsync(sale, ct);
        return new SaleResultDto(sale.Id, sale.Number, sale.Total, sale.ChangeGiven, slip.InvoiceNumber, replayed, slip);
    }

    public async Task<SlipDto> SlipAsync(PosSale sale, CancellationToken ct) {
        var firm     = (await tenantSettings.GetAsync<CompanyProfileSettings>(ct)).LegalName ?? string.Empty;
        var register = await db.PosRegisters.AsNoTracking().Where(r => r.Id == sale.RegisterId).Select(r => new { r.Code, r.Name }).FirstOrDefaultAsync(ct);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var branch   = (await branches.ListAsync(tenantId, ct)).FirstOrDefault(b => b.Id == sale.BranchId)?.Name ?? string.Empty;
        var invoiceNumber = sale.InvoiceId is { } invoiceId ? await db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct) : null;

        // Fişte birim fiyat ve iskontolar KDV dahil gösterilir.
        static decimal Gross(decimal value, int rate) => Math.Round(value * (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);
        var lines = sale.Lines.OrderBy(l => l.Sort)
                        .Select(l => new SlipLineDto(l.Description, l.Quantity, l.Unit, Gross(l.UnitPrice, l.VatRate), Gross(l.Discount, l.VatRate), Gross(l.CampaignDiscount, l.VatRate), l.VatRate, l.Total))
                        .ToList();
        var vat   = sale.Lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key).Select(g => new SlipVatDto(g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat))).ToList();
        var notes = sale.Mode == LegalDocumentMode.FiscalReceipt
                        ? new[] { "Bilgi fişidir. Mali değeri yoktur." }
                        : new[] { "e-Arşiv fatura e-posta/SMS ile iletilecektir." };
        var discount = sale.Lines.Sum(l => Gross(l.Discount, l.VatRate) + Gross(l.CampaignDiscount, l.VatRate));
        var subtotal = sale.Total + discount;

        return new SlipDto(firm, branch, register is null ? string.Empty : $"{register.Code} - {register.Name}", sale.CashierName ?? string.Empty, sale.Number, sale.At, sale.Mode, lines, vat,
                           subtotal, discount, sale.Total, sale.Payments.OrderBy(p => p.Method).Select(p => new SlipPaymentDto(p.Method, p.Amount)).ToList(), sale.ChangeGiven,
                           invoiceNumber, sale.Number, notes);
    }
}
