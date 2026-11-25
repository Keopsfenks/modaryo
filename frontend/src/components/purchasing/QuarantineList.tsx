"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import { purchasing, type QuarantineRow } from "@/lib/api/purchasing";
import { formatDate } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const qty = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });

type Action = { kind: "release" | "reject"; row: QuarantineRow };

/** Karantinadaki mal kabul satırları: depoya serbest bırakma ya da reddetme (hasarlı deposuna aktarma / tedarikçiye iade). Fiyat göstermez. */
export default function QuarantineList() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satinalma.read");
    const canAct = hasPermission("satinalma.quarantine");
    const [rows, setRows] = useState<QuarantineRow[] | null>(null);
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [action, setAction] = useState<Action | null>(null);
    const [notice, setNotice] = useState<string | null>(null);

    const load = useCallback(async () => {
        try {
            setRows(await purchasing.quarantine.list());
            setError(null);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
            setRows((cur) => cur ?? []);
        }
    }, []);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect
        void load();
        inventory.warehouses().then(setWarehouses, () => undefined);
    }, [allowed, load]);

    if (!allowed) return <Alert severity="warning">Karantina kayıtlarını görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Karantina" subtitle="Kalite kontrol bekleyen mal kabul miktarları satılabilir stoğa girmez" />
            <DashboardCard>
                <FormAlert error={error} />
                {notice && (
                    <Alert severity="success" sx={{ mb: 2 }}>
                        {notice}
                    </Alert>
                )}
                {rows === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : rows.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Karantinada bekleyen kalem yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 760 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Mal kabul</TableCell>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Tedarikçi</TableCell>
                                    <TableCell>Kalem</TableCell>
                                    <TableCell align="right">Açık miktar</TableCell>
                                    {canAct && <TableCell />}
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((r) => (
                                    <TableRow key={r.receiptLineId}>
                                        <TableCell>{r.receiptNumber}</TableCell>
                                        <TableCell>{formatDate(r.date)}</TableCell>
                                        <TableCell>{r.supplierName}</TableCell>
                                        <TableCell>
                                            {r.productCode} · {r.productName}
                                        </TableCell>
                                        <TableCell align="right">
                                            {qty(r.openQuantity)} {r.unit}
                                        </TableCell>
                                        {canAct && (
                                            <TableCell align="right">
                                                <Button size="small" onClick={() => setAction({ kind: "release", row: r })}>
                                                    Serbest bırak
                                                </Button>
                                                <Button size="small" color="error" onClick={() => setAction({ kind: "reject", row: r })}>
                                                    Reddet
                                                </Button>
                                            </TableCell>
                                        )}
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>
            {action && (
                <QuarantineDialog
                    key={`${action.kind}-${action.row.receiptLineId}`}
                    action={action}
                    warehouses={warehouses}
                    onClose={() => setAction(null)}
                    onDone={(message) => {
                        setAction(null);
                        setNotice(message);
                        void load();
                    }}
                />
            )}
        </>
    );
}

function QuarantineDialog({ action, warehouses, onClose, onDone }: { action: Action; warehouses: WarehouseDto[]; onClose(): void; onDone(message: string): void }) {
    const { row, kind } = action;
    const branchId = warehouses.find((w) => w.id === row.warehouseId)?.branchId;
    const targets = warehouses.filter((w) => w.isActive && w.kind === "normal" && (!branchId || w.branchId === branchId));
    const [quantity, setQuantity] = useState(String(row.openQuantity).replace(".", ","));
    const [target, setTarget] = useState(row.targetWarehouseId ?? "");
    const [reject, setReject] = useState<"damaged" | "return">("damaged");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const targetId = target || targets[0]?.id || "";

    async function submit() {
        const q = parseAmount(quantity, { maxDecimals: 4 });
        if (q === null || q <= 0) return setError("Miktar sıfırdan büyük olmalıdır.");
        if (q > row.openQuantity) return setError("Miktar karantinadaki açık miktarı aşıyor.");
        if (kind === "release" && !targetId) return setError("Hedef depo seçilmelidir.");
        setBusy(true);
        setError(null);
        try {
            if (kind === "release") {
                await purchasing.quarantine.release({ receiptLineId: row.receiptLineId, quantity: q, targetWarehouseId: targetId });
                onDone(`${row.productCode}: ${qty(q)} ${row.unit} depoya alındı.`);
            } else {
                const res = await purchasing.quarantine.reject({ receiptLineId: row.receiptLineId, quantity: q, action: reject });
                onDone(
                    reject === "return"
                        ? `${row.productCode}: ${qty(q)} ${row.unit} tedarikçiye iade edildi${res.invoiceNumber ? ` (iade faturası taslağı ${res.invoiceNumber})` : res.invoiceId ? " (iade faturası taslağı oluşturuldu)" : ""}.`
                        : `${row.productCode}: ${qty(q)} ${row.unit} hasarlı deposuna aktarıldı.`,
                );
            }
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{kind === "release" ? "Karantinadan serbest bırak" : "Karantinadaki miktarı reddet"}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <Typography variant="body2">
                        {row.receiptNumber} · {row.productCode} · {row.productName}
                    </Typography>
                    <FormAlert error={error} />
                    <TextField label="Miktar" value={quantity} onChange={(e) => setQuantity(e.target.value)} helperText={`Açık miktar: ${qty(row.openQuantity)} ${row.unit}`} slotProps={{ htmlInput: { inputMode: "decimal" } }} fullWidth />
                    {kind === "release" ? (
                        <TextField select label="Hedef depo" value={targetId} onChange={(e) => setTarget(e.target.value)} slotProps={{ select: { native: true } }} fullWidth>
                            {targets.map((w) => (
                                <option key={w.id} value={w.id}>
                                    {w.name}
                                </option>
                            ))}
                        </TextField>
                    ) : (
                        <RadioGroup value={reject} onChange={(e) => setReject(e.target.value as "damaged" | "return")}>
                            <FormControlLabel value="damaged" control={<Radio />} label="Hasarlı deposuna aktar" />
                            <FormControlLabel value="return" control={<Radio />} label="Tedarikçiye iade et" />
                        </RadioGroup>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" color={kind === "release" ? "primary" : "error"} disabled={busy} onClick={() => void submit()}>
                    {busy ? "İşleniyor..." : kind === "release" ? "Serbest bırak" : "Reddet"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
