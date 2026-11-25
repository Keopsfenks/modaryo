# P6 fix wave B: stock-side review fixes (C2, I2, I3, I5)

## C2 cost correction follows the received quantity
- **Replay (P3 `CostReplay.Run`)**: a Revaluation movement (`correction:{inboundId}`) is no longer applied at its own (Date, Sequence) position. It is applied right after the inbound movement it corrects, so same-day outflows (quarantine release TransferOut, sales, issues) are re-costed. Weighted average: the delta is added to the running value at the inbound's position. FIFO: the layer's unit cost is raised at that position, so the layer's quantity consumed later carries the correction. A correction whose inbound is not in the replayed list keeps the old behaviour.
- Because `RevaluationRunner` already propagates re-costed TransferOut cost to the TransferIn and enqueues the destination job, the released warehouse gets the corrected cost (also across several hops).
- **Live write (`StockLedger.CorrectCostAsync`)**: the `cost_correction_no_stock` guard (stock at end of the correction date) is removed. It was the cause of the 409 on invoice issue, landed-cost post and the cancel paths. `InventoryErrors.CostCorrectionNoStock` is now unused. The P3 test `CorrectCost_NoStock_Returns409` was changed to `CorrectCost_AfterStockWasFullyIssued_IsAccepted` (new semantics).
- No P6 code change was needed: `CostCorrection` already writes through P3 at the receipt date.

## I2
`CancelReceiptCommandHandler`: 409 `receipt_has_landed_cost` when any target of a Posted landed cost is in the receipt; 409 `receipt_processed` when the Revaluation movements (`correction:{inbound}`) of the receipt's inbound movements have a non-zero net. A cancelled landed cost nets to zero, so the receipt can be cancelled again after the landed cost is cancelled.

## I3
- `ReceiptLineStock.ReturnableAsync` = received - returned - rejected-to-Damaged (`RejectSource` TransferIn quantity).
- Per-release destination tracking is derived from the ledger, not from `TargetWarehouseId`: `ReleasedHeldAsync` = quantity transferred in by each `ReleaseSource` movement per warehouse minus returns (`ReturnSource`) from that warehouse, oldest release first. `PurchaseReturnService` takes the quarantine remainder first, then these warehouses in order (a legacy fallback to `TargetWarehouseId` covers leftovers with no release movement). Tracked items still must come from one warehouse (`return_split_tracked`).
- `POST /returns` locks the release destinations too (`ReleaseKeysAsync`). Known small hole: a release to a brand-new warehouse committed between the key read and the lock is locked late by the issue itself (re-entrant advisory lock, no known cycle).
- `TargetWarehouseId` still records the last release target (display only).

## I5
`CancelPoCommandHandler` now mirrors Close: transaction, `LockPurchaseOrderAsync`, re-read, receipts check, stock locks for the reservation warehouses, `ReleaseExpectedAsync(po.Id)`, outbox `purchase.order-cancelled` (`po_id, number, reason, lines[], released_reservations[]`). Documented in `contracts/README.md`.

## Tests (new, `Purchasing/StockSideFixTests.cs`, 10 cases)
- LandedCost_SameDayQuarantineRelease_FollowsReleasedStock (WA and FIFO)
- InvoicePriceDifference_SameDayQuarantineRelease_FollowsReleasedStock
- LandedCost_SameDayFullIssue_RecostsTheIssue (WA and FIFO)
- CancelReceipt_WithPostedLandedCost_IsRejected_UntilLandedCostCancelled
- CancelReceipt_WithOtherNetCostCorrection_IsRejectedAsProcessed
- Return_ExcludesQuantityRejectedToDamaged
- Return_TakesEachPortionFromWhereItIsNow
- CancelPo_ReleasesExpectedReservationsAndEmitsEvent

RED (before the fix, all 10 failed): same-day landed cost / invoice issue returned 409 `cost_correction_no_stock` (WA); FIFO silently kept 20 instead of 30 on the TransferOut and on the issue; receipt cancel returned 200 with a posted landed cost or a net correction; a return after rejecting to Damaged was not capped (409 instead of 400); a split release returned everything from the last target (physical 4 instead of 0 in warehouse 1); PO cancel left the reservation Active and wrote no event.
GREEN: all 10 pass after the fix.

Full ERP.slnx: Domain 146, Application 20, Functional 709, all pass (a TearDown Npgsql timeout message on TenantFilterTests is infra noise; the run still reports 0 failed).
