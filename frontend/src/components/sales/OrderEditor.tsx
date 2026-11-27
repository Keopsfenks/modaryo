"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import TotalsPanel from "@/components/invoicing/TotalsPanel";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { sales, type OrderDto, type OrderInput } from "@/lib/api/sales";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import InstallmentPlanEditor from "./InstallmentPlanEditor";
import OrderDetail, { OrderActions, ORDER_BASE } from "./OrderDetail";
import SalesLines from "./SalesLines";
import { SalesHeaderFields, useSalesDoc, type SalesDocInitial } from "./salesDoc";
import { hydrateLines, lineFromDto } from "./salesModel";

function initialOf(o: OrderDto): SalesDocInitial {
    return {
        customer: { id: o.currentAccountId, name: o.customerName },
        branchId: o.branchId,
        date: o.date,
        currency: o.currency,
        salesRepUserId: o.salesRepUserId,
        docDiscount: o.docDiscount,
        notes: o.notes,
        lines: o.lines.map((l) => lineFromDto(l)),
    };
}

type EditorProps = { id?: string };

/** Sipariş sayfası: yeni taslak, taslak düzenleme ya da (taslak değilse) ayrıntı ve işlemler. */
export default function OrderEditor({ id }: EditorProps) {
    const { hasPermission } = useSession();
    const [order, setOrder] = useState<OrderDto | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [version, setVersion] = useState(0);
    const [defaultCount, setDefaultCount] = useState<number | undefined>(undefined);
    const allowed = hasPermission("satis.read");

    useEffect(() => {
        if (!id || !allowed) return;
        let cancelled = false;
        sales.orders.get(id).then(
            (dto) => !cancelled && setOrder(dto),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [id, allowed, version]);

    useEffect(() => {
        let cancelled = false;
        sales.settings().then(
            (s) => !cancelled && setDefaultCount(s.value.defaultInstallmentCount),
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, []);

    if (!allowed) return <Alert severity="warning">Siparişi görüntülemek için yetkiniz yok.</Alert>;
    if (error) return <FormAlert error={error} />;

    const canManage = hasPermission("satis.manage");
    if (!id) return canManage ? <OrderForm initial={null} order={null} onChanged={() => undefined} /> : <Alert severity="warning">Sipariş girmek için satış yönetimi yetkisi gerekir.</Alert>;
    if (!order) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress />
            </Box>
        );
    }
    const bump = () => setVersion((v) => v + 1);
    if (order.status !== "draft" || !canManage) {
        const live = order.status !== "cancelled" && order.status !== "closed";
        return <OrderDetail order={order} planEditable={canManage && live} defaultInstallmentCount={defaultCount} onChanged={bump} />;
    }
    return <OrderForm key={`${order.id}-${order.version}`} initial={initialOf(order)} order={order} defaultInstallmentCount={defaultCount} onChanged={bump} />;
}

type FormProps = { initial: SalesDocInitial | null; order: OrderDto | null; defaultInstallmentCount?: number; onChanged(): void };

function OrderForm({ initial, order, defaultInstallmentCount, onChanged }: FormProps) {
    const router = useRouter();
    const doc = useSalesDoc(initial);
    const [prepay, setPrepay] = useState(order?.requiresPrepayment ?? false);
    const [prepayMode, setPrepayMode] = useState<"percent" | "amount">(order?.prepaymentAmount != null ? "amount" : "percent");
    const [prepayValue, setPrepayValue] = useState(() => {
        const v = order?.prepaymentAmount ?? order?.prepaymentPercent;
        return v == null ? "" : String(v).replace(".", ",");
    });
    const [error, setError] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);

    // Kayıtlı taslakta birim listesi ve ölçü bilgisi katalogdan tamamlanır.
    useEffect(() => {
        if (!initial) return;
        let cancelled = false;
        hydrateLines(initial.lines).then((lines) => {
            if (!cancelled) doc.setLines(lines);
        });
        return () => {
            cancelled = true;
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    async function save(): Promise<OrderDto | null> {
        setError(null);
        setSaved(false);
        const problem = doc.validate();
        if (problem) {
            setError(problem);
            return null;
        }
        const prepayNumber = prepay ? parseAmount(prepayValue) : null;
        if (prepay && (prepayNumber === null || prepayNumber <= 0 || (prepayMode === "percent" && prepayNumber > 100))) {
            setError("Ön ödeme tutarı ya da yüzdesi geçersiz.");
            return null;
        }
        const input: OrderInput = {
            ...doc.body(),
            version: order?.version ?? null,
            requiresPrepayment: prepay,
            prepaymentPercent: prepay && prepayMode === "percent" ? prepayNumber : null,
            prepaymentAmount: prepay && prepayMode === "amount" ? prepayNumber : null,
        };
        setBusy(true);
        try {
            const dto = order ? await sales.orders.update(order.id, input) : await sales.orders.create(input);
            setSaved(true);
            if (!order) router.replace(`${ORDER_BASE}/${dto.id}`);
            else onChanged();
            return dto;
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(Object.keys(e.fieldErrors).length === 0 ? errorMessage(e) : e);
            return null;
        } finally {
            setBusy(false);
        }
    }

    async function saveAndApprove() {
        const dto = await save();
        if (!dto) return;
        setBusy(true);
        try {
            await sales.orders.approve(dto.id);
            router.replace(`${ORDER_BASE}/${dto.id}`);
            onChanged();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title={order ? `Taslak sipariş ${order.number}` : "Yeni sipariş"} subtitle="Taslak olarak kaydedilir; onaylanınca kredi limiti, ön ödeme ve stok kuralları işler" />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {saved && <Alert severity="success">Taslak kaydedildi.</Alert>}
                {order && <OrderActions order={order} onChanged={onChanged} onError={setError} />}

                <DashboardCard title="Başlık">
                    <SalesHeaderFields doc={doc} />
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mt: 2, alignItems: { md: "center" } }}>
                        <FormControlLabel control={<Switch checked={prepay} onChange={(e) => setPrepay(e.target.checked)} />} label="Ön ödeme gerekli" />
                        {prepay && (
                            <>
                                <TextField select size="small" label="Ön ödeme türü" value={prepayMode} onChange={(e) => setPrepayMode(e.target.value as "percent" | "amount")} slotProps={{ select: { native: true }, htmlInput: { "aria-label": "Ön ödeme türü" } }} sx={{ width: 150 }}>
                                    <option value="percent">Yüzde</option>
                                    <option value="amount">Tutar</option>
                                </TextField>
                                <TextField size="small" label={prepayMode === "percent" ? "Ön ödeme %" : "Ön ödeme tutarı"} value={prepayValue} onChange={(e) => setPrepayValue(e.target.value)} slotProps={{ htmlInput: { inputMode: "decimal" } }} sx={{ width: 160 }} />
                            </>
                        )}
                    </Stack>
                </DashboardCard>

                <DashboardCard title="Satırlar">
                    <SalesLines lines={doc.lines} onChange={doc.setLines} currentAccountId={doc.customer?.id ?? ""} date={doc.date} currency={doc.currency} showProfit={!!order} />
                </DashboardCard>

                {order && (
                    <DashboardCard title="Taksit planı" subtitle="Plan, kaydedilmiş sipariş tutarına göre bölünür; satırları değiştirdiyseniz önce siparişi kaydedin.">
                        <InstallmentPlanEditor
                            key={`${order.id}-${order.version}-${order.total}`}
                            total={order.total}
                            currency={order.currency}
                            plan={order.plan}
                            defaultCount={defaultInstallmentCount}
                            onSave={async (input) => {
                                await sales.orders.setPlan(order.id, input);
                                onChanged();
                            }}
                        />
                    </DashboardCard>
                )}

                <Stack direction={{ xs: "column", md: "row" }} spacing={3} sx={{ justifyContent: "space-between" }}>
                    <Box sx={{ minWidth: 320, maxWidth: 420, flexGrow: 1 }}>
                        <DashboardCard>
                            <TotalsPanel input={doc.previewInput} currency={doc.currency} />
                        </DashboardCard>
                    </Box>
                    <Stack direction="row" spacing={1} sx={{ alignItems: "flex-end", flexWrap: "wrap", gap: 1 }}>
                        <Button variant="contained" disabled={busy} onClick={() => void save()}>
                            {busy ? "Kaydediliyor..." : "Taslağı kaydet"}
                        </Button>
                        <Button variant="contained" color="success" disabled={busy} onClick={() => void saveAndApprove()}>
                            Kaydet ve onayla
                        </Button>
                    </Stack>
                </Stack>
            </Stack>
        </>
    );
}
