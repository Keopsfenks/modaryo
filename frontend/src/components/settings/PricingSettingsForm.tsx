"use client";

import { useEffect, useState } from "react";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import { pricing, type PriceListDto, type PricingSettings, type RoundingRule } from "@/lib/api/pricing";
import { useSession } from "@/session/SessionProvider";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

export const ROUNDING_OPTIONS: { value: RoundingRule; label: string }[] = [
    { value: "none", label: "Yuvarlama yok" },
    { value: "cent", label: "0,01" },
    { value: "fiveCents", label: "0,05" },
    { value: "tenCents", label: "0,10" },
    { value: "fiftyCents", label: "0,50" },
    { value: "one", label: "1" },
    { value: "nineEnding", label: "9 ile biten (örn. 99,90)" },
];

/** Fiyatlandırma ayarları: varsayılan satış/alış listesi, KDV gösterimi ve türetilmiş fiyat yuvarlaması. */
export default function PricingSettingsForm() {
    const { hasPermission } = useSession();
    const canSeePurchase = hasPermission("fiyat.alis");
    const form = useSettingsForm<PricingSettings>("fiyat");
    const [saved, setSaved] = useState(false);
    const [saleLists, setSaleLists] = useState<PriceListDto[]>([]);
    const [purchaseLists, setPurchaseLists] = useState<PriceListDto[]>([]);
    const value = form.value;

    useEffect(() => {
        pricing.lists({ kind: "sale" }).then(setSaleLists, () => undefined);
        if (canSeePurchase) pricing.lists({ kind: "purchase" }).then(setPurchaseLists, () => undefined);
    }, [canSeePurchase]);

    const patch = (change: Partial<PricingSettings>) => {
        setSaved(false);
        form.setValue((current) => (current ? { ...current, ...change } : current));
    };

    const submit = async () => {
        setSaved(false);
        setSaved(await form.save());
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={() => void submit()}>
            {value && (
                <Stack spacing={3} sx={{ maxWidth: 560 }}>
                    <TextField
                        select
                        label="Varsayılan satış listesi"
                        value={value.defaultSaleListId ?? ""}
                        onChange={(e) => patch({ defaultSaleListId: e.target.value || null })}
                        error={Boolean(form.fieldErrors.defaultSaleListId)}
                        helperText={form.fieldErrors.defaultSaleListId ?? "Müşteriye özel fiyat ve grup listesi yoksa kullanılır."}
                    >
                        <MenuItem value="">Seçilmedi</MenuItem>
                        {saleLists.map((l) => (
                            <MenuItem key={l.id} value={l.id}>
                                {l.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        select
                        label="Varsayılan alış listesi"
                        value={value.defaultPurchaseListId ?? ""}
                        disabled={!canSeePurchase}
                        onChange={(e) => patch({ defaultPurchaseListId: e.target.value || null })}
                        error={Boolean(form.fieldErrors.defaultPurchaseListId)}
                        helperText={form.fieldErrors.defaultPurchaseListId ?? (canSeePurchase ? "Tedarikçi listesi yoksa kullanılır." : "Alış listelerini görmek için fiyat.alis izni gerekir.")}
                    >
                        <MenuItem value="">Seçilmedi</MenuItem>
                        {purchaseLists.map((l) => (
                            <MenuItem key={l.id} value={l.id}>
                                {l.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <FormControlLabel
                        control={<Switch checked={value.showVatIncluded} onChange={(e) => patch({ showVatIncluded: e.target.checked })} />}
                        label="Fiyatları KDV dahil göster"
                    />
                    <TextField
                        select
                        label="Yuvarlama kuralı"
                        value={value.rounding}
                        onChange={(e) => patch({ rounding: e.target.value as RoundingRule })}
                        helperText="Yalnızca türetilmiş listelerin ve toplu ayarın hesapladığı fiyatlara uygulanır."
                    >
                        {ROUNDING_OPTIONS.map((o) => (
                            <MenuItem key={o.value} value={o.value}>
                                {o.label}
                            </MenuItem>
                        ))}
                    </TextField>
                </Stack>
            )}
        </SettingsFormShell>
    );
}
