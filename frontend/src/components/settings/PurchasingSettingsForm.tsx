"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { identity, type BranchDto } from "@/lib/api/identity";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import { ALLOCATION_KEY_LABELS, type AllocationKey, type PurchasingSettings } from "@/lib/api/purchasing";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label }, inputLabel: { shrink: true } });

const asText = (n: number) => String(n).replace(".", ",");

/** Satın alma ayarları: fazla teslimat toleransı, fiyat farkı eşiği, karantina (şube başına karantina deposu) ve varsayılan masraf dağıtım anahtarı. */
export default function PurchasingSettingsForm() {
    const { hasPermission } = useSession();
    const canReadStock = hasPermission("stok.read");
    const form = useSettingsForm<PurchasingSettings>("satinalma");
    const [saved, setSaved] = useState(false);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [warehouseError, setWarehouseError] = useState(false);
    const [texts, setTexts] = useState<Record<string, string>>({});
    const [invalid, setInvalid] = useState<Record<string, boolean>>({});
    const value = form.value;

    useEffect(() => {
        let cancelled = false;
        identity.branches.list().then(
            (list) => !cancelled && setBranches(list.filter((b) => b.isActive)),
            () => undefined,
        );
        if (canReadStock) {
            inventory.warehouses().then(
                (list) => !cancelled && setWarehouses(list.filter((w) => w.isActive)),
                () => !cancelled && setWarehouseError(true),
            );
        }
        return () => {
            cancelled = true;
        };
    }, [canReadStock]);

    const patch = (change: Partial<PurchasingSettings>) => {
        setSaved(false);
        form.setValue((current) => (current ? { ...current, ...change } : current));
    };

    // Yüzde alanı metin olarak düzenlenir; geçerliyse sayı forma yazılır, değilse kayıt engellenir.
    const editPercent = (field: "overDeliveryPercent" | "priceVariancePercent", text: string) => {
        const parsed = parseAmount(text);
        setTexts((cur) => ({ ...cur, [field]: text }));
        setInvalid((cur) => ({ ...cur, [field]: parsed === null || parsed > 100 }));
        if (parsed !== null && parsed <= 100) patch({ [field]: parsed });
    };

    const submit = async () => {
        setSaved(false);
        if (invalid.overDeliveryPercent || invalid.priceVariancePercent) return;
        setSaved(await form.save());
    };

    const setWarehouseFor = (branchId: string, warehouseId: string) => {
        if (!value) return;
        const next = { ...(value.quarantineWarehouseByBranch ?? {}) };
        if (warehouseId) next[branchId] = warehouseId;
        else delete next[branchId];
        patch({ quarantineWarehouseByBranch: next });
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={() => void submit()}>
            {value && (
                <Stack spacing={3} sx={{ maxWidth: 560 }}>
                    <TextField
                        label="Fazla teslimat toleransı (%)"
                        value={texts.overDeliveryPercent ?? asText(value.overDeliveryPercent)}
                        onChange={(e) => editPercent("overDeliveryPercent", e.target.value)}
                        error={invalid.overDeliveryPercent}
                        helperText={invalid.overDeliveryPercent ? "0 ile 100 arasında bir yüzde girin." : "Sipariş miktarının bu oran kadar fazlası kabul edilir ve uyarı verir; ötesi reddedilir."}
                        slotProps={{ htmlInput: { inputMode: "decimal" } }}
                    />
                    <TextField
                        label="Fiyat farkı eşiği (%)"
                        value={texts.priceVariancePercent ?? asText(value.priceVariancePercent)}
                        onChange={(e) => editPercent("priceVariancePercent", e.target.value)}
                        error={invalid.priceVariancePercent}
                        helperText={invalid.priceVariancePercent ? "0 ile 100 arasında bir yüzde girin." : "Fatura birim fiyatı mal kabul fiyatından bu orandan fazla saparsa üçlü karşılaştırmada uyarı çıkar."}
                        slotProps={{ htmlInput: { inputMode: "decimal" } }}
                    />
                    <TextField
                        select
                        label="Varsayılan dağıtım anahtarı"
                        value={value.defaultLandedKey}
                        onChange={(e) => patch({ defaultLandedKey: e.target.value as AllocationKey })}
                        slotProps={nativeSelect("Varsayılan dağıtım anahtarı")}
                    >
                        {(Object.keys(ALLOCATION_KEY_LABELS) as AllocationKey[]).map((k) => (
                            <option key={k} value={k}>
                                {ALLOCATION_KEY_LABELS[k]}
                            </option>
                        ))}
                    </TextField>
                    <FormControlLabel
                        control={<Switch checked={value.quarantineEnabled} onChange={(e) => patch({ quarantineEnabled: e.target.checked })} />}
                        label="Karantinayı etkinleştir"
                    />
                    {value.quarantineEnabled && (
                        <Stack spacing={2}>
                            <Typography variant="subtitle2">Şube başına karantina deposu</Typography>
                            <Typography variant="body2" color="text.secondary">
                                Mal kabuller önce bu depoya girer; serbest bırakılana kadar satışa uygun stok sayılmaz.
                            </Typography>
                            {!canReadStock && <Alert severity="info">Depoları listelemek için stok görüntüleme yetkisi gerekir.</Alert>}
                            {warehouseError && <Alert severity="warning">Depolar yüklenemedi.</Alert>}
                            {branches.map((b) => {
                                const options = warehouses.filter((w) => w.branchId === b.id && w.kind === "quarantine");
                                const label = `${b.name} karantina deposu`;
                                return (
                                    <TextField
                                        key={b.id}
                                        select
                                        label={label}
                                        value={value.quarantineWarehouseByBranch?.[b.id] ?? ""}
                                        onChange={(e) => setWarehouseFor(b.id, e.target.value)}
                                        helperText={options.length === 0 && canReadStock ? "Bu şubede karantina türünde depo yok; önce Şubeler bölümünden ekleyin." : undefined}
                                        slotProps={nativeSelect(label)}
                                    >
                                        <option value="">Seçin</option>
                                        {options.map((w) => (
                                            <option key={w.id} value={w.id}>
                                                {w.name}
                                            </option>
                                        ))}
                                    </TextField>
                                );
                            })}
                        </Stack>
                    )}
                </Stack>
            )}
        </SettingsFormShell>
    );
}
