"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { PlanDto, PlanInput } from "@/lib/api/sales";
import { addDaysIso, todayIso } from "@/lib/date";
import { formatMoney, parseAmount, type Currency } from "@/lib/money";
import { fromCents, planError, planSumCents, splitPlan, type PlanRowDraft } from "./salesModel";

type InstallmentPlanEditorProps = {
    /** Sipariş tutarı (sunucudan; peşinat + taksitler buna eşit olmalıdır). */
    total: number;
    currency: string;
    plan: PlanDto | null;
    defaultCount?: number;
    /** Düzenleme kapalıyken (taslak/onaylı olmayan sipariş vb.) plan salt okunur görünür. */
    disabled?: boolean;
    onSave(input: PlanInput): Promise<void>;
};

let rowCounter = 0;

function rowsFromPlan(plan: PlanDto): PlanRowDraft[] {
    return plan.rows.map((r) => ({ key: `plan-row-${++rowCounter}`, dueDate: r.dueDate, amount: r.amount.toFixed(2).replace(".", ",") }));
}

/** Taksit planı: peşinat + eşit bölünen (kalan kuruş son satırda) ya da elle düzenlenen taksit satırları. */
export default function InstallmentPlanEditor({ total, currency, plan, defaultCount = 3, disabled, onSave }: InstallmentPlanEditorProps) {
    const [down, setDown] = useState(plan ? plan.downPayment.toFixed(2).replace(".", ",") : "0,00");
    const [count, setCount] = useState(String(plan?.installmentCount ?? defaultCount));
    const [first, setFirst] = useState(plan?.firstDueDate ?? addDaysIso(todayIso(), 30));
    const [rows, setRows] = useState<PlanRowDraft[]>(() => (plan ? rowsFromPlan(plan) : []));
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    const money = (n: number) => formatMoney(n, currency as Currency);
    const downValue = parseAmount(down);
    const consumed = !!plan && (plan.downPaymentRemaining < plan.downPayment || plan.rows.some((r) => r.remaining < r.amount));
    const locked = disabled || consumed;
    const validation = rows.length > 0 ? planError(total, downValue, rows) : null;
    const sum = downValue === null ? null : planSumCents(downValue, rows);

    function split() {
        const n = Number(count);
        if (downValue === null || downValue < 0 || !Number.isInteger(n) || n < 1 || n > 36) return setError("Peşinat ve taksit sayısı (1-36) geçerli olmalıdır.");
        if (!first) return setError("İlk vade tarihi girilmelidir.");
        setError(null);
        setRows(splitPlan(total, downValue, n, first));
    }

    async function save() {
        const found = planError(total, downValue, rows);
        if (found || downValue === null) return setError(found ?? "Peşinat geçersiz.");
        setBusy(true);
        setError(null);
        try {
            await onSave({ downPayment: downValue, rows: rows.map((r) => ({ dueDate: r.dueDate, amount: parseAmount(r.amount) ?? 0 })) });
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const patch = (key: string, change: Partial<PlanRowDraft>) => setRows((cur) => cur.map((r) => (r.key === key ? { ...r, ...change } : r)));

    return (
        <Stack spacing={2}>
            {consumed && <Alert severity="info">Bu plan faturalarca kullanılmaya başlandığı için değiştirilemez.</Alert>}
            {error && <Alert severity="error">{error}</Alert>}
            <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                <Typography variant="body2" color="text.secondary">
                    Sipariş tutarı: <strong>{money(total)}</strong>
                </Typography>
                <TextField size="small" label="Peşinat" value={down} disabled={locked} onChange={(e) => setDown(e.target.value)} slotProps={{ htmlInput: { inputMode: "decimal" } }} sx={{ width: 140 }} />
                <TextField size="small" label="Taksit sayısı" value={count} disabled={locked} onChange={(e) => setCount(e.target.value)} slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ width: 120 }} />
                <TextField size="small" type="date" label="İlk vade" value={first} disabled={locked} onChange={(e) => setFirst(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                {!locked && (
                    <Button variant="outlined" onClick={split}>
                        Eşit böl
                    </Button>
                )}
            </Stack>

            {rows.length > 0 && (
                <>
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell sx={{ width: 60 }}>#</TableCell>
                                <TableCell>Vade</TableCell>
                                <TableCell>Tutar</TableCell>
                                {plan && <TableCell align="right">Kalan</TableCell>}
                                <TableCell />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {rows.map((r, i) => (
                                <TableRow key={r.key}>
                                    <TableCell>{i + 1}</TableCell>
                                    <TableCell>
                                        <TextField size="small" type="date" value={r.dueDate} disabled={locked} onChange={(e) => patch(r.key, { dueDate: e.target.value })} slotProps={{ htmlInput: { "aria-label": `Taksit ${i + 1} vade` } }} />
                                    </TableCell>
                                    <TableCell>
                                        <TextField size="small" value={r.amount} disabled={locked} onChange={(e) => patch(r.key, { amount: e.target.value })} slotProps={{ htmlInput: { "aria-label": `Taksit ${i + 1} tutar`, inputMode: "decimal" } }} />
                                    </TableCell>
                                    {plan && <TableCell align="right">{plan.rows[i] ? money(plan.rows[i].remaining) : "—"}</TableCell>}
                                    <TableCell align="right">
                                        {!locked && rows.length > 1 && (
                                            <IconButton aria-label={`Taksit ${i + 1} satırını kaldır`} onClick={() => setRows((cur) => cur.filter((x) => x.key !== r.key))}>
                                                <IconTrash size={18} />
                                            </IconButton>
                                        )}
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                    <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center" }}>
                        <Typography variant="body2" color={validation ? "error" : "text.secondary"}>
                            Peşinat + taksitler: {sum === null ? "—" : `${fromCents(sum)} ${currency}`}
                            {plan && ` · Peşinattan kalan: ${money(plan.downPaymentRemaining)}`}
                        </Typography>
                        {!locked && (
                            <Button variant="contained" disabled={busy || validation !== null} onClick={() => void save()}>
                                {busy ? "Kaydediliyor..." : "Planı kaydet"}
                            </Button>
                        )}
                    </Stack>
                    {validation && !locked && <Typography variant="caption" color="error">{validation}</Typography>}
                </>
            )}
        </Stack>
    );
}
