"use client";

import { useState } from "react";
import Link from "next/link";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { purchasing, type ReceiptDto, type ReturnResult } from "@/lib/api/purchasing";
import { todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

type ReturnDialogProps = { receipt: ReceiptDto; onClose(): void; onDone(): void };

/** Mal kabulden tedarikçiye iade: satır başına iade miktarı (boş = iade yok), gerekçe ve isteğe bağlı iade faturası taslağı. */
export default function ReturnDialog({ receipt, onClose, onDone }: ReturnDialogProps) {
    const { hasPermission } = useSession();
    const canInvoice = hasPermission("fatura.manage") && hasPermission("fiyat.alis");
    const [quantities, setQuantities] = useState<Record<string, string>>({});
    const [reason, setReason] = useState("");
    const [date, setDate] = useState(todayIso());
    const [createInvoice, setCreateInvoice] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [result, setResult] = useState<ReturnResult | null>(null);
    const [busy, setBusy] = useState(false);

    async function submit() {
        const lines: { receiptLineId: string; quantity: number }[] = [];
        for (const l of receipt.lines) {
            const text = (quantities[l.id] ?? "").trim();
            if (!text) continue;
            const q = parseAmount(text, { maxDecimals: 4 });
            if (q === null || q <= 0) return setError(`${l.productCode}: iade miktarı geçersiz.`);
            if (q > l.quantity) return setError(`${l.productCode}: iade miktarı kabul edilen miktarı aşıyor.`);
            lines.push({ receiptLineId: l.id, quantity: q });
        }
        if (lines.length === 0) return setError("En az bir satır için iade miktarı girin.");
        if (reason.trim().length < 2) return setError("İade gerekçesi zorunludur.");
        setBusy(true);
        setError(null);
        try {
            setResult(await purchasing.createReturn({ receiptId: receipt.id, lines, date, reason: reason.trim(), createInvoice: createInvoice && canInvoice }));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={result ? onDone : onClose} fullWidth maxWidth="md">
            <DialogTitle>Tedarikçiye iade · {receipt.number}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    {result ? (
                        <Stack spacing={1}>
                            <FormAlert severity="success">İade stoktan düşüldü.</FormAlert>
                            {result.invoiceId && (
                                <Typography component={Link} href={`/satin-alma/faturalar/${result.invoiceId}`} sx={{ color: "primary.main" }}>
                                    İade faturası taslağını aç{result.invoiceNumber ? ` (${result.invoiceNumber})` : ""}
                                </Typography>
                            )}
                        </Stack>
                    ) : (
                        <>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Kabul edilen</TableCell>
                                        <TableCell sx={{ width: 150 }}>İade miktarı</TableCell>
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
                                            <TableCell>
                                                <TextField
                                                    size="small"
                                                    value={quantities[l.id] ?? ""}
                                                    onChange={(e) => setQuantities((cur) => ({ ...cur, [l.id]: e.target.value }))}
                                                    slotProps={{ htmlInput: { "aria-label": `${l.productCode} iade miktarı`, inputMode: "decimal" } }}
                                                />
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                            <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                                <TextField size="small" type="date" label="İade tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                                <TextField size="small" label="Gerekçe" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ flexGrow: 1 }} slotProps={{ htmlInput: { maxLength: 200 } }} />
                            </Stack>
                            {canInvoice && <FormControlLabel control={<Checkbox checked={createInvoice} onChange={(e) => setCreateInvoice(e.target.checked)} />} label="İade faturası taslağı oluştur" />}
                        </>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                {result ? (
                    <Button variant="contained" onClick={onDone}>
                        Tamam
                    </Button>
                ) : (
                    <>
                        <Button onClick={onClose}>Vazgeç</Button>
                        <Button variant="contained" color="error" disabled={busy} onClick={() => void submit()}>
                            {busy ? "İşleniyor..." : "İade et"}
                        </Button>
                    </>
                )}
            </DialogActions>
        </Dialog>
    );
}
