"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { CurrentAccountListItem } from "@/lib/api/erp";
import { treasury } from "@/lib/api/treasury";
import { todayIso } from "@/lib/date";
import { formatMoney, parseAmount } from "@/lib/money";
import CariPicker from "./CariPicker";

type Line = { code: string; debit: string; credit: string; description: string; cari: CurrentAccountListItem | null; showCari: boolean };

const emptyLine = (): Line => ({ code: "", debit: "", credit: "", description: "", cari: null, showCari: false });
const cents = (n: number) => Math.round(n * 100);

/** Boş alan 0 sayılır; geçersiz metin null döner. */
const amountOf = (text: string): number | null => (text.trim() === "" ? 0 : parseAmount(text));

/** Mahsup fişi: borç/alacak ızgarası; toplam borç = toplam alacak olmadan kaydedilemez. */
export default function JournalVoucherForm({ onSaved }: { onSaved?: () => void }) {
    const [date, setDate] = useState(todayIso());
    const [description, setDescription] = useState("");
    const [lines, setLines] = useState<Line[]>([emptyLine(), emptyLine()]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);
    const [saved, setSaved] = useState<string | null>(null);

    const parsed = lines.map((l) => ({ debit: amountOf(l.debit), credit: amountOf(l.credit) }));
    const totalDebit = parsed.reduce((s, l) => s + (l.debit ?? 0), 0);
    const totalCredit = parsed.reduce((s, l) => s + (l.credit ?? 0), 0);
    const diff = (cents(totalDebit) - cents(totalCredit)) / 100;
    const balanced = diff === 0 && totalDebit > 0;
    const linesValid = lines.every((l, i) => {
        const p = parsed[i];
        return l.code.trim() !== "" && p.debit !== null && p.credit !== null && (p.debit > 0) !== (p.credit > 0);
    });
    const canSave = balanced && linesValid && description.trim() !== "" && lines.length >= 2 && !busy;

    const update = (index: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, i) => (i === index ? { ...l, ...patch } : l)));

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        if (!canSave) return;
        setError(null);
        setBusy(true);
        try {
            const result = await treasury.createJournalVoucher({
                date,
                description: description.trim(),
                lines: lines.map((l, i) => ({
                    accountCode: l.code.trim(),
                    debit: parsed[i].debit ?? 0,
                    credit: parsed[i].credit ?? 0,
                    description: l.description.trim() || null,
                    currentAccountId: l.cari?.id ?? null,
                })),
            });
            setSaved(result.number);
            setDescription("");
            setLines([emptyLine(), emptyLine()]);
            onSaved?.();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <DashboardCard title="Yeni mahsup fişi">
            <form noValidate onSubmit={submit}>
                <Stack spacing={2}>
                    {saved && <Alert severity="success">Mahsup fişi kaydedildi: {saved}</Alert>}
                    <FormAlert error={error} />
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                        <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField size="small" label="Açıklama" value={description} onChange={(e) => setDescription(e.target.value)} sx={{ flexGrow: 1 }} />
                    </Stack>
                    {lines.map((l, i) => {
                        const n = i + 1;
                        return (
                            <Stack key={i} spacing={1}>
                                <Stack direction={{ xs: "column", md: "row" }} spacing={1} sx={{ alignItems: { md: "center" } }}>
                                    <TextField size="small" label={`Hesap kodu ${n}`} value={l.code} onChange={(e) => update(i, { code: e.target.value })} sx={{ width: 150 }} />
                                    <TextField size="small" label={`Borç ${n}`} value={l.debit} onChange={(e) => update(i, { debit: e.target.value })} error={amountOf(l.debit) === null} slotProps={{ htmlInput: { inputMode: "decimal" } }} sx={{ width: 140 }} />
                                    <TextField size="small" label={`Alacak ${n}`} value={l.credit} onChange={(e) => update(i, { credit: e.target.value })} error={amountOf(l.credit) === null} slotProps={{ htmlInput: { inputMode: "decimal" } }} sx={{ width: 140 }} />
                                    <TextField size="small" label={`Satır açıklaması ${n}`} value={l.description} onChange={(e) => update(i, { description: e.target.value })} sx={{ flexGrow: 1 }} />
                                    <Button size="small" color="inherit" onClick={() => update(i, { showCari: !l.showCari, cari: l.showCari ? null : l.cari })}>
                                        {l.showCari ? "Cariyi kaldır" : "Cari bağla"}
                                    </Button>
                                    <IconButton aria-label={`Satır ${n} sil`} disabled={lines.length <= 2} onClick={() => setLines((ls) => ls.filter((_, k) => k !== i))}>
                                        <IconTrash size={18} />
                                    </IconButton>
                                </Stack>
                                {l.showCari && <CariPicker label={`Cari ${n} (yalnızca TL cariler)`} value={l.cari} onChange={(c) => update(i, { cari: c })} />}
                            </Stack>
                        );
                    })}
                    <div>
                        <Button size="small" onClick={() => setLines((ls) => [...ls, emptyLine()])}>
                            Satır ekle
                        </Button>
                    </div>
                    <Typography data-testid="voucher-diff" color={diff === 0 ? "success.main" : "error.main"}>
                        Toplam borç: {formatMoney(totalDebit, "TRY")} · Toplam alacak: {formatMoney(totalCredit, "TRY")} · {diff === 0 ? "Dengeli" : `Fark: ${formatMoney(Math.abs(diff), "TRY")}`}
                    </Typography>
                    <div>
                        <Button type="submit" variant="contained" disabled={!canSave}>
                            {busy ? "Kaydediliyor..." : "Kaydet"}
                        </Button>
                    </div>
                </Stack>
            </form>
        </DashboardCard>
    );
}
