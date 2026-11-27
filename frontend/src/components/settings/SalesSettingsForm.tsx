"use client";

import { useState } from "react";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import type { SalesSettings } from "@/lib/api/sales";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

type NumberField = "quoteValidityDays" | "defaultInstallmentCount";

const LIMITS: Record<NumberField, [number, number]> = { quoteValidityDays: [1, 365], defaultInstallmentCount: [1, 36] };

/** Satış ayarları: teklif geçerlilik günü, satış temsilcisi zorunluluğu, varsayılan taksit sayısı ve ön ödeme varsayılanı. */
export default function SalesSettingsForm() {
    const form = useSettingsForm<SalesSettings>("satis");
    const [saved, setSaved] = useState(false);
    const [texts, setTexts] = useState<Partial<Record<NumberField, string>>>({});
    const [invalid, setInvalid] = useState<Partial<Record<NumberField, boolean>>>({});
    const value = form.value;

    const patch = (change: Partial<SalesSettings>) => {
        setSaved(false);
        form.setValue((current) => (current ? { ...current, ...change } : current));
    };

    // Sayı alanları metin olarak düzenlenir; geçerliyse forma yazılır, değilse kayıt engellenir.
    const edit = (field: NumberField, input: string) => {
        const [min, max] = LIMITS[field];
        const n = Number(input.trim());
        const ok = /^\d+$/.test(input.trim()) && n >= min && n <= max;
        setTexts((cur) => ({ ...cur, [field]: input }));
        setInvalid((cur) => ({ ...cur, [field]: !ok }));
        if (ok) patch({ [field]: n });
    };

    const submit = async () => {
        setSaved(false);
        if (invalid.quoteValidityDays || invalid.defaultInstallmentCount) return;
        setSaved(await form.save());
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={() => void submit()}>
            {value && (
                <Stack spacing={3} sx={{ maxWidth: 560 }}>
                    <TextField
                        label="Teklif geçerlilik süresi (gün)"
                        value={texts.quoteValidityDays ?? String(value.quoteValidityDays)}
                        onChange={(e) => edit("quoteValidityDays", e.target.value)}
                        error={invalid.quoteValidityDays}
                        helperText={invalid.quoteValidityDays ? "1 ile 365 arasında bir gün sayısı girin." : "Geçerlilik tarihi girilmeyen tekliflerde varsayılan olarak kullanılır."}
                        slotProps={{ htmlInput: { inputMode: "numeric" } }}
                    />
                    <TextField
                        label="Varsayılan taksit sayısı"
                        value={texts.defaultInstallmentCount ?? String(value.defaultInstallmentCount)}
                        onChange={(e) => edit("defaultInstallmentCount", e.target.value)}
                        error={invalid.defaultInstallmentCount}
                        helperText={invalid.defaultInstallmentCount ? "1 ile 36 arasında bir taksit sayısı girin." : "Sipariş taksit planı eşit bölünürken önerilen taksit sayısı."}
                        slotProps={{ htmlInput: { inputMode: "numeric" } }}
                    />
                    <FormControlLabel
                        control={<Switch checked={value.requireSalesRep} onChange={(e) => patch({ requireSalesRep: e.target.checked })} />}
                        label="Teklif ve siparişte satış temsilcisi zorunlu"
                    />
                    <FormControlLabel
                        control={<Switch checked={value.prepaymentDefaultOff} onChange={(e) => patch({ prepaymentDefaultOff: e.target.checked })} />}
                        label="Ön ödeme varsayılan olarak kapalı"
                    />
                </Stack>
            )}
        </SettingsFormShell>
    );
}
