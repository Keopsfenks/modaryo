"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { purchasing, RECEIPT_STATUS_LABELS, type ReceiptDto, type ReceiptRow, type ReceiptStatus } from "@/lib/api/purchasing";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import ReasonDialog from "./ReasonDialog";
import ReturnDialog from "./ReturnDialog";

const QUARANTINE_LABELS = { none: "", held: "Karantinada", released: "Serbest bırakıldı", rejected: "Reddedildi" } as const;

/** Mal kabul listesi; satıra tıklayınca ayrıntı penceresi (iade ve iptal buradan). Fiyatlar fiyat.alis olmadan gelmez. */
export default function ReceiptList() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satinalma.read");
    const showPrices = hasPermission("fiyat.alis");
    const [status, setStatus] = useState<ReceiptStatus | "">("");
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: ReceiptRow[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [openId, setOpenId] = useState<string | null>(null);
    const [version, setVersion] = useState(0);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        purchasing.receipts.list({ status: status || undefined, search: search || undefined, page: page + 1, pageSize }).then(
            (r) => {
                if (cancelled) return;
                setError(null);
                setResult(r);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setResult((c) => c ?? { items: [], total: 0 });
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, status, search, page, pageSize, version]);

    if (!allowed) return <Alert severity="warning">Mal kabulleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Mal kabul" subtitle="Tedarikçilerden teslim alınan mallar" />
            <DashboardCard
                action={
                    hasPermission("satinalma.receive") ? (
                        <Button component={Link} href="/satin-alma/mal-kabul/yeni" variant="contained">
                            Yeni mal kabul
                        </Button>
                    ) : undefined
                }
            >
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2 }}>
                    <TextField select size="small" label="Durum" value={status} onChange={(e) => { setStatus(e.target.value as ReceiptStatus | ""); setPage(0); }} sx={{ minWidth: 160 }}>
                        <MenuItem value="">Tümü</MenuItem>
                        {(Object.keys(RECEIPT_STATUS_LABELS) as ReceiptStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>
                                {RECEIPT_STATUS_LABELS[s]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField size="small" label="Ara" placeholder="Mal kabul no, sipariş no ya da tedarikçi" value={searchInput} onChange={(e) => { setSearchInput(e.target.value); setPage(0); }} sx={{ minWidth: 300 }} />
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : result.total === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek mal kabul yok.
                    </Typography>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 720 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Tedarikçi</TableCell>
                                        <TableCell>Sipariş</TableCell>
                                        <TableCell>Durum</TableCell>
                                        <TableCell align="right">Satır</TableCell>
                                        {showPrices && <TableCell align="right">Tutar</TableCell>}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((r) => (
                                        <TableRow key={r.id} hover sx={{ cursor: "pointer" }} onClick={() => setOpenId(r.id)}>
                                            <TableCell>{r.number}</TableCell>
                                            <TableCell>{formatDate(r.date)}</TableCell>
                                            <TableCell>{r.supplierName}</TableCell>
                                            <TableCell>{r.purchaseOrderNumber ?? "—"}</TableCell>
                                            <TableCell>
                                                <Chip size="small" color={r.status === "posted" ? "success" : "error"} label={RECEIPT_STATUS_LABELS[r.status]} />
                                            </TableCell>
                                            <TableCell align="right">{r.lineCount}</TableCell>
                                            {showPrices && <TableCell align="right">{r.total === null ? "—" : formatMoney(r.total, r.currency as Currency)}</TableCell>}
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                        <TablePagination
                            component="div"
                            count={result.total}
                            page={page}
                            rowsPerPage={pageSize}
                            rowsPerPageOptions={[25, 50, 100]}
                            onPageChange={(_, p) => setPage(p)}
                            onRowsPerPageChange={(e) => {
                                setPageSize(Number(e.target.value));
                                setPage(0);
                            }}
                            labelRowsPerPage="Sayfa başına"
                        />
                    </>
                )}
            </DashboardCard>
            {openId && (
                <ReceiptDialog
                    id={openId}
                    onClose={() => setOpenId(null)}
                    onChanged={() => {
                        setOpenId(null);
                        setVersion((v) => v + 1);
                    }}
                />
            )}
        </>
    );
}

function ReceiptDialog({ id, onClose, onChanged }: { id: string; onClose(): void; onChanged(): void }) {
    const { hasPermission } = useSession();
    const showPrices = hasPermission("fiyat.alis");
    const [receipt, setReceipt] = useState<ReceiptDto | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [mode, setMode] = useState<"return" | "cancel" | null>(null);

    const load = useCallback(async () => {
        try {
            setReceipt(await purchasing.receipts.get(id));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        }
    }, [id]);
    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        void load();
    }, [load]);

    return (
        <>
            <Dialog open onClose={onClose} fullWidth maxWidth="md">
                <DialogTitle>{receipt ? `Mal kabul ${receipt.number}` : "Mal kabul"}</DialogTitle>
                <DialogContent>
                    <FormAlert error={error} />
                    {receipt && (
                        <Stack spacing={2} sx={{ pt: 1 }}>
                            <Typography variant="body2">
                                {receipt.supplierName} · {formatDate(receipt.date)}
                                {receipt.purchaseOrderNumber ? ` · Sipariş ${receipt.purchaseOrderNumber}` : ""}
                                {receipt.deliveryNoteNo ? ` · İrsaliye ${receipt.deliveryNoteNo}` : ""}
                            </Typography>
                            {receipt.cancelReason && <Typography variant="body2">İptal gerekçesi: {receipt.cancelReason}</Typography>}
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Miktar</TableCell>
                                        {showPrices && <TableCell align="right">Birim fiyat</TableCell>}
                                        <TableCell>Karantina</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {receipt.lines.map((l) => (
                                        <TableRow key={l.id}>
                                            <TableCell>
                                                {l.productCode} · {l.productName}
                                            </TableCell>
                                            <TableCell align="right">
                                                {l.quantity.toLocaleString("tr-TR", { maximumFractionDigits: 4 })} {l.unit}
                                            </TableCell>
                                            {showPrices && <TableCell align="right">{l.unitPrice === null ? "—" : formatMoney(l.unitPrice, receipt.currency as Currency)}</TableCell>}
                                            <TableCell>{QUARANTINE_LABELS[l.quarantine]}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    {receipt?.status === "posted" && hasPermission("satinalma.manage") && <Button onClick={() => setMode("return")}>Tedarikçiye iade</Button>}
                    {receipt?.status === "posted" && hasPermission("satinalma.receive") && (
                        <Button color="error" onClick={() => setMode("cancel")}>
                            Mal kabulü iptal et
                        </Button>
                    )}
                    <Button onClick={onClose}>Kapat</Button>
                </DialogActions>
            </Dialog>
            {receipt && mode === "return" && <ReturnDialog receipt={receipt} onClose={() => setMode(null)} onDone={onChanged} />}
            <ReasonDialog
                open={mode === "cancel"}
                title="Mal kabul iptal edilsin mi?"
                text="Stok girişi geri alınır ve sipariş gelen miktarı düşer. Faturalanmış ya da karantina/iade görmüş mal kabul iptal edilemez."
                confirmLabel="İptal et"
                onClose={() => setMode(null)}
                onConfirm={async (reason) => {
                    await purchasing.receipts.cancel(id, reason);
                    setMode(null);
                    onChanged();
                }}
            />
        </>
    );
}
