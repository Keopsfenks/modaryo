"use client";

import { useEffect, useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Divider from "@mui/material/Divider";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { invoicing, type InvoiceInput, type PreviewDto, type Totals, type VatSummaryRow } from "@/lib/api/invoicing";
import { formatMoney, type Currency } from "@/lib/money";

export const PREVIEW_DEBOUNCE_MS = 400;

type View = { totals: Totals; totalsTl: Totals; vatSummary: VatSummaryRow[] };

type TotalsPanelProps = {
    /** Önizlenecek geçerli gövde; null ise (eksik/geçersiz form) çağrı yapılmaz. */
    input: InvoiceInput | null;
    currency: string;
    onResult?(preview: PreviewDto | null): void;
    /** Verilirse (kesilmiş fatura) sunucuya gidilmez, bu değerler gösterilir. */
    fixed?: View;
};

const money = (n: number, currency: string) => formatMoney(n, currency as Currency);

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
    return (
        <Stack direction="row" sx={{ justifyContent: "space-between" }}>
            <Typography variant={strong ? "subtitle1" : "body2"} sx={{ fontWeight: strong ? 700 : 400 }}>
                {label}
            </Typography>
            <Typography variant={strong ? "subtitle1" : "body2"} sx={{ fontWeight: strong ? 700 : 400 }}>
                {value}
            </Typography>
        </Stack>
    );
}

/** Toplamlar paneli. Tüm tutarlar sunucudaki /invoices/preview ucundan gelir (400 ms geciktirilmiş); istemci vergi hesaplamaz. */
export default function TotalsPanel({ input, currency, onResult, fixed }: TotalsPanelProps) {
    const [preview, setPreview] = useState<View | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);
    const callback = useRef(onResult);
    useEffect(() => {
        callback.current = onResult;
    });

    const key = fixed ? null : input ? JSON.stringify(input) : null;

    useEffect(() => {
        if (fixed) return;
        if (!key || !input) {
            callback.current?.(null);
            return;
        }
        const controller = new AbortController();
        const handle = setTimeout(() => {
            setLoading(true);
            invoicing.preview(input, controller.signal).then(
                (result) => {
                    setPreview(result);
                    setError(null);
                    setLoading(false);
                    callback.current?.(result);
                },
                (e: unknown) => {
                    if (controller.signal.aborted) return;
                    if (!(e instanceof ApiError)) throw e;
                    setError(errorMessage(e));
                    setPreview(null);
                    setLoading(false);
                    callback.current?.(null);
                },
            );
        }, PREVIEW_DEBOUNCE_MS);
        return () => {
            clearTimeout(handle);
            controller.abort();
        };
        // input, key'in kaynağıdır; yalnızca içerik değişince yeniden çalışır.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [key, fixed]);

    const view = fixed ?? (key ? preview : null);
    const shownError = key ? error : null;
    const foreign = currency !== "TRY";

    return (
        <Stack spacing={1} aria-busy={loading} aria-label="Toplamlar">
            <Typography variant="h6">Toplamlar</Typography>
            {shownError && <Alert severity="warning">{shownError}</Alert>}
            {!view && !shownError && <Typography color="text.secondary">Toplamlar için satırları ve fiyatları girin.</Typography>}
            {view && (
                <>
                    <Row label="Ara toplam" value={money(view.totals.gross, currency)} />
                    {view.totals.discount > 0 && <Row label="İskonto" value={`-${money(view.totals.discount, currency)}`} />}
                    <Row label="Net tutar" value={money(view.totals.net, currency)} />
                    {view.totals.otv > 0 && <Row label="ÖTV" value={money(view.totals.otv, currency)} />}
                    <Divider />
                    {view.vatSummary.map((v) => (
                        <Row key={v.rate} label={`KDV %${v.rate} (matrah ${money(v.base, currency)})`} value={money(v.vat, currency)} />
                    ))}
                    <Row label="Toplam KDV" value={money(view.totals.vat, currency)} />
                    {view.totals.withheld > 0 && <Row label="Tevkifat" value={`-${money(view.totals.withheld, currency)}`} />}
                    <Divider />
                    <Row strong label="Ödenecek" value={money(view.totals.payable, currency)} />
                    {foreign && <Row label="TL karşılığı" value={money(view.totalsTl.payable, "TRY")} />}
                </>
            )}
        </Stack>
    );
}
