"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import FormLabel from "@mui/material/FormLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import { inventory, type CostingMethod, type StockSettings } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

export const COSTING_OPTIONS: { value: CostingMethod; label: string; help: string }[] = [
    { value: "weightedAverage", label: "Ağırlıklı ortalama", help: "Her giriş ortalama maliyeti günceller; çıkışlar ortalama maliyetle yazılır." },
    { value: "fifo", label: "FIFO (ilk giren ilk çıkar)", help: "Çıkışlar en eski giriş katmanlarının maliyetiyle yazılır." },
];

/** Stok ayarları: maliyet yöntemi (hareket varken kilitli) ve eksi stok izni. */
export default function StockSettingsForm() {
    const { hasPermission } = useSession();
    const canReadStock = hasPermission("stok.read");
    const form = useSettingsForm<StockSettings>("stok");
    const [saved, setSaved] = useState(false);
    // null: bilinmiyor (stok.read yok); sunucu yine de 409 costing_method_locked ile korur.
    const [hasMovements, setHasMovements] = useState<boolean | null>(null);
    const value = form.value;

    useEffect(() => {
        if (!canReadStock) return;
        inventory.balances({ pageSize: 1 }).then(
            (r) => setHasMovements(r.total > 0),
            () => setHasMovements(null),
        );
    }, [canReadStock]);

    const locked = hasMovements === true;

    const patch = (change: Partial<StockSettings>) => {
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
                    <FormControl>
                        <FormLabel id="stock-method-label">Maliyet yöntemi</FormLabel>
                        <RadioGroup aria-labelledby="stock-method-label" value={value.method} onChange={(e) => patch({ method: e.target.value as CostingMethod })}>
                            {COSTING_OPTIONS.map((o) => (
                                <FormControlLabel key={o.value} value={o.value} disabled={locked} control={<Radio />} label={`${o.label} — ${o.help}`} />
                            ))}
                        </RadioGroup>
                        {locked && (
                            <Alert severity="info" sx={{ mt: 1 }}>
                                Firmada stok hareketi bulunduğu için maliyet yöntemi değiştirilemez. Yöntem yalnızca hiç hareket yokken seçilebilir.
                            </Alert>
                        )}
                        {!locked && <FormHelperText>Yöntem, ilk stok hareketinden sonra değiştirilemez.</FormHelperText>}
                    </FormControl>
                    <FormControlLabel
                        control={<Switch checked={value.allowNegative} onChange={(e) => patch({ allowNegative: e.target.checked })} />}
                        label="Eksi stoğa izin ver"
                    />
                </Stack>
            )}
        </SettingsFormShell>
    );
}
