"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import type { EBelgeSettings } from "@/lib/api/invoicing";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

/** e-Belge ayarları: e-Fatura / e-Arşiv kullanımı ve e-Fatura varsayılan senaryosu. */
export default function EBelgeSettingsForm() {
    const form = useSettingsForm<EBelgeSettings>("ebelge");
    const [saved, setSaved] = useState(false);
    const value = form.value;
    const patch = (change: Partial<EBelgeSettings>) => form.setValue((current) => (current ? { ...current, ...change } : current));

    return (
        <SettingsFormShell
            form={form}
            saved={saved}
            onSubmit={async () => {
                setSaved(false);
                setSaved(await form.save());
            }}
        >
            {value && (
                <>
                    <FormControlLabel control={<Switch checked={value.enabled} onChange={(e) => patch({ enabled: e.target.checked })} />} label="e-Fatura / e-Arşiv kullanıyorum" />
                    <Alert severity="info">
                        Açıkken kesilen faturalar e-belge serilerinden numaralanır: alıcı e-Fatura mükellefiyse e-Fatura, değilse e-Arşiv. Kapalıyken kâğıt serileri kullanılır. Seriler Belgeler ayarlarında tanımlanır.
                    </Alert>
                    <TextField
                        select
                        label="Varsayılan e-Fatura senaryosu"
                        value={value.defaultScenario}
                        onChange={(e) => patch({ defaultScenario: e.target.value as EBelgeSettings["defaultScenario"] })}
                        disabled={!value.enabled}
                        sx={{ maxWidth: 360 }}
                    >
                        <MenuItem value="basic">Temel fatura</MenuItem>
                        <MenuItem value="commercial">Ticari fatura</MenuItem>
                    </TextField>
                    <FormControlLabel control={<Switch checked={value.eIrsaliyeEnabled} onChange={(e) => patch({ eIrsaliyeEnabled: e.target.checked })} />} label="e-İrsaliye kullanıyorum" />
                </>
            )}
        </SettingsFormShell>
    );
}
