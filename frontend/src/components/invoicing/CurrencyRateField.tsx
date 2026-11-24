"use client";

import { useEffect, useRef, useState } from "react";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { invoicing } from "@/lib/api/invoicing";
import { formatDate } from "@/lib/date";
import { parseAmount } from "@/lib/money";

export type RateState = { value: string; overridden: boolean };

type CurrencyRateFieldProps = {
    currency: string;
    date: string;
    value: RateState;
    onChange(next: RateState): void;
    disabled?: boolean;
    /** true ise kayıtlı (elle girilmiş) kur ilk yüklemede korunur; para birimi/tarih değişince yine yeni kur istenir. */
    keepInitial?: boolean;
};

/** Para birimi TRY değilse tarihin TCMB kurunu getirir; kur düzenlenebilir ve elle girilince "Elle girildi" rozeti çıkar. */
export default function CurrencyRateField({ currency, date, value, onChange, disabled, keepInitial }: CurrencyRateFieldProps) {
    const [info, setInfo] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [reload, setReload] = useState(0);
    const keep = useRef({ onChange, skip: !!keepInitial && value.overridden });
    useEffect(() => {
        keep.current.onChange = onChange;
    });

    const foreign = currency !== "TRY";

    useEffect(() => {
        if (!foreign || !date) return;
        if (keep.current.skip) {
            keep.current.skip = false;
            return;
        }
        let cancelled = false;
        invoicing.rates.lookup(currency, date).then(
            (r) => {
                if (cancelled) return;
                setError(null);
                setInfo(r.effectiveDate !== date ? `TCMB kuru, ${formatDate(r.effectiveDate)} tarihli` : r.overridden ? "Firma kuru" : "TCMB kuru");
                keep.current.onChange({ value: String(r.rate).replace(".", ","), overridden: false });
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setInfo(null);
                setError(errorMessage(e));
            },
        );
        return () => {
            cancelled = true;
        };
    }, [currency, date, foreign, reload]);

    if (!foreign) return null;

    const parsed = parseAmount(value.value, { maxDecimals: 4 });
    return (
        <Stack direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
            <TextField
                size="small"
                label={`Kur (${currency}/TL)`}
                value={value.value}
                disabled={disabled}
                error={!!error || (value.value.trim() !== "" && parsed === null)}
                helperText={error ?? info ?? undefined}
                onChange={(e) => onChange({ value: e.target.value, overridden: true })}
                slotProps={{ htmlInput: { inputMode: "decimal" } }}
                sx={{ width: 190 }}
            />
            {value.overridden ? (
                <>
                    <Chip size="small" color="warning" label="Elle girildi" sx={{ mt: 0.75 }} />
                    <Button size="small" disabled={disabled} onClick={() => setReload((n) => n + 1)} sx={{ mt: 0.25 }}>
                        TCMB kuruna dön
                    </Button>
                </>
            ) : (
                value.value && <Chip size="small" variant="outlined" label="TCMB" sx={{ mt: 0.75 }} />
            )}
        </Stack>
    );
}
