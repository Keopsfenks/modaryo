"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import CurrencyRateField, { type RateState } from "@/components/invoicing/CurrencyRateField";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp, type Currency, type CurrentAccountListItem } from "@/lib/api/erp";
import { METHOD_LABELS, treasury, type OpenItemForAllocation, type PaymentMethod, type PostResult, type TreasuryAccountDto, type TreasuryAccountKind } from "@/lib/api/treasury";
import { todayIso } from "@/lib/date";
import { formatMoney, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import AllocationPicker, { type AllocationChange } from "./AllocationPicker";
import CariPicker from "./CariPicker";

const today = todayIso;
const round2 = (n: number) => Math.round(n * 100) / 100;

const KIND_FOR_METHOD: Record<PaymentMethod, TreasuryAccountKind> = { cash: "cash", transfer: "bank", creditCard: "cardPos" };

type PaymentFormProps = { kind: "collection" | "payment" };

/** Cari tutarı: hesap ve cari para birimi farklıysa kurla çevrilir; çevrilemiyorsa null. */
function budgetInCariCurrency(amount: number | null, account: TreasuryAccountDto | undefined, cari: CurrentAccountListItem | null, rate: number | null): number | null {
    if (amount === null || !account || !cari) return null;
    if (account.currency === cari.currency) return amount;
    if (rate === null) return null;
    if (cari.currency === "TRY") return round2(amount * rate);
    if (account.currency === "TRY") return round2(amount / rate);
    return null;
}

/** Tahsilat ve ödeme formu: cari, yönteme göre hesap, kur, avans ve elle vade eşleştirmesi. */
export default function PaymentForm({ kind }: PaymentFormProps) {
    const collection = kind === "collection";
    const searchParams = useSearchParams();
    const initialCari = searchParams.get("cari");
    // Satış siparişi peşinatı: /finans/tahsilat/yeni?cari=..&avans=1&siparis=..&tutar=..
    const salesOrderId = collection ? searchParams.get("siparis") : null;
    const { hasPermission } = useSession();
    const allowed = hasPermission(collection ? "tahsilat.manage" : "odeme.manage");

    const [cari, setCari] = useState<CurrentAccountListItem | null>(null);
    const [accounts, setAccounts] = useState<TreasuryAccountDto[]>([]);
    const [method, setMethod] = useState<PaymentMethod>("cash");
    const [accountId, setAccountId] = useState("");
    const [date, setDate] = useState(today());
    const [amountText, setAmountText] = useState(() => (collection && searchParams.get("tutar") ? (searchParams.get("tutar") ?? "").replace(".", ",") : ""));
    const [rate, setRate] = useState<RateState>({ value: "", overridden: false });
    const [isAdvance, setIsAdvance] = useState(() => collection && searchParams.get("avans") === "1");
    const [description, setDescription] = useState("");
    const [items, setItems] = useState<OpenItemForAllocation[]>([]);
    const [allocation, setAllocation] = useState<AllocationChange>({ allocations: [], total: 0, valid: true });
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);
    const [reload, setReload] = useState(0);
    const [result, setResult] = useState<PostResult | null>(null);

    useEffect(() => {
        if (!allowed) return;
        treasury.accounts().then((list) => setAccounts(list.filter((a) => a.isActive)), () => setAccounts([]));
    }, [allowed]);

    useEffect(() => {
        if (!allowed || !initialCari) return;
        erp.getAccount(initialCari).then(
            (a) => setCari({ id: a.id, code: a.code, name: a.name, isCustomer: a.isCustomer, isSupplier: a.isSupplier, isEmployee: a.isEmployee, currency: a.currency, balance: a.balance, overLimit: false, overdueAmount: a.overdueAmount, isActive: a.isActive }),
            () => undefined,
        );
    }, [allowed, initialCari]);

    const cariId = cari?.id;
    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- cari değişince açık kalemler sıfırlanır
        setItems([]);
        if (!allowed || !cariId) return;
        let cancelled = false;
        treasury.openItems(cariId).then(
            (list) => {
                if (!cancelled) setItems(list.filter((i) => i.direction === (collection ? "receivable" : "payable") && i.open > 0));
            },
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, cariId, collection, reload]);

    const candidates = useMemo(() => accounts.filter((a) => a.kind === KIND_FOR_METHOD[method]), [accounts, method]);
    const account = accounts.find((a) => a.id === accountId);
    const effectiveAccountId = account && account.kind === KIND_FOR_METHOD[method] ? accountId : "";
    const selected = candidates.find((a) => a.id === effectiveAccountId);

    const differs = !!selected && !!cari && selected.currency !== cari.currency;
    const rateCurrency: Currency | null = differs ? (selected.currency !== "TRY" ? selected.currency : cari.currency) : null;
    const parsedRate = rateCurrency ? parseAmount(rate.value, { maxDecimals: 4 }) : null;
    const amount = parseAmount(amountText);
    const budget = budgetInCariCurrency(amount, selected, cari, parsedRate);

    if (!allowed) return <Alert severity="warning">{collection ? "Tahsilat" : "Ödeme"} girmek için yetkiniz yok.</Alert>;

    function reset() {
        setResult(null);
        setAmountText("");
        setDescription("");
        setIsAdvance(false);
        setError(null);
        setReload((n) => n + 1);
    }

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        if (busy) return;
        setError(null);
        if (!cari) return setError("Cari seçilmelidir.");
        if (!selected) return setError("Kasa/banka hesabı seçilmelidir.");
        if (amount === null || amount <= 0) return setError("Tutar 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");
        if (differs && (parsedRate === null || parsedRate <= 0)) return setError(errorMessage(new ApiError(400, "", "rate_required")));
        if (!isAdvance && !allocation.valid) return setError("Eşleştirme satırlarındaki hataları düzeltin.");

        setBusy(true);
        try {
            const body = {
                currentAccountId: cari.id,
                accountId: selected.id,
                method,
                date,
                amount,
                rate: differs ? parsedRate : null,
                description: description.trim() || null,
                isAdvance,
                salesOrderId: salesOrderId ?? undefined,
                allocations: !isAdvance && allocation.allocations.length > 0 ? allocation.allocations : undefined,
            };
            setResult(collection ? await treasury.createCollection(body) : await treasury.createPayment(body));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const title = collection ? "Yeni tahsilat" : "Yeni ödeme";

    if (result) {
        return (
            <>
                <PageHeader title={title} />
                <DashboardCard>
                    <Stack spacing={2}>
                        <Alert severity="success">
                            {collection ? "Tahsilat" : "Ödeme"} kaydedildi: {result.number}
                        </Alert>
                        {result.warnings.includes("cash_negative") && (
                            <Alert severity="warning">Kasa bakiyesi eksiye düştü. Kasa hareketlerini kontrol edin.</Alert>
                        )}
                        <Stack direction="row" spacing={1}>
                            <Button variant="contained" onClick={reset}>
                                Yeni kayıt
                            </Button>
                            {cari && (
                                <Button component={Link} href={`/cari/${cari.id}`} variant="outlined">
                                    Cariye git
                                </Button>
                            )}
                        </Stack>
                    </Stack>
                </DashboardCard>
            </>
        );
    }

    return (
        <>
            <PageHeader
                title={title}
                subtitle={collection ? "Hesaba giriş yapılır, cari alacaklandırılır." : "Hesaptan çıkış yapılır, cari borçlandırılır."}
            />
            <DashboardCard>
                <form noValidate onSubmit={submit}>
                    <Stack spacing={2}>
                        <FormAlert error={error} />
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <CariPicker label={collection ? "Müşteri" : "Tedarikçi / personel"} value={cari} onChange={setCari} />
                            <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        </Stack>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField select size="small" label="Yöntem" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)} sx={{ minWidth: 180 }}>
                                {(Object.keys(METHOD_LABELS) as PaymentMethod[]).map((m) => (
                                    <MenuItem key={m} value={m}>
                                        {METHOD_LABELS[m]}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField
                                select
                                size="small"
                                label="Hesap"
                                value={effectiveAccountId}
                                onChange={(e) => setAccountId(e.target.value)}
                                helperText={candidates.length === 0 ? "Bu yönteme uygun etkin hesap yok." : undefined}
                                sx={{ minWidth: 260 }}
                            >
                                {candidates.map((a) => (
                                    <MenuItem key={a.id} value={a.id}>
                                        {a.name} ({a.currency})
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField
                                size="small"
                                label={selected ? `Tutar (${selected.currency})` : "Tutar"}
                                value={amountText}
                                onChange={(e) => setAmountText(e.target.value)}
                                error={amountText.trim() !== "" && amount === null}
                                slotProps={{ htmlInput: { inputMode: "decimal" } }}
                            />
                        </Stack>
                        {rateCurrency && <CurrencyRateField currency={rateCurrency} date={date} value={rate} onChange={setRate} />}
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                            <TextField size="small" label="Açıklama" value={description} onChange={(e) => setDescription(e.target.value)} sx={{ flexGrow: 1 }} />
                            <FormControlLabel control={<Switch checked={isAdvance} onChange={(e) => setIsAdvance(e.target.checked)} />} label="Avans" />
                        </Stack>
                        {cari && !isAdvance && (
                            <div>
                                <Typography variant="h6" component="h2" sx={{ mb: 1 }}>
                                    Vade eşleştirmesi
                                </Typography>
                                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                                    Boş bırakırsanız tutar en eski vadeden başlayarak kendiliğinden eşleşir
                                    {budget !== null && ` (dağıtılabilecek: ${formatMoney(budget, cari.currency)})`}.
                                </Typography>
                                <AllocationPicker items={items} budget={budget} currency={cari.currency} onChange={setAllocation} />
                            </div>
                        )}
                        <div>
                            <Button type="submit" variant="contained" disabled={busy}>
                                {busy ? "Kaydediliyor..." : "Kaydet"}
                            </Button>
                        </div>
                    </Stack>
                </form>
            </DashboardCard>
        </>
    );
}
