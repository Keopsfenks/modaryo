"use client";

import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { CURRENCIES, type Currency } from "@/lib/money";

const LABELS: Record<Currency, string> = {
    TRY: "TRY - Türk Lirası",
    USD: "USD - ABD Doları",
    EUR: "EUR - Euro",
    GBP: "GBP - İngiliz Sterlini",
};

type CurrencySelectProps = {
    value: Currency;
    onChange: (value: Currency) => void;
    disabled?: boolean;
    error?: string;
    helperText?: string;
    label?: string;
    id?: string;
};

/** Para birimi seçici (yalnızca TRY, USD, EUR, GBP). */
export default function CurrencySelect({ value, onChange, disabled, error, helperText, label = "Para birimi", id = "currency" }: CurrencySelectProps) {
    return (
        <TextField
            select
            id={id}
            label={label}
            fullWidth
            value={value}
            disabled={disabled}
            error={Boolean(error)}
            helperText={error ?? helperText}
            onChange={(e) => onChange(e.target.value as Currency)}
        >
            {CURRENCIES.map((c) => (
                <MenuItem key={c} value={c}>
                    {LABELS[c]}
                </MenuItem>
            ))}
        </TextField>
    );
}
