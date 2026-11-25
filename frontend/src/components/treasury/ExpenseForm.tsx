"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { CurrentAccountListItem, UploadedFile } from "@/lib/api/erp";
import { ACCOUNT_KIND_LABELS, treasury, type ExpenseCategoryDto, type ExpenseKind, type ExpenseResult, type TreasuryAccountDto } from "@/lib/api/treasury";
import { todayIso } from "@/lib/date";
import { formatMoney, parseAmount } from "@/lib/money";
import AttachmentUpload from "./AttachmentUpload";
import CariPicker from "./CariPicker";

const today = todayIso;
const round2 = (n: number) => Math.round(n * 100) / 100;

/** KDV dahil tutardan net ve KDV tutarı (sunucuyla aynı: KDV = brüt x oran / (100 + oran), net = brüt - KDV). */
export function splitVat(gross: number, rate: number): { net: number; vat: number } {
    const vat = round2((gross * rate) / (100 + rate));
    return { net: round2(gross - vat), vat };
}

/** Masraf / gelir fişi: kategori, KDV dahil tutar ve ayrım önizlemesi; hesap ya da cari üzerinden. */
export default function ExpenseForm() {
    const [kind, setKind] = useState<ExpenseKind>("expense");
    const [target, setTarget] = useState<"account" | "cari">("account");
    const [categories, setCategories] = useState<ExpenseCategoryDto[]>([]);
    const [accounts, setAccounts] = useState<TreasuryAccountDto[]>([]);
    const [categoryId, setCategoryId] = useState("");
    const [accountId, setAccountId] = useState("");
    const [cari, setCari] = useState<CurrentAccountListItem | null>(null);
    const [date, setDate] = useState(today());
    const [amountText, setAmountText] = useState("");
    const [vatText, setVatText] = useState("20");
    const [description, setDescription] = useState("");
    const [attachment, setAttachment] = useState<UploadedFile | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);
    const [result, setResult] = useState<ExpenseResult | null>(null);

    useEffect(() => {
        treasury.expenseCategories().then((list) => setCategories(list.filter((c) => c.isActive)), () => setCategories([]));
        treasury.accounts().then((list) => setAccounts(list.filter((a) => a.isActive)), () => setAccounts([]));
    }, []);

    const options = categories.filter((c) => c.isIncome === (kind === "income"));
    const category = options.find((c) => c.id === categoryId);
    const amount = parseAmount(amountText);
    const vat = vatText.trim() === "" ? null : parseAmount(vatText);
    const preview = amount !== null && amount > 0 && vat !== null && vat <= 100 ? splitVat(amount, vat) : null;
    const selectedAccount = accounts.find((a) => a.id === accountId);
    const currency = target === "cari" ? cari?.currency : selectedAccount?.currency;

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        if (busy) return;
        setError(null);
        if (!category) return setError("Kategori seçilmelidir.");
        if (target === "account" && !selectedAccount) return setError("Kasa/banka hesabı seçilmelidir.");
        if (target === "cari" && !cari) return setError("Cari seçilmelidir.");
        if (amount === null || amount <= 0) return setError("Tutar 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");
        if (vatText.trim() !== "" && (vat === null || vat > 100)) return setError("KDV oranı 0 ile 100 arasında olmalıdır.");

        setBusy(true);
        try {
            setResult(
                await treasury.createExpense({
                    kind,
                    categoryId: category.id,
                    date,
                    amount,
                    vatRate: vat,
                    accountId: target === "account" ? selectedAccount?.id : null,
                    currentAccountId: target === "cari" ? cari?.id : null,
                    description: description.trim() || null,
                    attachmentFileId: attachment?.id ?? null,
                }),
            );
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    if (result) {
        return (
            <DashboardCard>
                <Stack spacing={2}>
                    <Alert severity="success">
                        {kind === "income" ? "Gelir" : "Masraf"} fişi kaydedildi: {result.number} (net {formatMoney(result.netAmount, currency ?? "TRY")}, KDV {formatMoney(result.vatAmount, currency ?? "TRY")})
                    </Alert>
                    {result.warnings.includes("cash_negative") && <Alert severity="warning">Kasa bakiyesi eksiye düştü. Kasa hareketlerini kontrol edin.</Alert>}
                    <div>
                        <Button
                            variant="contained"
                            onClick={() => {
                                setResult(null);
                                setAmountText("");
                                setDescription("");
                                setAttachment(null);
                            }}
                        >
                            Yeni fiş
                        </Button>
                    </div>
                </Stack>
            </DashboardCard>
        );
    }

    return (
        <DashboardCard>
            <form noValidate onSubmit={submit}>
                <Stack spacing={2}>
                    <FormAlert error={error} />
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                        <ToggleButtonGroup
                            exclusive
                            size="small"
                            value={kind}
                            aria-label="Fiş türü"
                            onChange={(_, v: ExpenseKind | null) => {
                                if (v) {
                                    setKind(v);
                                    setCategoryId("");
                                }
                            }}
                        >
                            <ToggleButton value="expense">Masraf</ToggleButton>
                            <ToggleButton value="income">Gelir</ToggleButton>
                        </ToggleButtonGroup>
                        <TextField select size="small" label="Kategori" value={category?.id ?? ""} onChange={(e) => setCategoryId(e.target.value)} sx={{ minWidth: 240 }}>
                            {options.map((c) => (
                                <MenuItem key={c.id} value={c.id}>
                                    {c.name}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                        <ToggleButtonGroup exclusive size="small" value={target} aria-label="Hedef" onChange={(_, v: "account" | "cari" | null) => v && setTarget(v)}>
                            <ToggleButton value="account">Kasa / banka</ToggleButton>
                            <ToggleButton value="cari">Cari</ToggleButton>
                        </ToggleButtonGroup>
                        {target === "account" ? (
                            <TextField select size="small" label="Hesap" value={selectedAccount?.id ?? ""} onChange={(e) => setAccountId(e.target.value)} sx={{ minWidth: 260 }}>
                                {accounts.map((a) => (
                                    <MenuItem key={a.id} value={a.id}>
                                        {a.name} · {ACCOUNT_KIND_LABELS[a.kind]} ({a.currency})
                                    </MenuItem>
                                ))}
                            </TextField>
                        ) : (
                            <CariPicker value={cari} onChange={setCari} />
                        )}
                    </Stack>
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                        <TextField
                            size="small"
                            label={currency ? `Tutar (KDV dahil, ${currency})` : "Tutar (KDV dahil)"}
                            value={amountText}
                            onChange={(e) => setAmountText(e.target.value)}
                            error={amountText.trim() !== "" && amount === null}
                            slotProps={{ htmlInput: { inputMode: "decimal" } }}
                        />
                        <TextField
                            size="small"
                            label="KDV oranı (%)"
                            value={vatText}
                            onChange={(e) => setVatText(e.target.value)}
                            error={vatText.trim() !== "" && (vat === null || vat > 100)}
                            slotProps={{ htmlInput: { inputMode: "decimal" } }}
                            sx={{ width: 140 }}
                        />
                        <TextField size="small" label="Açıklama" value={description} onChange={(e) => setDescription(e.target.value)} sx={{ flexGrow: 1 }} />
                    </Stack>
                    {preview && (
                        <Typography variant="body2" color="text.secondary" data-testid="vat-preview">
                            Net: {formatMoney(preview.net, currency ?? "TRY")} · KDV: {formatMoney(preview.vat, currency ?? "TRY")} · Toplam: {formatMoney(amount ?? 0, currency ?? "TRY")}
                        </Typography>
                    )}
                    <AttachmentUpload file={attachment} onChange={setAttachment} label="Fiş/fatura eki" />
                    <div>
                        <Button type="submit" variant="contained" disabled={busy}>
                            {busy ? "Kaydediliyor..." : "Kaydet"}
                        </Button>
                    </div>
                </Stack>
            </form>
        </DashboardCard>
    );
}
