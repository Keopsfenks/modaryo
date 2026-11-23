"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import Grid from "@mui/material/Grid";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { CompanyProfileSettings, IbanEntry } from "@/lib/api/erp";
import { isValidTrIban } from "@/lib/iban";
import { CURRENCIES } from "@/lib/money";
import { isValidTckn, isValidVkn } from "@/lib/taxNumber";
import LogoUpload from "./LogoUpload";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

type Errors = Record<string, string>;
type TextKey = Exclude<keyof CompanyProfileSettings, "logoFileId" | "ibans">;

const TEXT_FIELDS: { key: TextKey; label: string; size?: number; multiline?: boolean }[] = [
    { key: "legalName", label: "Unvan", size: 12 },
    { key: "taxNumber", label: "VKN / TCKN" },
    { key: "taxOffice", label: "Vergi dairesi" },
    { key: "mersisNo", label: "MERSİS no" },
    { key: "tradeRegistryNo", label: "Ticaret sicil no" },
    { key: "address", label: "Adres", size: 12, multiline: true },
    { key: "district", label: "İlçe" },
    { key: "city", label: "İl" },
    { key: "phone", label: "Telefon" },
    { key: "email", label: "E-posta" },
    { key: "website", label: "Web sitesi" },
];

function validate(v: CompanyProfileSettings): Errors {
    const errors: Errors = {};
    const tax = (v.taxNumber ?? "").trim();
    if (tax && !(isValidVkn(tax) || isValidTckn(tax))) {
        errors.taxNumber = "Geçerli bir VKN (10 hane) veya TCKN (11 hane) girin.";
    }
    v.ibans.forEach((row, i) => {
        if (!row.bankName.trim()) {
            errors[`ibans.${i}.bankName`] = "Banka adı zorunludur.";
        }
        if (!isValidTrIban(row.iban)) {
            errors[`ibans.${i}.iban`] = "IBAN geçerli değil (TR ile başlayan 26 karakter).";
        }
    });
    return errors;
}

type CompanyProfileFormProps = {
    /** Verilirse yalnızca bu metin alanları gösterilir; logo ve banka hesapları gizlenir (kurulum sihirbazı). */
    fields?: TextKey[];
    submitLabel?: string;
    onSaved?: () => void;
};

/** Firma bilgileri: unvan, vergi, iletişim, logo ve banka hesapları (IBAN). */
export default function CompanyProfileForm({ fields, submitLabel, onSaved }: CompanyProfileFormProps = {}) {
    const form = useSettingsForm<CompanyProfileSettings>("firma");
    const [errors, setErrors] = useState<Errors>({});
    const [saved, setSaved] = useState(false);
    const value = form.value;
    const shown: Errors = { ...form.fieldErrors, ...errors };

    const set = (patch: Partial<CompanyProfileSettings>) => form.setValue({ ...value!, ...patch });
    const setIban = (i: number, patch: Partial<IbanEntry>) =>
        set({ ibans: value!.ibans.map((row, idx) => (idx === i ? { ...row, ...patch } : row)) });

    const submit = async () => {
        setSaved(false);
        const found = validate(value!);
        setErrors(found);
        if (Object.keys(found).length > 0) {
            return;
        }
        const ok = await form.save();
        setSaved(ok);
        if (ok) {
            onSaved?.();
        }
    };

    const visibleFields = fields ? TEXT_FIELDS.filter((f) => fields.includes(f.key)) : TEXT_FIELDS;

    return (
        <SettingsFormShell form={form} saved={saved && !onSaved} onSubmit={submit} submitLabel={submitLabel}>
            {value && (
                <>
                    <Grid container spacing={2}>
                        {visibleFields.map((f) => (
                            <Grid key={f.key} size={{ xs: 12, md: f.size ?? 6 }}>
                                <TextField
                                    fullWidth
                                    label={f.label}
                                    multiline={f.multiline}
                                    value={value[f.key] ?? ""}
                                    onChange={(e) => set({ [f.key]: e.target.value })}
                                    error={!!shown[f.key]}
                                    helperText={shown[f.key]}
                                />
                            </Grid>
                        ))}
                    </Grid>
                    {!fields && <LogoUpload fileId={value.logoFileId} onChange={(id) => set({ logoFileId: id })} />}
                    {!fields && (
                        <Stack spacing={2}>
                            <Typography variant="h6">Banka hesapları</Typography>
                            {value.ibans.map((row, i) => (
                                <Grid container spacing={2} key={i} sx={{ alignItems: "flex-start" }}>
                                    <Grid size={{ xs: 12, md: 3 }}>
                                        <TextField
                                            fullWidth
                                            label="Banka adı"
                                            value={row.bankName}
                                            onChange={(e) => setIban(i, { bankName: e.target.value })}
                                            error={!!shown[`ibans.${i}.bankName`]}
                                            helperText={shown[`ibans.${i}.bankName`]}
                                        />
                                    </Grid>
                                    <Grid size={{ xs: 12, md: 5 }}>
                                        <TextField
                                            fullWidth
                                            label="IBAN"
                                            value={row.iban}
                                            onChange={(e) => setIban(i, { iban: e.target.value })}
                                            error={!!shown[`ibans.${i}.iban`]}
                                            helperText={shown[`ibans.${i}.iban`]}
                                        />
                                    </Grid>
                                    <Grid size={{ xs: 6, md: 2 }}>
                                        <TextField
                                            select
                                            fullWidth
                                            label="Para birimi"
                                            value={row.currency}
                                            onChange={(e) => setIban(i, { currency: e.target.value as IbanEntry["currency"] })}
                                        >
                                            {CURRENCIES.map((c) => (
                                                <MenuItem key={c} value={c}>
                                                    {c}
                                                </MenuItem>
                                            ))}
                                        </TextField>
                                    </Grid>
                                    <Grid size={{ xs: 6, md: 2 }}>
                                        <Button color="error" onClick={() => set({ ibans: value.ibans.filter((_, idx) => idx !== i) })}>
                                            Kaldır
                                        </Button>
                                    </Grid>
                                </Grid>
                            ))}
                            <div>
                                <Button variant="outlined" onClick={() => set({ ibans: [...value.ibans, { bankName: "", iban: "", currency: "TRY" }] })}>
                                    Hesap ekle
                                </Button>
                            </div>
                        </Stack>
                    )}
                </>
            )}
        </SettingsFormShell>
    );
}
