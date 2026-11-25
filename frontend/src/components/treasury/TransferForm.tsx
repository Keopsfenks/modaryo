"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import CurrencyRateField, { type RateState } from "@/components/invoicing/CurrencyRateField";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { Currency } from "@/lib/api/erp";
import { ACCOUNT_KIND_LABELS, treasury, type PostResult, type TreasuryAccountDto } from "@/lib/api/treasury";
import { todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const today = todayIso;

/** Kasa/banka hesapları arası virman. Tutar çıkış hesabının para biriminde; para birimleri farklıysa kur gerekir. */
export default function TransferForm() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("virman.manage");

    const [accounts, setAccounts] = useState<TreasuryAccountDto[]>([]);
    const [fromId, setFromId] = useState("");
    const [toId, setToId] = useState("");
    const [date, setDate] = useState(today());
    const [amountText, setAmountText] = useState("");
    const [rate, setRate] = useState<RateState>({ value: "", overridden: false });
    const [description, setDescription] = useState("");
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);
    const [result, setResult] = useState<PostResult | null>(null);

    useEffect(() => {
        if (!allowed) return;
        treasury.accounts().then((list) => setAccounts(list.filter((a) => a.isActive)), () => setAccounts([]));
    }, [allowed]);

    if (!allowed) return <Alert severity="warning">Virman yapmak için yetkiniz yok.</Alert>;

    const from = accounts.find((a) => a.id === fromId);
    const to = accounts.find((a) => a.id === toId);
    const differs = !!from && !!to && from.currency !== to.currency;
    const pair = differs && from.currency !== "TRY" && to.currency !== "TRY";
    const rateCurrency: Currency | null = differs && !pair ? (from.currency !== "TRY" ? from.currency : to.currency) : null;
    const parsedRate = rateCurrency ? parseAmount(rate.value, { maxDecimals: 4 }) : null;
    const amount = parseAmount(amountText);
    const sameAccount = !!fromId && fromId === toId;

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        if (busy) return;
        setError(null);
        if (!from || !to) return setError("Çıkış ve giriş hesabı seçilmelidir.");
        if (sameAccount) return setError("Çıkış ve giriş hesabı aynı olamaz.");
        if (pair) return setError(errorMessage(new ApiError(400, "", "transfer_currency_pair")));
        if (amount === null || amount <= 0) return setError("Tutar 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");
        if (differs && (parsedRate === null || parsedRate <= 0)) return setError(errorMessage(new ApiError(400, "", "rate_required")));

        setBusy(true);
        try {
            setResult(
                await treasury.createTransfer({
                    fromAccountId: from.id,
                    toAccountId: to.id,
                    date,
                    amount,
                    rate: differs ? parsedRate : null,
                    description: description.trim() || null,
                }),
            );
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const option = (a: TreasuryAccountDto) => (
        <MenuItem key={a.id} value={a.id}>
            {a.name} · {ACCOUNT_KIND_LABELS[a.kind]} ({a.currency})
        </MenuItem>
    );

    if (result) {
        return (
            <>
                <PageHeader title="Yeni virman" />
                <DashboardCard>
                    <Stack spacing={2}>
                        <Alert severity="success">Virman kaydedildi: {result.number}</Alert>
                        {result.warnings.includes("cash_negative") && <Alert severity="warning">Kasa bakiyesi eksiye düştü. Kasa hareketlerini kontrol edin.</Alert>}
                        <Stack direction="row" spacing={1}>
                            <Button
                                variant="contained"
                                onClick={() => {
                                    setResult(null);
                                    setAmountText("");
                                    setDescription("");
                                }}
                            >
                                Yeni virman
                            </Button>
                            <Button component={Link} href="/finans/kasa-banka" variant="outlined">
                                Hesaplara dön
                            </Button>
                        </Stack>
                    </Stack>
                </DashboardCard>
            </>
        );
    }

    return (
        <>
            <PageHeader title="Yeni virman" subtitle="Çıkış hesabından düşülür, giriş hesabına eklenir." />
            <DashboardCard>
                <form noValidate onSubmit={submit}>
                    <Stack spacing={2}>
                        <FormAlert error={error} />
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField select size="small" label="Çıkış hesabı" value={fromId} onChange={(e) => setFromId(e.target.value)} sx={{ minWidth: 260 }}>
                                {accounts.map(option)}
                            </TextField>
                            <TextField
                                select
                                size="small"
                                label="Giriş hesabı"
                                value={toId}
                                onChange={(e) => setToId(e.target.value)}
                                error={sameAccount}
                                helperText={sameAccount ? "Çıkış ve giriş hesabı aynı olamaz." : undefined}
                                sx={{ minWidth: 260 }}
                            >
                                {accounts.map(option)}
                            </TextField>
                        </Stack>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField
                                size="small"
                                label={from ? `Tutar (${from.currency})` : "Tutar"}
                                value={amountText}
                                onChange={(e) => setAmountText(e.target.value)}
                                error={amountText.trim() !== "" && amount === null}
                                slotProps={{ htmlInput: { inputMode: "decimal" } }}
                            />
                            <TextField size="small" label="Açıklama" value={description} onChange={(e) => setDescription(e.target.value)} sx={{ flexGrow: 1 }} />
                        </Stack>
                        {pair && <Alert severity="warning">{errorMessage(new ApiError(400, "", "transfer_currency_pair"))}</Alert>}
                        {rateCurrency && <CurrencyRateField currency={rateCurrency} date={date} value={rate} onChange={setRate} />}
                        <div>
                            <Button type="submit" variant="contained" disabled={busy}>
                                {busy ? "Kaydediliyor..." : "Virman yap"}
                            </Button>
                        </div>
                    </Stack>
                </form>
            </DashboardCard>
        </>
    );
}
