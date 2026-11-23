"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormGroup from "@mui/material/FormGroup";
import FormLabel from "@mui/material/FormLabel";
import Grid from "@mui/material/Grid";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { DateFormat, GeneralSettings, WeekDay } from "@/lib/api/erp";
import { CURRENCIES } from "@/lib/money";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

const DAYS: { key: WeekDay; label: string }[] = [
    { key: "monday", label: "Pazartesi" },
    { key: "tuesday", label: "Salı" },
    { key: "wednesday", label: "Çarşamba" },
    { key: "thursday", label: "Perşembe" },
    { key: "friday", label: "Cuma" },
    { key: "saturday", label: "Cumartesi" },
    { key: "sunday", label: "Pazar" },
];

const DATE_FORMATS: { key: DateFormat; label: string }[] = [
    { key: "dd.MM.yyyy", label: "GG.AA.YYYY (31.12.2026)" },
    { key: "yyyy-MM-dd", label: "YYYY-AA-GG (2026-12-31)" },
    { key: "dd/MM/yyyy", label: "GG/AA/YYYY (31/12/2026)" },
];

/** Genel ayarlar: para birimi, ondalıklar, tarih biçimi, hafta/çalışma günleri ve tatiller. */
export default function GeneralSettingsForm() {
    const form = useSettingsForm<GeneralSettings>("genel");
    const [saved, setSaved] = useState(false);
    const [newHoliday, setNewHoliday] = useState("");
    const [validation, setValidation] = useState<string | null>(null);
    const [clientErrors, setClientErrors] = useState<Record<string, string>>({});
    const shown: Record<string, string> = { ...form.fieldErrors, ...clientErrors };
    const value = form.value;

    const set = (patch: Partial<GeneralSettings>) => form.setValue({ ...value!, ...patch });
    const num = (text: string) => (text === "" ? 0 : Number(text));

    const submit = async () => {
        setSaved(false);
        const found: Record<string, string> = {};
        const q = value!.quantityDecimals;
        const u = value!.unitPriceDecimals;
        if (!Number.isInteger(q) || q < 0 || q > 4) {
            found.quantityDecimals = "Miktar ondalık basamağı 0 ile 4 arasında olmalıdır.";
        }
        if (!Number.isInteger(u) || u < 2 || u > 6) {
            found.unitPriceDecimals = "Birim fiyat ondalık basamağı 2 ile 6 arasında olmalıdır.";
        }
        setClientErrors(found);
        if (value!.workDays.length === 0) {
            setValidation("En az bir çalışma günü seçilmelidir.");
            return;
        }
        setValidation(null);
        if (Object.keys(found).length > 0) {
            return;
        }
        setSaved(await form.save());
    };

    const addHoliday = () => {
        if (newHoliday && !value!.holidays.includes(newHoliday)) {
            set({ holidays: [...value!.holidays, newHoliday].sort() });
        }
        setNewHoliday("");
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={submit}>
            {value && (
                <>
                    <Grid container spacing={2}>
                        <Grid size={{ xs: 12, md: 4 }}>
                            <TextField select fullWidth label="Varsayılan para birimi" value={value.defaultCurrency} error={!!shown.defaultCurrency} helperText={shown.defaultCurrency} onChange={(e) => set({ defaultCurrency: e.target.value as GeneralSettings["defaultCurrency"] })}>
                                {CURRENCIES.map((c) => (
                                    <MenuItem key={c} value={c}>
                                        {c}
                                    </MenuItem>
                                ))}
                            </TextField>
                        </Grid>
                        <Grid size={{ xs: 12, md: 4 }}>
                            <TextField select fullWidth label="Tarih biçimi" value={value.dateFormat} error={!!shown.dateFormat} helperText={shown.dateFormat} onChange={(e) => set({ dateFormat: e.target.value as DateFormat })}>
                                {DATE_FORMATS.map((f) => (
                                    <MenuItem key={f.key} value={f.key}>
                                        {f.label}
                                    </MenuItem>
                                ))}
                            </TextField>
                        </Grid>
                        <Grid size={{ xs: 12, md: 4 }}>
                            <TextField select fullWidth label="Haftanın ilk günü" value={value.weekStart} error={!!shown.weekStart} helperText={shown.weekStart} onChange={(e) => set({ weekStart: e.target.value as WeekDay })}>
                                {DAYS.map((d) => (
                                    <MenuItem key={d.key} value={d.key}>
                                        {d.label}
                                    </MenuItem>
                                ))}
                            </TextField>
                        </Grid>
                        <Grid size={{ xs: 12, md: 6 }}>
                            <TextField
                                fullWidth
                                type="number"
                                label="Miktar ondalık basamağı"
                                error={!!shown.quantityDecimals}
                                helperText={shown.quantityDecimals ?? "0 ile 4 arasında"}
                                value={value.quantityDecimals}
                                onChange={(e) => set({ quantityDecimals: num(e.target.value) })}
                                slotProps={{ htmlInput: { min: 0, max: 4 } }}
                            />
                        </Grid>
                        <Grid size={{ xs: 12, md: 6 }}>
                            <TextField
                                fullWidth
                                type="number"
                                label="Birim fiyat ondalık basamağı"
                                error={!!shown.unitPriceDecimals}
                                helperText={shown.unitPriceDecimals ?? "2 ile 6 arasında"}
                                value={value.unitPriceDecimals}
                                onChange={(e) => set({ unitPriceDecimals: num(e.target.value) })}
                                slotProps={{ htmlInput: { min: 2, max: 6 } }}
                            />
                        </Grid>
                    </Grid>

                    <div>
                        <FormLabel component="legend">Çalışma günleri</FormLabel>
                        <FormGroup row>
                            {DAYS.map((d) => (
                                <FormControlLabel
                                    key={d.key}
                                    label={d.label}
                                    control={
                                        <Checkbox
                                            checked={value.workDays.includes(d.key)}
                                            onChange={(e) =>
                                                set({ workDays: e.target.checked ? [...value.workDays, d.key] : value.workDays.filter((x) => x !== d.key) })
                                            }
                                        />
                                    }
                                />
                            ))}
                        </FormGroup>
                        {validation && (
                            <Typography color="error" variant="caption">
                                {validation}
                            </Typography>
                        )}
                    </div>

                    <Stack spacing={1}>
                        <Typography variant="h6">Resmi tatiller</Typography>
                        <Stack direction="row" spacing={1}>
                            <TextField type="date" label="Tatil günü" size="small" value={newHoliday} onChange={(e) => setNewHoliday(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <Button variant="outlined" onClick={addHoliday}>
                                Ekle
                            </Button>
                        </Stack>
                        {value.holidays.map((h) => (
                            <Stack key={h} direction="row" spacing={2} sx={{ alignItems: "center" }}>
                                <Typography>{h}</Typography>
                                <Button size="small" color="error" aria-label={`${h} tatilini kaldır`} onClick={() => set({ holidays: value.holidays.filter((x) => x !== h) })}>
                                    Kaldır
                                </Button>
                            </Stack>
                        ))}
                    </Stack>
                </>
            )}
        </SettingsFormShell>
    );
}
