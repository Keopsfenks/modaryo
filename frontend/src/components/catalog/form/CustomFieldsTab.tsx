"use client";

import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { CustomFieldDto, ProductKind } from "@/lib/api/catalog";
import type { Errors } from "./model";

type CustomFieldsTabProps = {
    definitions: CustomFieldDto[];
    kind: ProductKind;
    values: Record<string, unknown>;
    errors: Errors;
    onChange: (values: Record<string, unknown>) => void;
};

/** Özel alanlar sekmesi: alanlar tanımın türüne göre (metin, sayı, tarih, liste, evet/hayır) çizilir. */
export default function CustomFieldsTab({ definitions, kind, values, errors, onChange }: CustomFieldsTabProps) {
    const applicable = definitions.filter((d) => (kind === "product" ? d.appliesToProduct : d.appliesToService));
    const set = (key: string, value: unknown) => onChange({ ...values, [key]: value });

    if (applicable.length === 0) {
        return <Typography color="text.secondary">Tanımlı özel alan yok. Ayarlar &gt; Katalog sayfasından ekleyebilirsiniz.</Typography>;
    }

    return (
        <Stack spacing={2.5} sx={{ maxWidth: 520 }}>
            {applicable.map((def) => {
                const error = errors[`customFields.${def.key}`];
                const label = def.required ? `${def.label} *` : def.label;
                const raw = values[def.key];
                if (def.type === "bool") {
                    return <FormControlLabel key={def.id} control={<Switch checked={raw === true} onChange={(e) => set(def.key, e.target.checked)} />} label={def.label} />;
                }
                if (def.type === "select") {
                    return (
                        <TextField key={def.id} select label={label} value={typeof raw === "string" ? raw : ""} onChange={(e) => set(def.key, e.target.value)} error={Boolean(error)} helperText={error}>
                            <MenuItem value="">—</MenuItem>
                            {def.options.map((o) => (
                                <MenuItem key={o} value={o}>
                                    {o}
                                </MenuItem>
                            ))}
                        </TextField>
                    );
                }
                if (def.type === "number") {
                    return (
                        <TextField
                            key={def.id}
                            type="number"
                            label={label}
                            value={raw === undefined || raw === null ? "" : String(raw)}
                            onChange={(e) => set(def.key, e.target.value === "" ? "" : Number(e.target.value))}
                            error={Boolean(error)}
                            helperText={error}
                        />
                    );
                }
                return (
                    <TextField
                        key={def.id}
                        type={def.type === "date" ? "date" : "text"}
                        label={label}
                        value={typeof raw === "string" ? raw : ""}
                        onChange={(e) => set(def.key, e.target.value)}
                        error={Boolean(error)}
                        helperText={error}
                        slotProps={def.type === "date" ? { inputLabel: { shrink: true } } : undefined}
                    />
                );
            })}
        </Stack>
    );
}
