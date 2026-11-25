"use client";

import FormControlLabel from "@mui/material/FormControlLabel";
import Grid from "@mui/material/Grid";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { AccountMapping } from "@/lib/api/erp";

/** Tek Düzen Hesap Planı varsayılanları (sunucudaki AccountMapping ile aynı). */
export const DEFAULT_MAPPING: AccountMapping = {
    customers: "120",
    suppliers: "320",
    employees: "335",
    advancesGiven: "159",
    advancesReceived: "340",
    cash: "100",
    bank: "102",
    checksReceived: "101",
    checksIssued: "103",
    notesReceived: "121",
    notesPayable: "321",
    sales: "600",
    salesReturns: "610",
    salesDiscounts: "611",
    purchasesInventory: "153",
    vatOutput: "391",
    vatInput: "191",
    withholdingVatPayable: "360",
    fxGain: "646",
    fxLoss: "656",
    expenseDefault: "770",
};

const LABELS: Record<keyof AccountMapping, string> = {
    customers: "Müşteriler",
    suppliers: "Satıcılar",
    employees: "Personel",
    advancesGiven: "Verilen avanslar",
    advancesReceived: "Alınan avanslar",
    cash: "Kasa",
    bank: "Bankalar",
    checksReceived: "Alınan çekler",
    checksIssued: "Verilen çekler",
    notesReceived: "Alacak senetleri",
    notesPayable: "Borç senetleri",
    sales: "Satışlar",
    salesReturns: "Satıştan iadeler",
    salesDiscounts: "Satış iskontoları",
    purchasesInventory: "Ticari mallar",
    vatOutput: "Hesaplanan KDV",
    vatInput: "İndirilecek KDV",
    withholdingVatPayable: "KDV tevkifatı",
    fxGain: "Kur farkı geliri",
    fxLoss: "Kur farkı gideri",
    expenseDefault: "Varsayılan gider",
};

/** Sunucunun kuralı: en çok 30 karakter; yalnızca rakam, nokta ve tire. */
export const isAccountCode = (code: string) => code.trim().length > 0 && code.trim().length <= 30 && /^[0-9.-]+$/.test(code.trim());

type AccountMappingFormProps = {
    mapping: AccountMapping | undefined;
    subAccountPerCari: boolean;
    onChange(next: { accountMapping: AccountMapping; subAccountPerCari: boolean }): void;
};

/** Muhasebe aktarımı hesap eşlemesi: her belge türü için hesap kodu; cari başına alt hesap seçeneği. */
export default function AccountMappingForm({ mapping, subAccountPerCari, onChange }: AccountMappingFormProps) {
    const value = { ...DEFAULT_MAPPING, ...mapping };
    const keys = Object.keys(DEFAULT_MAPPING) as (keyof AccountMapping)[];

    return (
        <Stack spacing={3}>
            <Typography variant="body2" color="text.secondary">
                Yevmiye aktarımında her belge türünün işleneceği hesap kodları. Masraf kategorilerinin kodları Masraflar &gt; Kategoriler bölümündedir.
            </Typography>
            <Grid container spacing={2}>
                {keys.map((key) => {
                    const invalid = !isAccountCode(value[key]);
                    return (
                        <Grid key={key} size={{ xs: 12, sm: 6, md: 4 }}>
                            <TextField
                                fullWidth
                                size="small"
                                label={LABELS[key]}
                                value={value[key]}
                                error={invalid}
                                helperText={invalid ? "Rakam, nokta ve tire; en çok 30 karakter." : undefined}
                                onChange={(e) => onChange({ accountMapping: { ...value, [key]: e.target.value }, subAccountPerCari })}
                            />
                        </Grid>
                    );
                })}
            </Grid>
            <FormControlLabel
                control={<Switch checked={subAccountPerCari} onChange={(_, on) => onChange({ accountMapping: value, subAccountPerCari: on })} />}
                label="Cari başına alt hesap kullan (örn. 120.C-0001)"
            />
        </Stack>
    );
}
