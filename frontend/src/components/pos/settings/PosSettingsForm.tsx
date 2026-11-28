"use client";

import { useEffect, useState } from "react";
import FormControlLabel from "@mui/material/FormControlLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Typography from "@mui/material/Typography";
import SettingsFormShell from "@/components/settings/SettingsFormShell";
import { useSettingsForm } from "@/components/settings/useSettingsForm";
import { erp } from "@/lib/api/erp";
import type { EBelgeSettings } from "@/lib/api/invoicing";
import { MODE_LABELS, type LegalDocumentMode, type PosSettings } from "@/lib/api/pos";
import ScaleFormats from "./ScaleFormats";

/** POS ayarları: yasal belge modu (e-Arşiv için e-Belge açık olmalı), terazi barkod biçimleri ve kasa eksiye düşme davranışı. */
export default function PosSettingsForm() {
    const form = useSettingsForm<PosSettings>("pos");
    const [saved, setSaved] = useState(false);
    // null: henüz bilinmiyor ya da okunamadı (sunucu 409 ebelge_required ile yine korur).
    const [ebelge, setEbelge] = useState<boolean | null>(null);
    const value = form.value;

    useEffect(() => {
        let active = true;
        erp.settings.get<EBelgeSettings>("ebelge").then(
            (s) => active && setEbelge(s.value.enabled),
            () => undefined,
        );
        return () => {
            active = false;
        };
    }, []);

    const patch = (change: Partial<PosSettings>) => {
        setSaved(false);
        form.setValue((cur) => (cur ? { ...cur, ...change } : cur));
    };
    const eArchiveBlocked = ebelge === false;

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={async () => setSaved(await form.save())}>
            {value && (
                <Stack spacing={4} sx={{ maxWidth: 900 }}>
                    <Stack spacing={1}>
                        <Typography variant="h6">Yasal belge</Typography>
                        <RadioGroup aria-label="Yasal belge modu" value={value.legalDocumentMode} onChange={(e) => patch({ legalDocumentMode: e.target.value as LegalDocumentMode })}>
                            <FormControlLabel value="fiscalReceipt" control={<Radio />} label={MODE_LABELS.fiscalReceipt} />
                            <FormControlLabel value="eArchiveInvoice" control={<Radio />} disabled={eArchiveBlocked} label={MODE_LABELS.eArchiveInvoice} />
                        </RadioGroup>
                        <Typography variant="caption" color="text.secondary">
                            {eArchiveBlocked
                                ? "e-Arşiv modu için e-Belge ayarlarında etkinleştirilmelidir (Ayarlar > e-Belge)."
                                : "Yazar kasa varsa kasa ekranı bilgi fişi basar; yoksa her satış için e-Arşiv fatura kesilir."}
                        </Typography>
                    </Stack>
                    <FormControlLabel
                        control={<Switch checked={value.allowNegativeCashWarningOnly} onChange={(e) => patch({ allowNegativeCashWarningOnly: e.target.checked })} />}
                        label="Kasa eksiye düşerse yalnızca uyar (kapalıysa işlem reddedilir)"
                    />
                    <Stack spacing={1}>
                        <Typography variant="h6">Terazi barkodları</Typography>
                        <ScaleFormats value={value.scaleBarcodes} onChange={(scaleBarcodes) => patch({ scaleBarcodes })} />
                    </Stack>
                </Stack>
            )}
        </SettingsFormShell>
    );
}
