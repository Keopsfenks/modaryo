namespace ERP.Domain.Treasury;

public static class InstrumentStateMachine {
    static readonly HashSet<(InstrumentDirection, InstrumentStatus, InstrumentStatus)> Allowed = [
        (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.Endorsed),
        (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.InCollection),
        (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.ReturnedToCustomer),
        (InstrumentDirection.Received, InstrumentStatus.InCollection, InstrumentStatus.Collected),
        (InstrumentDirection.Received, InstrumentStatus.InCollection, InstrumentStatus.Bounced),
        (InstrumentDirection.Received, InstrumentStatus.Endorsed, InstrumentStatus.Bounced),
        (InstrumentDirection.Received, InstrumentStatus.Bounced, InstrumentStatus.Collected),
        (InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.Paid),
        (InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.ReturnedFromSupplier)
    ];

    public static bool CanMove(InstrumentDirection d, InstrumentStatus from, InstrumentStatus to) =>
        Allowed.Contains((d, from, to));
}
