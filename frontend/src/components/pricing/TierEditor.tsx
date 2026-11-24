"use client";

import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { IconPlus, IconTrash } from "@tabler/icons-react";
import { validateTiers, type TierText } from "./tiers";

type TierEditorProps = {
    tiers: TierText[];
    onChange: (tiers: TierText[]) => void;
    unit?: string;
    disabled?: boolean;
};

/** Miktar kademeleri: "şu miktardan itibaren şu fiyat". Kurallar tiers.validateTiers'ta; hata anında gösterilir. */
export default function TierEditor({ tiers, onChange, unit, disabled }: TierEditorProps) {
    const error = validateTiers(tiers);

    const patch = (index: number, change: Partial<TierText>) => onChange(tiers.map((t, i) => (i === index ? { ...t, ...change } : t)));

    const add = () => {
        const last = tiers[tiers.length - 1];
        const next = last ? String(Number(last.minQty.replace(",", ".")) * 2 || 2).replace(".", ",") : "1";
        onChange([...tiers, { minQty: next, price: "" }]);
    };

    return (
        <Stack spacing={1.5}>
            {tiers.map((tier, index) => (
                <Stack key={index} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                    <TextField
                        size="small"
                        label={`${index + 1}. kademe miktarı`}
                        value={tier.minQty}
                        disabled={disabled}
                        onChange={(e) => patch(index, { minQty: e.target.value })}
                        slotProps={{ htmlInput: { inputMode: "decimal" }, input: unit ? { endAdornment: <span>{unit}+</span> } : undefined }}
                        sx={{ width: 190 }}
                    />
                    <TextField
                        size="small"
                        label={`${index + 1}. kademe fiyatı`}
                        value={tier.price}
                        disabled={disabled}
                        onChange={(e) => patch(index, { price: e.target.value })}
                        slotProps={{ htmlInput: { inputMode: "decimal" } }}
                        sx={{ width: 190 }}
                    />
                    <IconButton
                        aria-label={`${index + 1}. kademeyi sil`}
                        size="small"
                        disabled={disabled || tiers.length === 1}
                        onClick={() => onChange(tiers.filter((_, i) => i !== index))}
                    >
                        <IconTrash size={18} />
                    </IconButton>
                </Stack>
            ))}
            {error && (
                <Alert severity="error" role="alert">
                    {error}
                </Alert>
            )}
            <div>
                <Button size="small" startIcon={<IconPlus size={16} />} disabled={disabled} onClick={add}>
                    Kademe ekle
                </Button>
            </div>
        </Stack>
    );
}
