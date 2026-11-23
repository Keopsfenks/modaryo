"use client";

import { useState } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import Chip from "@mui/material/Chip";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { VAT_RATES, type CatalogSettings } from "@/lib/api/catalog";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

const TEMPLATE = /^[^{}\s]{0,15}\{0{1,9}\}[^{}\s]{0,5}$/;
const PREFIX = /^\d{7}$/;

function validate(v: CatalogSettings): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!TEMPLATE.test(v.codeTemplate.trim())) errors.codeTemplate = "Ürün kodu şablonu bir önek ve {00000} biçiminde sıra alanı içermelidir (örn. U{00000}).";
    if (!PREFIX.test(v.barcodePrefix.trim())) errors.barcodePrefix = "Barkod öneki 7 haneli sayı olmalıdır.";
    if (v.units.length === 0) errors.units = "En az bir birim tanımlanmalıdır.";
    if (!Number.isInteger(v.nextBarcodeSequence) || v.nextBarcodeSequence < 1 || v.nextBarcodeSequence > 99999) errors.nextBarcodeSequence = "Sonraki barkod sırası 1 ile 99999 arasında olmalıdır.";
    if (!Number.isInteger(v.nextCodeSequence) || v.nextCodeSequence < 1) errors.nextCodeSequence = "Sonraki ürün kodu sırası geçersiz.";
    return errors;
}

/** Katalog ayarları: ürün kodu şablonu, varsayılan KDV, birim listesi, barkod öneki ve sıra sayaçları. */
export default function CatalogSettingsForm() {
    const form = useSettingsForm<CatalogSettings>("katalog");
    const [saved, setSaved] = useState(false);
    const [localErrors, setLocalErrors] = useState<Record<string, string>>({});
    const value = form.value;
    const errors = { ...form.fieldErrors, ...localErrors };

    const patch = (change: Partial<CatalogSettings>) => {
        setSaved(false);
        form.setValue((current) => (current ? { ...current, ...change } : current));
    };

    const submit = async () => {
        setSaved(false);
        const found = validate(value!);
        setLocalErrors(found);
        if (Object.keys(found).length > 0) return;
        setSaved(await form.save());
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={() => void submit()}>
            {value && (
                <Stack spacing={3} sx={{ maxWidth: 560 }}>
                    <TextField
                        label="Ürün kodu şablonu"
                        value={value.codeTemplate}
                        onChange={(e) => patch({ codeTemplate: e.target.value })}
                        error={Boolean(errors.codeTemplate)}
                        helperText={errors.codeTemplate ?? "Örn. U{00000}: önek ve 5 haneli sıra numarası."}
                    />
                    <TextField
                        label="Sonraki ürün kodu sırası"
                        type="number"
                        value={value.nextCodeSequence}
                        onChange={(e) => patch({ nextCodeSequence: Number(e.target.value) })}
                        error={Boolean(errors.nextCodeSequence)}
                        helperText={errors.nextCodeSequence}
                    />
                    <TextField select label="Varsayılan KDV oranı" value={String(value.defaultVatRate)} onChange={(e) => patch({ defaultVatRate: Number(e.target.value) })}>
                        {VAT_RATES.map((r) => (
                            <MenuItem key={r} value={String(r)}>
                                %{r}
                            </MenuItem>
                        ))}
                    </TextField>
                    <Autocomplete
                        multiple
                        freeSolo
                        options={[] as string[]}
                        value={value.units}
                        onChange={(_, units) => patch({ units: [...new Set(units.map((u) => u.trim().toLowerCase()).filter(Boolean))] })}
                        renderValue={(values, getItemProps) =>
                            values.map((unit, index) => {
                                const { key, ...props } = getItemProps({ index });
                                return <Chip key={key} size="small" label={unit} {...props} />;
                            })
                        }
                        renderInput={(params) => <TextField {...params} label="Birimler" error={Boolean(errors.units)} helperText={errors.units ?? "Birim yazıp Enter'a basın."} />}
                    />
                    <TextField
                        label="Barkod öneki"
                        value={value.barcodePrefix}
                        onChange={(e) => patch({ barcodePrefix: e.target.value })}
                        error={Boolean(errors.barcodePrefix)}
                        helperText={errors.barcodePrefix ?? "7 haneli sayı; 2 ile başlayan önekler mağaza içi kullanım içindir. Barkod: önek + 5 haneli sıra + kontrol hanesi."}
                        slotProps={{ htmlInput: { inputMode: "numeric", maxLength: 7 } }}
                    />
                    <TextField
                        label="Sonraki barkod sırası"
                        type="number"
                        value={value.nextBarcodeSequence}
                        onChange={(e) => patch({ nextBarcodeSequence: Number(e.target.value) })}
                        error={Boolean(errors.nextBarcodeSequence)}
                        helperText={errors.nextBarcodeSequence}
                    />
                </Stack>
            )}
        </SettingsFormShell>
    );
}
