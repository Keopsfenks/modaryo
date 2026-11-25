"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
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
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { PO_STATUS_LABELS, purchasing, RECEIPT_STATUS_LABELS, type PoDto, type PoStatus } from "@/lib/api/purchasing";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import MatchTable from "./MatchTable";
import ReasonDialog from "./ReasonDialog";

export const PO_BASE = "/satin-alma/siparisler";

export const STATUS_COLOR: Record<PoStatus, "default" | "info" | "warning" | "success" | "error"> = {
    draft: "default",
    approved: "info",
    partiallyReceived: "warning",
    received: "success",
    closed: "default",
    cancelled: "error",
};

const money = (value: number | null, currency: string) => (value === null ? "—" : formatMoney(value, currency as Currency));
const qty = (value: number) => value.toLocaleString("tr-TR", { maximumFractionDigits: 4 });

type ActionsProps = { po: PoDto; onChanged(): void; onError(error: ApiError | string | null): void };

/** Başlık işlemleri: Onayla, Kopyala, PDF, E-posta, Kapat, İptal, Mal kabul yap, Fatura oluştur. */
export function OrderActions({ po, onChanged, onError }: ActionsProps) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("satinalma.manage");
    const canPrice = hasPermission("fiyat.alis");
    const [busy, setBusy] = useState(false);
    const [dialog, setDialog] = useState<"cancel" | "close" | "email" | "invoice" | "pdf" | null>(null);

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

    const open = po.status === "approved" || po.status === "partiallyReceived";
    const hasReceipts = po.receipts.some((r) => r.status === "posted");

    return (
        <>
            <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                {canManage && po.status === "draft" && (
                    <Button
                        variant="contained"
                        disabled={busy}
                        onClick={() =>
                            void run(async () => {
                                await purchasing.orders.approve(po.id);
                                onChanged();
                            })
                        }
                    >
                        Onayla
                    </Button>
                )}
                {hasPermission("satinalma.receive") && open && (
                    <Button variant="contained" component={Link} href={`/satin-alma/mal-kabul/yeni?po=${po.id}`}>
                        Mal kabul yap
                    </Button>
                )}
                {canManage && canPrice && hasPermission("fatura.manage") && hasReceipts && (
                    <Button variant="outlined" onClick={() => setDialog("invoice")}>
                        Fatura oluştur
                    </Button>
                )}
                {canManage && canPrice && (
                    <Button
                        variant="outlined"
                        disabled={busy}
                        onClick={() =>
                            void run(async () => {
                                const copy = await purchasing.orders.copy(po.id);
                                router.push(`${PO_BASE}/${copy.id}`);
                            })
                        }
                    >
                        Kopyala
                    </Button>
                )}
                <Button variant="outlined" onClick={() => setDialog("pdf")}>
                    PDF
                </Button>
                {canManage && po.status !== "draft" && po.status !== "cancelled" && (
                    <Button variant="outlined" onClick={() => setDialog("email")}>
                        E-posta
                    </Button>
                )}
                {canManage && open && (
                    <Button color="warning" onClick={() => setDialog("close")}>
                        Kapat
                    </Button>
                )}
                {canManage && (po.status === "draft" || (po.status === "approved" && !hasReceipts)) && (
                    <Button color="error" onClick={() => setDialog("cancel")}>
                        İptal
                    </Button>
                )}
            </Stack>

            <ReasonDialog
                open={dialog === "cancel"}
                title="Sipariş iptal edilsin mi?"
                text={`${po.number} iptal edilecek. Mal kabulü olmayan siparişler iptal edilebilir.`}
                confirmLabel="İptal et"
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    await purchasing.orders.cancel(po.id, reason);
                    setDialog(null);
                    onChanged();
                }}
            />
            <ReasonDialog
                open={dialog === "close"}
                title="Sipariş kısa kapatılsın mı?"
                text="Kalan açık miktar serbest bırakılır; sonraki mal kabuller reddedilir."
                confirmLabel="Kapat"
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    await purchasing.orders.close(po.id, reason);
                    setDialog(null);
                    onChanged();
                }}
            />
            {dialog === "email" && <PoEmailDialog po={po} onClose={() => setDialog(null)} />}
            {dialog === "invoice" && (
                <InvoiceDraftDialog
                    po={po}
                    onClose={() => setDialog(null)}
                    onCreated={(id) => {
                        setDialog(null);
                        router.push(`/satin-alma/faturalar/${id}`);
                    }}
                />
            )}
            <Dialog open={dialog === "pdf"} onClose={() => setDialog(null)} fullWidth maxWidth="md">
                <DialogTitle>{po.number}</DialogTitle>
                <DialogContent>{dialog === "pdf" && <PdfPreview title="Sipariş PDF" load={() => purchasing.orders.pdf(po.id)} />}</DialogContent>
                <DialogActions>
                    <Button onClick={() => setDialog(null)}>Kapat</Button>
                </DialogActions>
            </Dialog>
        </>
    );
}

const EMAIL = /^[^\s@,;]+@[^\s@,;]+\.[^\s@,;]+$/;
const split = (text: string) =>
    text
        .split(/[,;\s]+/)
        .map((s) => s.trim())
        .filter(Boolean);

function PoEmailDialog({ po, onClose }: { po: PoDto; onClose(): void }) {
    const [to, setTo] = useState("");
    const [subject, setSubject] = useState("");
    const [body, setBody] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [sent, setSent] = useState(false);
    const [busy, setBusy] = useState(false);

    async function submit() {
        const list = split(to);
        if (list.some((a) => !EMAIL.test(a))) {
            setError("Geçerli e-posta adresleri girin.");
            return;
        }
        setBusy(true);
        setError(null);
        try {
            await purchasing.orders.email(po.id, { to: list.length ? list : undefined, subject: subject.trim() || undefined, body: body.trim() || undefined });
            setSent(true);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Siparişi tedarikçiye gönder</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    {sent && <FormAlert severity="success">E-posta gönderildi.</FormAlert>}
                    <TextField label="Alıcı" value={to} onChange={(e) => setTo(e.target.value)} helperText="Boşsa tedarikçi kartındaki e-posta kullanılır. Birden çok adres virgülle ayrılır." fullWidth />
                    <TextField label="Konu" value={subject} onChange={(e) => setSubject(e.target.value)} fullWidth />
                    <TextField label="Mesaj" value={body} onChange={(e) => setBody(e.target.value)} multiline minRows={3} fullWidth />
                    <Typography variant="caption" color="text.secondary">
                        Sipariş PDF olarak eklenir ({po.number}).
                    </Typography>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>{sent ? "Kapat" : "Vazgeç"}</Button>
                {!sent && (
                    <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                        {busy ? "Gönderiliyor..." : "Gönder"}
                    </Button>
                )}
            </DialogActions>
        </Dialog>
    );
}

/** Siparişin kabul edilmiş mal kabullerinden alış faturası taslağı üretir; taslak P4 fatura düzenleyicisinde açılır. */
function InvoiceDraftDialog({ po, onClose, onCreated }: { po: PoDto; onClose(): void; onCreated(invoiceId: string): void }) {
    const receipts = po.receipts.filter((r) => r.status === "posted");
    const [selected, setSelected] = useState<string[]>(receipts.map((r) => r.id));
    const [externalNumber, setExternalNumber] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    async function submit() {
        if (selected.length === 0) return setError("En az bir mal kabul seçin.");
        if (!externalNumber.trim()) return setError("Tedarikçi fatura numarası zorunludur.");
        setBusy(true);
        setError(null);
        try {
            const invoice = await purchasing.receipts.invoiceDraft({ receiptIds: selected, externalNumber: externalNumber.trim() });
            onCreated(invoice.id);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Mal kabulden fatura oluştur</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <Stack>
                        {receipts.map((r) => (
                            <FormControlLabel
                                key={r.id}
                                label={`${r.number} · ${formatDate(r.date)}`}
                                control={<Checkbox checked={selected.includes(r.id)} onChange={(e) => setSelected((cur) => (e.target.checked ? [...cur, r.id] : cur.filter((x) => x !== r.id)))} />}
                            />
                        ))}
                    </Stack>
                    <TextField label="Tedarikçi fatura no" required value={externalNumber} onChange={(e) => setExternalNumber(e.target.value)} slotProps={{ htmlInput: { maxLength: 50 } }} fullWidth />
                    <Typography variant="caption" color="text.secondary">
                        Faturalanmamış miktarlar taslak olarak hazırlanır; fiyatlar siparişten gelir.
                    </Typography>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Hazırlanıyor..." : "Taslak oluştur"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}

/** Taslak dışındaki sipariş: satırlar, mal kabuller, faturalar ve üçlü karşılaştırma sekmeleri. */
export default function OrderDetail({ po, onChanged }: { po: PoDto; onChanged(): void }) {
    const { hasPermission } = useSession();
    const showPrices = hasPermission("fiyat.alis");
    const [tab, setTab] = useState(0);
    const [error, setError] = useState<ApiError | string | null>(null);

    return (
        <>
            <PageHeader title={po.number} subtitle={`${po.supplierName} · ${formatDate(po.date)}`} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                <Stack direction="row" spacing={2} sx={{ alignItems: "center", flexWrap: "wrap", gap: 1 }}>
                    <Chip color={STATUS_COLOR[po.status]} label={PO_STATUS_LABELS[po.status]} />
                    {po.expectedDate && <Typography variant="body2">Beklenen: {formatDate(po.expectedDate)}</Typography>}
                    {showPrices && po.total !== null && <Typography variant="body2">Toplam: {money(po.total, po.currency)}</Typography>}
                    {po.cancelReason && <Typography variant="body2">İptal gerekçesi: {po.cancelReason}</Typography>}
                    {po.closeReason && <Typography variant="body2">Kapatma gerekçesi: {po.closeReason}</Typography>}
                </Stack>
                <OrderActions po={po} onChanged={onChanged} onError={setError} />

                <DashboardCard>
                    <Tabs value={tab} onChange={(_, v: number) => setTab(v)} sx={{ mb: 2 }}>
                        <Tab label="Satırlar" />
                        <Tab label="Mal kabuller" />
                        <Tab label="Faturalar" />
                        <Tab label="Karşılaştırma" />
                    </Tabs>
                    {tab === 0 && (
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 720 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Miktar</TableCell>
                                        <TableCell align="right">Gelen</TableCell>
                                        <TableCell align="right">Açık</TableCell>
                                        <TableCell>Birim</TableCell>
                                        <TableCell>Beklenen</TableCell>
                                        {showPrices && <TableCell align="right">Birim fiyat</TableCell>}
                                        {showPrices && <TableCell align="right">Toplam</TableCell>}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {po.lines.map((l) => (
                                        <TableRow key={l.id}>
                                            <TableCell>
                                                {l.productCode} · {l.productName}
                                                {showPrices && l.lastPurchase && (
                                                    <Typography variant="caption" color="text.secondary" sx={{ display: "block" }}>
                                                        Son alış: {formatMoney(l.lastPurchase.unitPriceTl, "TRY")} ({formatDate(l.lastPurchase.date)})
                                                    </Typography>
                                                )}
                                            </TableCell>
                                            <TableCell align="right">{qty(l.quantity)}</TableCell>
                                            <TableCell align="right">{qty(l.receivedQuantity)}</TableCell>
                                            <TableCell align="right">{qty(l.openQuantity)}</TableCell>
                                            <TableCell>{l.unit}</TableCell>
                                            <TableCell>{formatDate(l.expectedDate)}</TableCell>
                                            {showPrices && <TableCell align="right">{money(l.unitPrice, po.currency)}</TableCell>}
                                            {showPrices && <TableCell align="right">{money(l.total, po.currency)}</TableCell>}
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                    {tab === 1 &&
                        (po.receipts.length === 0 ? (
                            <Typography color="text.secondary">Henüz mal kabul yok.</Typography>
                        ) : (
                            <Stack spacing={1}>
                                {po.receipts.map((r) => (
                                    <Typography key={r.id}>
                                        {r.number} · {formatDate(r.date)} · {RECEIPT_STATUS_LABELS[r.status]}
                                    </Typography>
                                ))}
                            </Stack>
                        ))}
                    {tab === 2 &&
                        (po.invoices.length === 0 ? (
                            <Typography color="text.secondary">Henüz fatura yok.</Typography>
                        ) : (
                            <Stack spacing={1}>
                                {po.invoices.map((i) => (
                                    <Typography key={i.id} component={Link} href={`/satin-alma/faturalar/${i.id}`} sx={{ color: "primary.main" }}>
                                        {i.number ?? "(numarasız taslak)"} · {formatDate(i.date)} · {i.status}
                                    </Typography>
                                ))}
                            </Stack>
                        ))}
                    {tab === 3 && <MatchTable rows={po.matchRows} lines={po.lines} currency={po.currency} showPrices={showPrices} />}
                </DashboardCard>
            </Stack>
        </>
    );
}
