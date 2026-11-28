using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Invoicing;
using ERP.Application.Pos.Sales;
using ERP.Application.Pos.Shifts;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Application.Treasury;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Pos;
using ERP.Domain.Treasury;

namespace ERP.Application.Pos.Returns;

public sealed record ReturnLineInput(Guid SaleLineId, decimal Quantity);

public sealed record RefundInput(PosReturnRefundMethod Method, decimal Amount);

/// <summary>
///     İade/değişim girdisi. SaleNumber fiş numarası ya da fiş barkodudur. Refund: iade edilecek tutarın (iade değeri - veresiye payı - değişime mahsup edilen)
///     nakit/kart dağılımı; değişimde ödeme gerekiyorsa ExchangePayments verilir (ikisi birlikte verilemez). RegisterId verilmezse satışın kasası kullanılır;
///     iade, kasiyerin o kasadaki açık vardiyasına yazılır. ClientReturnId isteğe bağlı tek seferlik kimliktir (aynı kimlik yeni iade yazmaz).
/// </summary>
public sealed record ReturnInput(
    Guid?                            ClientReturnId,
    string                           SaleNumber,
    Guid?                            RegisterId,
    IReadOnlyList<ReturnLineInput>   Lines,
    IReadOnlyList<RefundInput>?      Refund,
    IReadOnlyList<SaleLineInput>?    ExchangeLines,
    IReadOnlyList<SalePaymentInput>? ExchangePayments,
    string?                          Reason
);

/// <summary>İade önizlemesi girdisi: gerçek iadenin satır ve değişim girdileriyle aynıdır (dağılım ve ödeme yoktur).</summary>
public sealed record ReturnPreviewInput(string SaleNumber, Guid? RegisterId, IReadOnlyList<ReturnLineInput> Lines, IReadOnlyList<SaleLineInput>? ExchangeLines);

public sealed record ReturnPreviewLineDto(Guid SaleLineId, decimal Quantity, decimal Amount, decimal Net);

/// <summary>
///     İade önizlemesi: gerçek iadeyle aynı hesap. ReturnValue iade değeri (KDV dahil), OnAccountShare müşteri borcundan düşen veresiye payı, ExchangeCredit değişime
///     mahsup edilen, PayDue değişimde müşteriden alınacak fark (exchangePayments toplamı), RefundDue nakit/karta iade edilecek tutar (refund toplamı).
/// </summary>
public sealed record ReturnPreviewDto(
    decimal                            ReturnValue,
    decimal                            ReturnNet,
    decimal                            OnAccountShare,
    decimal                            ExchangeCredit,
    decimal                            PayDue,
    decimal                            RefundDue,
    IReadOnlyList<ReturnPreviewLineDto> Lines,
    SalePreviewDto?                    Exchange
);

public sealed record ReturnResultDto(
    Guid            Id,
    string          Number,
    decimal         ReturnValue,
    decimal         CashRefund,
    decimal         CardRefund,
    decimal         OnAccountCredit,
    decimal         ExchangeCredit,
    decimal         Difference,
    Guid?           ReturnInvoiceId,
    bool            Replayed,
    IReadOnlyList<string> Warnings,
    SaleResultDto?  ExchangeSale
);

/// <summary>
///     POS iade ve değişim yazıcısı. İade fiş numarasıyla yapılır; miktar, satılan - daha önce iade edilenden fazla olamaz (kilit altında denetlenir). İade değeri satırın
///     fiş tutarından orantılı hesaplanır (son parçada kalan tutar, kuruş kayması olmaz). Stok satış anındaki birim maliyetle kasanın deposuna girer.
///     Yazar kasa modunda hesap hareketi (nakit/kart çıkışı) ve veresiye payı kadar cari alacak yazılır; e-Arşiv modunda iade tutarı ödeme belgesiyle çıkar ve
///     P4 SalesReturn taslağı oluşturulur (kesim muhasebecidedir). Değişim: iade + yeni satış tek işlemde; fark ödenir ya da iade edilir.
///     Kilit sırası: cari, vardiya, seri numarası (e-Arşiv değişimi), stok (iade ve değişim anahtarları tek çağrıda).
/// </summary>
public sealed class ReturnPoster(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    TimeProvider          time,
    ITenantSettings       tenantSettings,
    PosSetup              setup,
    PosSalePoster         sales,
    PosSaleResolver       resolver,
    FinancePoster         finance,
    InvoiceBuilder        invoiceBuilder,
    InvoiceIssuer         issuer,
    IStockService         stock,
    AutoAllocator         allocator,
    IOutbox               outbox
) {
    public const string StockSource = "pos-return";

    public async Task<ReturnResultDto> CreateAsync(ReturnInput input, CancellationToken ct) {
        if (input.ClientReturnId is { } clientId && await FindReplayAsync(clientId, ct) is { } replay) return replay;

        var number = input.SaleNumber.Trim().ToUpperInvariant();
        var head   = await db.PosSales.AsNoTracking().Where(s => s.Number == number).Select(s => new { s.Id, s.BranchId, s.RegisterId, s.CurrentAccountId, s.Mode, s.OnAccountAmount }).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException(input.SaleNumber, "PosSale");
        BranchGuard.Ensure(tenant, head.BranchId);

        var registerId = input.RegisterId ?? head.RegisterId;
        var register   = await db.PosRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registerId, ct) ?? throw new NotFoundException(registerId.ToString(), "Register");
        BranchGuard.Ensure(tenant, register.BranchId);
        if (!register.IsActive) throw PosErrors.RegisterInactive();

        var lineIds = input.Lines.Select(l => l.SaleLineId).ToList();
        if (lineIds.Count == 0) throw PosErrors.Bad("return_empty", "İade için en az bir satır seçilmelidir.");
        if (lineIds.Distinct().Count() != lineIds.Count) throw PosErrors.Bad("duplicate_line", "Aynı fiş satırı bir iadede yalnızca bir kez yer alabilir.");
        foreach (var l in input.Lines)
            if (l.Quantity <= 0m || Math.Round(l.Quantity, 6) != l.Quantity) throw PosErrors.Bad("quantity_invalid", "İade miktarı sıfırdan büyük olmalı ve en fazla 6 ondalık basamak içermelidir.");

        var settings   = (PosSettings)(await setup.EnsureAsync(ct)).Value;
        var exchanging = input.ExchangeLines is { Count: > 0 };
        if (!exchanging && input.ExchangePayments is { Count: > 0 }) throw PosErrors.Bad("settlement_invalid", "Değişim satırı olmadan değişim ödemesi verilemez.");
        if (exchanging && settings.LegalDocumentMode == LegalDocumentMode.EArchiveInvoice && !(await tenantSettings.GetAsync<EBelgeSettings>(ct)).Enabled) throw PosErrors.EBelgeRequired();

        var date     = CurrentAccountClock.Today(time);
        var customer = await db.CurrentAccounts.AsNoTracking().FirstAsync(c => c.Id == head.CurrentAccountId, ct);
        var exchange = exchanging ? await resolver.ResolveAsync(input.ExchangeLines!, customer.Id, date, ct) : null;

        try {
            await using var tx = await db.BeginTransactionAsync(ct);

            var exchangeInvoice = exchanging && settings.LegalDocumentMode == LegalDocumentMode.EArchiveInvoice;
            var retail          = customer.Id == settings.RetailCustomerId;
            var needsCari       = (head.Mode == LegalDocumentMode.EArchiveInvoice || head.OnAccountAmount > 0m || exchangeInvoice) && !retail;
            if (retail) db.CariLockExemptions.Add(customer.Id);   // perakende cari: kilit ve yeniden eşleştirme yok (PosSalePoster ile aynı)
            if (needsCari && !await db.LockCurrentAccountAsync(customer.Id, ct)) throw new NotFoundException(customer.Id.ToString(), "CurrentAccount");
            var shift = await sales.LockOpenShiftAsync(register.Id, ct);

            // Fişin satırı kilitlenir: aynı fişe başka kasadan gelen iade ve iptal sıraya girer (iade edilmiş miktar ve durum kilit altında okunur).
            // Sıra: cari, vardiya, satış satırı, seri numarası, stok.
            if (!await db.LockPosSaleAsync(head.Id, ct)) throw new NotFoundException(head.Id.ToString(), "PosSale");

            if (input.ClientReturnId is { } raced && await FindReplayAsync(raced, ct) is { } existing) return existing;

            // Kilit altında okunan satış: iade edilmiş miktarlar güncel.
            var sale = await db.PosSales.Include(s => s.Lines).FirstAsync(s => s.Id == head.Id, ct);
            if (sale.Status != PosSaleStatus.Completed) throw PosErrors.Conflict("sale_cancelled", "Satış iptal edilmiş; iade yapılamaz.");

            // Önizlemeyle aynı hesap (PickLines, ValueAsync, Settle).
            var picked = PickLines(sale, input.Lines);
            var values = await ValueAsync(picked, ct);
            var net    = values.Sum(v => v.Net);
            var total  = values.Sum(v => v.Amount);
            var (share, credit, payDue, refundDue) = Settle(sale, total, exchange?.Total);

            var refunds = (input.Refund ?? []).ToList();
            foreach (var r in refunds)
                if (r.Amount <= 0m || Math.Round(r.Amount, 2) != r.Amount || !Enum.IsDefined(r.Method)) throw PosErrors.Bad("refund_invalid", "İade tutarı sıfırdan büyük olmalı ve en fazla 2 ondalık basamak içermelidir.");
            var cashRefund = refunds.Where(r => r.Method == PosReturnRefundMethod.Cash).Sum(r => r.Amount);
            var cardRefund = refunds.Where(r => r.Method == PosReturnRefundMethod.Card).Sum(r => r.Amount);
            if (cashRefund + cardRefund != refundDue) throw PosErrors.Bad("refund_mismatch", $"İade dağılımı toplamı {refundDue:0.00} olmalıdır.");

            decimal payCash = 0m, payCard = 0m;
            var     payments = input.ExchangePayments ?? [];
            if (payDue > 0m) {
                if (refunds.Count > 0) throw PosErrors.Bad("settlement_invalid", "Fark ödenecekse iade dağılımı verilemez.");
                var (cash, card, onAccount) = PosSalePoster.NormalizePayments(payments);
                if (onAccount > 0m) throw PosErrors.Bad("payment_invalid", "Değişimde veresiye ödeme yapılamaz.");
                (payCash, payCard) = (cash, card);
            } else if (payments.Count > 0) {
                throw PosErrors.Bad("settlement_invalid", "Ödenecek fark yok; değişim ödemesi verilemez.");
            }

            // Stok: ürünlü ve hizmet olmayan satırlar kasanın deposuna satış maliyetiyle girer.
            var productIds = picked.Select(p => p.Line.ProductId).Distinct().ToList();
            var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var ret = new PosReturn {
                RegisterId = register.Id, ShiftId = shift.Id, SaleId = sale.Id, CashierUserId = session.UserId ?? throw new ForbiddenAccessException(), CashierName = tenant.UserFullName,
                Date = date, At = time.GetUtcNow(), Mode = sale.Mode, Net = net, Vat = total - net, RefundAmount = cashRefund + cardRefund,
                RefundMethod = cashRefund > 0m || cardRefund == 0m ? PosReturnRefundMethod.Cash : PosReturnRefundMethod.Card, CashRefund = cashRefund, CardRefund = cardRefund,
                OnAccountCredit = share, ExchangeCredit = credit, Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(), ClientReturnId = input.ClientReturnId
            };
            ret.Number = await NextNumberAsync(register, date, ct);

            var stockLines = new List<StockLine>();
            var keys       = new List<(Guid, Guid, Guid?)>();
            foreach (var (line, quantity, value) in picked.Select((p, i) => (p.Line, p.Quantity, values[i]))) {
                ret.Lines.Add(new PosReturnLine { ReturnId = ret.Id, SaleLineId = line.Id, Quantity = quantity, Amount = value.Amount, Net = value.Net, Vat = value.Amount - value.Net });
                var product = products[line.ProductId];
                if (product.Kind == ProductKind.Service) continue;
                if (product.IsBundle) throw PosErrors.Bad("bundle_return_unsupported", $"\"{line.Description}\" set ürünüdür; bileşenleri ayrı iade edilmelidir.");
                stockLines.Add(new StockLine(register.WarehouseId, product.Id, line.VariantId, Math.Round(quantity * line.Factor, 6, MidpointRounding.AwayFromZero), line.UnitCost, null, null, line.Id));
                keys.Add((register.WarehouseId, product.Id, line.VariantId));
            }

            // e-Arşiv değişimi: numara (seri kilidi) stok kilitlerinden önce ayrılır.
            ReservedNumber? reserved = null;
            if (exchangeInvoice) reserved = await issuer.ReserveNumberAsync(register.BranchId, InvoiceKind.Sales, date, customer, ct);
            if (exchange is not null) PosSalePoster.AddStockKeys(exchange, register.WarehouseId, keys);
            if (keys.Count > 0) await db.LockStockAsync(keys, ct);

            db.PosReturns.Add(ret);
            await db.SaveChangesAsync(ct);   // tekil ClientReturnId ve numara dizinleri

            foreach (var (line, quantity) in picked) line.ReturnedQuantity += quantity;
            if (stockLines.Count > 0) await stock.ReceiveAsync(MovementType.PosReturn, StockSource, ret.Id, date, stockLines, ct);

            if (sale.Mode == LegalDocumentMode.EArchiveInvoice) await PostInvoiceSideAsync(ret, sale, picked, customer, register, ct);
            else await PostFiscalSideAsync(ret, sale, customer, register, share, ct);

            var warnings = new List<string>();
            if (cashRefund > 0m) await EnsureCashAsync(shift, settings, warnings, ct);

            SaleResultDto? exchangeResult = null;
            if (exchange is not null) {
                var exchangeSale = await sales.PostAsync(new PosSaleContext(register, shift, settings, customer, Guid.CreateVersion7(), exchange, payCash, payCard, 0m, credit, date, reserved, StockLocked: true), ct);
                ret.ExchangeSaleId = exchangeSale.Id;
                await db.SaveChangesAsync(ct);
                exchangeResult = await sales.ResultAsync(exchangeSale.Id, false, ct);
            }

            outbox.Enqueue("pos.return-created", new { returnId = ret.Id, number = ret.Number, saleId = sale.Id, total, exchangeSaleId = ret.ExchangeSaleId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ToResult(ret, total, false, warnings, exchangeResult);
        } catch (DbUpdateException ex) when (PosErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            if (input.ClientReturnId is { } clientId2 && await FindReplayAsync(clientId2, ct) is { } existing) return existing;
            throw PosErrors.Conflict("number_conflict", "İade kaydı başka bir belgeyle çakıştı; tekrar deneyin.");
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw PosErrors.VersionConflict();
        }
    }

    /// <summary>Seçilen satırları fişe karşı doğrular (fişe ait, kalan miktarı aşmayan). Kilit altında (gerçek iade) ya da kilitsiz (önizleme) çağrılır.</summary>
    private static List<(PosSaleLine Line, decimal Quantity)> PickLines(PosSale sale, IReadOnlyList<ReturnLineInput> lines) {
        var picked = new List<(PosSaleLine Line, decimal Quantity)>();
        foreach (var l in lines) {
            var line = sale.Lines.FirstOrDefault(x => x.Id == l.SaleLineId) ?? throw PosErrors.Bad("return_line_invalid", "İade satırı fişe ait değil.");
            var left = line.Quantity - line.ReturnedQuantity;
            if (l.Quantity > left) throw PosErrors.Bad("return_exceeds_sold", $"\"{line.Description}\" için iade edilebilecek en fazla miktar {Math.Max(0m, left):0.######}.");
            picked.Add((line, l.Quantity));
        }

        return picked;
    }

    /// <summary>Değer dağılımı: veresiye payı müşteri borcundan düşer; kalan değişime mahsup edilir; artan nakit/karta iade edilir.</summary>
    private static (decimal Share, decimal Credit, decimal PayDue, decimal RefundDue) Settle(PosSale sale, decimal total, decimal? exchangeTotal) {
        var share     = sale.OnAccountAmount > 0m && sale.Total > 0m ? Math.Min(total, Math.Round(total * sale.OnAccountAmount / sale.Total, 2, MidpointRounding.AwayFromZero)) : 0m;
        var cashable  = total - share;
        var credit    = exchangeTotal is null ? 0m : Math.Min(cashable, exchangeTotal.Value);
        var payDue    = exchangeTotal is null ? 0m : exchangeTotal.Value - credit;
        return (share, credit, payDue, cashable - credit);
    }

    /// <summary>
    ///     POST /returns/preview: gerçek iadeyle aynı satır seçimi, değer hesabı, mahsup ve (varsa) değişim satırı çözümü; kilit almaz, hiçbir şey yazmaz.
    ///     İade kasası ve vardiya gerekmez (yalnızca fişin şubesine erişim denetlenir).
    /// </summary>
    public async Task<ReturnPreviewDto> PreviewAsync(ReturnPreviewInput input, CancellationToken ct) {
        var number = input.SaleNumber.Trim().ToUpperInvariant();
        var sale   = await db.PosSales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Number == number, ct) ?? throw new NotFoundException(input.SaleNumber, "PosSale");
        BranchGuard.Ensure(tenant, sale.BranchId);
        if (sale.Status != PosSaleStatus.Completed) throw PosErrors.Conflict("sale_cancelled", "Satış iptal edilmiş; iade yapılamaz.");
        if (input.Lines.Count == 0) throw PosErrors.Bad("return_empty", "İade için en az bir satır seçilmelidir.");
        if (input.Lines.Select(l => l.SaleLineId).Distinct().Count() != input.Lines.Count) throw PosErrors.Bad("duplicate_line", "Aynı fiş satırı bir iadede yalnızca bir kez yer alabilir.");
        foreach (var l in input.Lines)
            if (l.Quantity <= 0m || Math.Round(l.Quantity, 6) != l.Quantity) throw PosErrors.Bad("quantity_invalid", "İade miktarı sıfırdan büyük olmalı ve en fazla 6 ondalık basamak içermelidir.");

        var picked   = PickLines(sale, input.Lines);
        var values   = await ValueAsync(picked, ct);
        var total    = values.Sum(v => v.Amount);
        var exchange = input.ExchangeLines is { Count: > 0 } ? await resolver.ResolveAsync(input.ExchangeLines, sale.CurrentAccountId, CurrentAccountClock.Today(time), ct) : null;
        var (share, credit, payDue, refundDue) = Settle(sale, total, exchange?.Total);
        return new ReturnPreviewDto(total, values.Sum(v => v.Net), share, credit, payDue, refundDue,
                                    picked.Select((p, i) => new ReturnPreviewLineDto(p.Line.Id, p.Quantity, values[i].Amount, values[i].Net)).ToList(),
                                    exchange is null ? null : PosSalePoster.ToPreview(exchange));
    }

    private sealed record Value(decimal Amount, decimal Net);

    /// <summary>İade değeri: satır tutarından orantılı; satırın kalan son miktarı iade edilirse kalan tutar (kuruş kayması olmaz).</summary>
    private async Task<List<Value>> ValueAsync(List<(PosSaleLine Line, decimal Quantity)> picked, CancellationToken ct) {
        var ids   = picked.Select(p => p.Line.Id).ToList();
        var prior = await db.PosReturnLines.AsNoTracking().Where(l => ids.Contains(l.SaleLineId)).Select(l => new { l.SaleLineId, l.Amount, l.Net }).ToListAsync(ct);
        var result = new List<Value>();
        foreach (var (line, quantity) in picked) {
            var left = line.Quantity - line.ReturnedQuantity;
            if (quantity == left) {
                result.Add(new Value(line.Total - prior.Where(p => p.SaleLineId == line.Id).Sum(p => p.Amount), line.Net - prior.Where(p => p.SaleLineId == line.Id).Sum(p => p.Net)));
            } else {
                result.Add(new Value(Math.Round(line.Total * quantity / line.Quantity, 2, MidpointRounding.AwayFromZero), Math.Round(line.Net * quantity / line.Quantity, 2, MidpointRounding.AwayFromZero)));
            }
        }

        return result;
    }

    private async Task PostFiscalSideAsync(PosReturn ret, PosSale sale, CurrentAccount customer, Register register, decimal share, CancellationToken ct) {
        var movements = new List<FinancePoster.PosMovement>();
        if (ret.CashRefund > 0m) movements.Add(new FinancePoster.PosMovement(register.CashAccountId, PaymentMethod.Cash, -ret.CashRefund));
        if (ret.CardRefund > 0m) movements.Add(new FinancePoster.PosMovement(register.CardAccountId, PaymentMethod.CreditCard, -ret.CardRefund));
        await finance.PostPosMovementsAsync(ret.Number, ret.Date, movements, $"POS iade {ret.Number}", ct);

        if (share > 0m) {
            TreasuryPosting.CariEntry(db, customer, false, share, ret.Date, LedgerEntrySource.PosSale, ret.Id, ret.Number, $"POS iade {ret.Number} (veresiye)");
            await db.SaveChangesAsync(ct);
            await allocator.RebuildAsync(customer.Id, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed record PosPayout : PaymentInput;

    /// <summary>e-Arşiv: nakit/kart iadesi müşteriye ödeme belgesiyle çıkar; P4 SalesReturn taslağı oluşturulur (kesilmez, stok etkisi yok: stok iade belgesinde girdi).</summary>
    private async Task PostInvoiceSideAsync(PosReturn ret, PosSale sale, List<(PosSaleLine Line, decimal Quantity)> picked, CurrentAccount customer, Register register, CancellationToken ct) {
        if (ret.CashRefund > 0m) await PayOutAsync(ret, customer.Id, register.CashAccountId, PaymentMethod.Cash, ret.CashRefund, ct);
        if (ret.CardRefund > 0m) await PayOutAsync(ret, customer.Id, register.CardAccountId, PaymentMethod.CreditCard, ret.CardRefund, ct);

        if (sale.InvoiceId is not { } invoiceId) return;
        var original = await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstAsync(i => i.Id == invoiceId, ct);
        var lines = new List<LineInput>();
        foreach (var (line, quantity) in picked) {
            var source = original.Lines.First(l => l.Sort == line.Sort);
            var ratio  = source.Quantity == 0m ? 0m : quantity / source.Quantity;
            lines.Add(new LineInput {
                ProductId = source.ProductId, VariantId = source.VariantId, Description = source.Description, Unit = source.Unit, Quantity = quantity, UnitPrice = source.UnitPrice,
                VatIncluded = source.VatIncluded, DiscountAmount = source.DiscountAmount is { } d ? Math.Round(d * ratio, 2, MidpointRounding.AwayFromZero) : null, VatRate = source.VatRate,
                SourceLineId = source.Id
            });
        }

        var body = new InvoiceInput {
            Kind = InvoiceKind.SalesReturn, BranchId = sale.BranchId, CurrentAccountId = sale.CurrentAccountId, Date = ret.Date, Currency = "TRY", AffectsStock = false,
            ReturnOfInvoiceId = original.Id, Notes = $"POS iade: {sale.Number}", Lines = lines
        };
        var built = await invoiceBuilder.BuildAsync(body, null, false, ct, systemGenerated: true);
        var draft = new Invoice { Status = InvoiceStatus.Draft, SourceType = PosInvoiceHook.ReturnSource, SourceId = ret.Id };
        InvoiceBuilder.Apply(draft, body, built);
        foreach (var l in built.Lines) {
            l.InvoiceId = draft.Id;
            draft.Lines.Add(l);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = draft.Id;
            draft.DueSchedule.Add(due);
        }

        db.Invoices.Add(draft);
        ret.ReturnInvoiceId = draft.Id;
        await db.SaveChangesAsync(ct);
    }

    private Task PayOutAsync(PosReturn ret, Guid customerId, Guid accountId, PaymentMethod method, decimal amount, CancellationToken ct) {
        return finance.PostInTransactionAsync(FinanceDocumentKind.Payment, new PosPayout {
            CurrentAccountId = customerId, AccountId = accountId, Method = method, Date = ret.Date, Amount = amount, Description = $"POS iade {ret.Number}"
        }, null, null, ct);
    }

    /// <summary>Nakit iade beklenen nakdi eksiye düşürürse ayara göre uyarı ya da 409.</summary>
    private async Task EnsureCashAsync(Shift shift, PosSettings settings, List<string> warnings, CancellationToken ct) {
        var expected = (await ShiftRules.TotalsAsync(db, shift, ct)).ExpectedCash;
        if (expected >= 0m) return;
        if (!settings.AllowNegativeCashWarningOnly) throw PosErrors.Conflict("cash_negative", "Kasada yeterli nakit yok.");
        warnings.Add("cash_negative");
    }

    private async Task<string> NextNumberAsync(Register register, DateOnly date, CancellationToken ct) {
        var count = await db.PosReturns.CountAsync(r => r.RegisterId == register.Id && r.Date == date, ct);
        return $"PI{register.Code}{date:yyyyMMdd}{count + 1:0000}";
    }

    private async Task<ReturnResultDto?> FindReplayAsync(Guid clientReturnId, CancellationToken ct) {
        var ret = await db.PosReturns.AsNoTracking().FirstOrDefaultAsync(r => r.ClientReturnId == clientReturnId, ct);
        if (ret is null) return null;

        var sale = await db.PosSales.AsNoTracking().Where(s => s.Id == ret.SaleId).Select(s => s.BranchId).FirstAsync(ct);
        BranchGuard.Ensure(tenant, sale);
        var exchange = ret.ExchangeSaleId is { } id ? await sales.ResultAsync(id, true, ct) : null;
        return ToResult(ret, ret.Net + ret.Vat, true, [], exchange);
    }

    private static ReturnResultDto ToResult(PosReturn ret, decimal total, bool replayed, IReadOnlyList<string> warnings, SaleResultDto? exchange) {
        var difference = exchange is null ? -(total - ret.OnAccountCredit) : exchange.Total - ret.ExchangeCredit - ret.RefundAmount;
        return new ReturnResultDto(ret.Id, ret.Number, total, ret.CashRefund, ret.CardRefund, ret.OnAccountCredit, ret.ExchangeCredit, difference, ret.ReturnInvoiceId, replayed, warnings, exchange);
    }
}
