namespace ERP.Domain.CurrentAccounts;

/// <summary>
///     Bir hareketin vade satırı. Bugün her hareketin tek satırı vardır (Sequence = 1);
///     taksitli hareketler 1..n satır üretecektir.
/// </summary>
public class LedgerDueLine : BaseEntity, ITenantOwned {
    public Guid         EntryId          { get; set; }
    public Guid         CurrentAccountId { get; set; }
    public DateOnly     DueDate          { get; set; }
    public decimal      Amount           { get; set; }
    public DueDirection Direction        { get; set; }
    public int          Sequence         { get; set; } = 1;
    public Guid         TenantId         { get; set; }

    /// <summary>Hareketin tek vade satırı: tutar borç ya da alacak, vade DueDate ?? Date.</summary>
    public static LedgerDueLine ForEntry(LedgerEntry entry) {
        var receivable = entry.Debit > 0;
        return new LedgerDueLine {
                                     EntryId          = entry.Id,
                                     CurrentAccountId = entry.CurrentAccountId,
                                     DueDate          = entry.DueDate ?? entry.Date,
                                     Amount           = receivable ? entry.Debit : entry.Credit,
                                     Direction        = receivable ? DueDirection.Receivable : DueDirection.Payable,
                                     Sequence         = 1,
                                     TenantId         = entry.TenantId
                                 };
    }
}
