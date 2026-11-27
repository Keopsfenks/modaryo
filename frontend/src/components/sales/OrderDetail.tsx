"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import Tab from "@mui/material/Tab";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Tabs from "@mui/material/Tabs";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import PdfPreview from "@/components/invoicing/PdfPreview";
import TotalsPanel from "@/components/invoicing/TotalsPanel";
import ReasonDialog from "@/components/purchasing/ReasonDialog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { INVOICE_STATE_LABELS, ORDER_STATUS_LABELS, SHIPMENT_STATUS_LABELS, sales, type OrderDto, type OrderStatus, type ShipmentStatus } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { formatMoney, parseAmount, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import ApprovalBanner from "./ApprovalBanner";
import DownPaymentDialog from "./DownPaymentDialog";
import InstallmentPlanEditor from "./InstallmentPlanEditor";
import LineTimeline from "./LineTimeline";
import { ProfitCell, ProfitHeaderCell, useCanSeeProfit } from "./ProfitColumn";

export const ORDER_BASE = "/satis/siparisler";

export const ORDER_STATUS_COLOR: Record<OrderStatus, "default" | "info" | "warning" | "success" | "error"> = {
    draft: "default",
    awaitingLimitApproval: "warning",
    awaitingPayment: "warning",
    approved: "info",
    partiallyShipped: "warning",
    shipped: "success",
    closed: "default",
    cancelled: "error",
};

const qty = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });
const SHIPPABLE: OrderStatus[] = ["approved", "partiallyShipped"];

type ActionsProps = { order: OrderDto; onChanged(): void; onError(error: ApiError | string | null): void };

/** Başlık işlemleri: Onayla, Peşinat al, Sevk et, Hızlı fatura, Proforma, Kapat, İptal. */
export function OrderActions({ order, onChanged, onError }: ActionsProps) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const [busy, setBusy] = useState(false);
    const [dialog, setDialog] = useState<"cancel" | "close" | "pdf" | "ship" | "advance" | null>(null);

    async function run(action: () => Promise<void>) {
        setBusy(true);
        onError(null);
        try {
            await action();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            onError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const shippable = SHIPPABLE.includes(order.status);
    const canManage = hasPermission("satis.manage");
    const advanceDue = (order.plan?.downPaymentRemaining ?? 0) > 0 || order.status === "awaitingPayment";
    const live = order.status !== "cancelled" && order.status !== "closed";

    return (
        <>
            <ApprovalBanner
                order={order}
                busy={busy}
                onApproveLimit={() =>
                    void run(async () => {
                        await sales.orders.approveLimit(order.id);
                        onChanged();
                    })
                }
                onCollectAdvance={() => setDialog("advance")}
            />
            <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                {hasPermission("satis.approve") && (order.status === "draft" || order.status === "awaitingPayment") && (
                    <Button
                        variant="contained"
                        disabled={busy}
                        onClick={() =>
                            void run(async () => {
                                await sales.orders.approve(order.id);
                                onChanged();
                            })
                        }
                    >
                        Onayla
                    </Button>
                )}
                {hasPermission("tahsilat.manage") && live && advanceDue && order.status !== "draft" && (
                    <Button variant="outlined" onClick={() => setDialog("advance")}>
                        Peşinat al
                    </Button>
                )}
                {hasPermission("satis.ship") && shippable && (
                    <Button variant="contained" onClick={() => setDialog("ship")}>
                        Sevk et
                    </Button>
                )}
                {hasPermission("satis.ship") && hasPermission("fatura.issue") && shippable && (
                    <Button
                        variant="outlined"
                        disabled={busy}
                        onClick={() =>
                            void run(async () => {
                                const result = await sales.orders.quickInvoice(order.id);
                                router.push(`/satis/faturalar/${result.invoiceId}`);
                            })
                        }
                    >
                        Hızlı fatura
                    </Button>
                )}
                <Button variant="outlined" onClick={() => setDialog("pdf")}>
                    Proforma PDF
                </Button>
                {canManage && (order.status === "approved" || order.status === "partiallyShipped" || order.status === "shipped") && (
                    <Button color="warning" onClick={() => setDialog("close")}>
                        Kapat
                    </Button>
                )}
                {canManage && (order.status === "draft" || order.status === "awaitingLimitApproval" || order.status === "awaitingPayment" || order.status === "approved") && (
                    <Button color="error" onClick={() => setDialog("cancel")}>
                        İptal
                    </Button>
                )}
            </Stack>

            <ReasonDialog
                open={dialog === "cancel"}
                title="Siparişi iptal et"
                text="Rezervasyonlar bırakılır ve bu siparişe ait taslak tedarik siparişleri iptal edilir."
                confirmLabel="İptal et"
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    await sales.orders.cancel(order.id, reason);
                    setDialog(null);
                    onChanged();
                }}
            />
            <ReasonDialog
                open={dialog === "close"}
                title="Siparişi kapat"
                text="Kalan açık miktarın rezervasyonu bırakılır."
                confirmLabel="Kapat"
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    await sales.orders.close(order.id, reason);
                    setDialog(null);
                    onChanged();
                }}
            />
            <Dialog open={dialog === "pdf"} onClose={() => setDialog(null)} fullWidth maxWidth="lg">
                <DialogTitle>Proforma</DialogTitle>
                <DialogContent>{dialog === "pdf" && <PdfPreview title="Proforma PDF" load={() => sales.orders.proforma(order.id)} />}</DialogContent>
            </Dialog>
            <DownPaymentDialog open={dialog === "advance"} order={order} onClose={() => setDialog(null)} />
            {dialog === "ship" && (
                <ShipDialog
                    order={order}
                    onClose={() => setDialog(null)}
                    onDone={() => {
                        setDialog(null);
                        onChanged();
                    }}
                />
            )}
        </>
    );
}

function ShipDialog({ order, onClose, onDone }: { order: OrderDto; onClose(): void; onDone(): void }) {
    const open = order.lines.filter((l) => l.openQuantity > 0);
    const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(open.map((l) => [l.id, String(l.openQuantity).replace(".", ",")])));
    const [cargoNote, setCargoNote] = useState("");
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    async function submit() {
        const lines = open
            .map((l) => ({ orderLineId: l.id, quantity: parseAmount(values[l.id] ?? "", { maxDecimals: 4 }) ?? 0, max: l.openQuantity }))
            .filter((l) => l.quantity > 0);
        if (lines.length === 0) return setError("Sevk edilecek miktar girilmelidir.");
        if (lines.some((l) => l.quantity > l.max)) return setError(errorMessage(new ApiError(400, "", "ship_exceeds_open")));
        setBusy(true);
        setError(null);
        try {
            await sales.shipments.create({ orderId: order.id, cargoNote: cargoNote.trim() || null, lines: lines.map(({ orderLineId, quantity }) => ({ orderLineId, quantity })) });
            onDone();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="md">
            <DialogTitle>Sevk et</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Kalem</TableCell>
                                <TableCell align="right">Açık</TableCell>
                                <TableCell sx={{ width: 150 }}>Sevk miktarı</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {open.map((l) => (
                                <TableRow key={l.id}>
                                    <TableCell>{l.productName}</TableCell>
                                    <TableCell align="right">
                                        {qty(l.openQuantity)} {l.unit}
                                    </TableCell>
                                    <TableCell>
                                        <TextField size="small" value={values[l.id] ?? ""} onChange={(e) => setValues((cur) => ({ ...cur, [l.id]: e.target.value }))} slotProps={{ htmlInput: { "aria-label": `${l.productName} sevk miktarı`, inputMode: "decimal" } }} />
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                    <TextField size="small" label="Kargo / teslimat notu" value={cargoNote} onChange={(e) => setCargoNote(e.target.value)} fullWidth />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Sevk ediliyor..." : "Sevk et"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}

type DetailProps = { order: OrderDto; planEditable: boolean; onChanged(): void; defaultInstallmentCount?: number };

/** Taslak olmayan sipariş: durum, satırlar + tedarik zaman çizgisi, taksit planı ve Sevkler/Faturalar/Tahsilatlar sekmeleri. */
export default function OrderDetail({ order, planEditable, onChanged, defaultInstallmentCount }: DetailProps) {
    const [error, setError] = useState<ApiError | string | null>(null);
    const [tab, setTab] = useState(0);
    const showProfit = useCanSeeProfit();
    const money = (n: number) => formatMoney(n, order.currency as Currency);

    return (
        <>
            <PageHeader title={`Sipariş ${order.number}`} subtitle={`${order.customerName} · ${formatDate(order.date)}`} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                    <Chip color={ORDER_STATUS_COLOR[order.status]} label={ORDER_STATUS_LABELS[order.status]} />
                    <Chip variant="outlined" label={INVOICE_STATE_LABELS[order.invoiceState]} />
                    {order.salesRepName && <Chip variant="outlined" label={`Temsilci: ${order.salesRepName}`} />}
                    {order.quoteId && (
                        <Button component={Link} href={`/satis/teklifler/${order.quoteId}`} size="small">
                            Teklifi aç
                        </Button>
                    )}
                    {order.cancelReason && <Chip variant="outlined" color="error" label={`İptal gerekçesi: ${order.cancelReason}`} />}
                    {showProfit && order.marginPercent !== null && <Chip variant="outlined" label={`Tahmini marj: % ${order.marginPercent.toLocaleString("tr-TR", { maximumFractionDigits: 1 })}`} />}
                </Stack>
                <OrderActions order={order} onChanged={onChanged} onError={setError} />

                <DashboardCard title="Satırlar">
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 900 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kalem</TableCell>
                                    <TableCell align="right">Sipariş</TableCell>
                                    <TableCell align="right">Sevk</TableCell>
                                    <TableCell align="right">Fatura</TableCell>
                                    <TableCell align="right">İade</TableCell>
                                    <TableCell align="right">Birim fiyat</TableCell>
                                    <TableCell align="right">Toplam</TableCell>
                                    <ProfitHeaderCell />
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {order.lines.map((l) => (
                                    <LineRows key={l.id} line={l} currency={order.currency} />
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                </DashboardCard>

                <Stack direction={{ xs: "column", md: "row" }} spacing={3}>
                    <Box sx={{ flexGrow: 1 }}>
                        <DashboardCard title="Taksit planı">
                            <InstallmentPlanEditor
                                key={`${order.id}-${order.version}-${order.total}`}
                                total={order.total}
                                currency={order.currency}
                                plan={order.plan}
                                defaultCount={defaultInstallmentCount}
                                disabled={!planEditable}
                                onSave={async (input) => {
                                    await sales.orders.setPlan(order.id, input);
                                    onChanged();
                                }}
                            />
                        </DashboardCard>
                    </Box>
                    <Box sx={{ minWidth: 320, maxWidth: 420 }}>
                        <DashboardCard>
                            <TotalsPanel
                                input={null}
                                currency={order.currency}
                                fixed={{
                                    totals: { gross: order.gross, discount: order.discount, net: order.net, otv: 0, vat: order.vat, withheld: 0, payable: order.total },
                                    totalsTl: { gross: order.gross, discount: order.discount, net: order.net, otv: 0, vat: order.vat, withheld: 0, payable: order.total },
                                    vatSummary: [],
                                }}
                            />
                        </DashboardCard>
                    </Box>
                </Stack>

                <DashboardCard>
                    <Tabs value={tab} onChange={(_, v: number) => setTab(v)} sx={{ mb: 2 }}>
                        <Tab label={`Sevkler (${order.shipments.length})`} />
                        <Tab label={`Faturalar (${order.invoices.length})`} />
                        <Tab label={`Tahsilatlar (${order.advances.length})`} />
                    </Tabs>
                    {tab === 0 &&
                        (order.shipments.length === 0 ? (
                            <Typography color="text.secondary">Henüz sevk yok.</Typography>
                        ) : (
                            <Stack spacing={0.5}>
                                {order.shipments.map((s) => (
                                    <Typography key={s.id} variant="body2">
                                        {s.number} · {formatDate(s.date)} · {SHIPMENT_STATUS_LABELS[s.status as ShipmentStatus] ?? s.status}
                                    </Typography>
                                ))}
                            </Stack>
                        ))}
                    {tab === 1 &&
                        (order.invoices.length === 0 ? (
                            <Typography color="text.secondary">Henüz fatura yok.</Typography>
                        ) : (
                            <Stack spacing={0.5}>
                                {order.invoices.map((i) => (
                                    <Typography key={i.id} variant="body2">
                                        <Link href={`/satis/faturalar/${i.id}`}>{i.number || "Taslak"}</Link> · {formatDate(i.date)}
                                    </Typography>
                                ))}
                            </Stack>
                        ))}
                    {tab === 2 &&
                        (order.advances.length === 0 ? (
                            <Typography color="text.secondary">Henüz tahsilat yok.</Typography>
                        ) : (
                            <Stack spacing={0.5}>
                                {order.advances.map((a) => (
                                    <Typography key={a.documentId} variant="body2">
                                        {a.number} · {formatDate(a.date)} · {formatMoney(a.amount, a.currency as Currency)}
                                    </Typography>
                                ))}
                                <Typography variant="body2" sx={{ fontWeight: 700, pt: 1 }}>
                                    Toplam avans: {money(order.advanceTotal)}
                                </Typography>
                            </Stack>
                        ))}
                </DashboardCard>
            </Stack>
        </>
    );
}

function LineRows({ line, currency }: { line: OrderDto["lines"][number]; currency: string }) {
    const money = (n: number) => formatMoney(n, currency as Currency);
    const measured = line.width !== null || line.length !== null;
    const size = measured ? [line.width, line.length].filter((n) => n !== null).map((n) => (n as number).toFixed(2).replace(".", ",")).join(" × ") : null;
    return (
        <>
            <TableRow>
                <TableCell>
                    <Typography variant="body2">
                        {line.productCode} · {line.productName}
                    </Typography>
                    {size && (
                        <Typography variant="caption" color="text.secondary">
                            Ebat: {size}
                        </Typography>
                    )}
                    {line.supplyMode === "backToBack" && (
                        <Chip size="small" variant="outlined" label={line.purchaseOrderNumber ? `Tedarik: ${line.purchaseOrderNumber}` : "Siparişe göre tedarik"} sx={{ ml: 1 }} />
                    )}
                </TableCell>
                <TableCell align="right">
                    {qty(line.quantity)} {line.unit}
                </TableCell>
                <TableCell align="right">{qty(line.shippedQuantity)}</TableCell>
                <TableCell align="right">{qty(line.invoicedQuantity)}</TableCell>
                <TableCell align="right">{qty(line.returnedQuantity)}</TableCell>
                <TableCell align="right">{money(line.unitPrice)}</TableCell>
                <TableCell align="right">{money(line.total)}</TableCell>
                <ProfitCell cost={line.estimatedUnitCost} margin={line.marginPercent} currency={currency} />
            </TableRow>
            {line.timeline.length > 0 && (
                <TableRow>
                    <TableCell colSpan={8} sx={{ bgcolor: "action.hover" }}>
                        <LineTimeline steps={line.timeline} />
                    </TableCell>
                </TableRow>
            )}
        </>
    );
}
