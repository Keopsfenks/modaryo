"use client";

import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import { validateUnits, type UnitRow } from "./model";

type UnitsTabProps = {
    units: UnitRow[];
    baseUnit: string;
    unitOptions: string[];
    onChange: (units: UnitRow[]) => void;
};

/** Birimler sekmesi: "1 {birim} = {katsayı} {anaBirim}" satırları ve birim barkodu. */
export default function UnitsTab({ units, baseUnit, unitOptions, onChange }: UnitsTabProps) {
    const errors = validateUnits(units, baseUnit);
    const update = (index: number, patch: Partial<UnitRow>) => onChange(units.map((u, i) => (i === index ? { ...u, ...patch } : u)));
    const choices = unitOptions.filter((u) => u !== baseUnit);

    return (
        <Stack spacing={2}>
            <Typography color="text.secondary">
                Temel birim: <strong>{baseUnit}</strong>. Koli, paket gibi alternatif birimleri temel birime çevrim katsayısıyla tanımlayın.
            </Typography>
            {units.map((row, i) => (
                <Box key={i} sx={{ display: "flex", gap: 1.5, alignItems: "flex-start", flexWrap: "wrap" }} role="group" aria-label={`Birim ${i + 1}`}>
                    <TextField
                        select
                        size="small"
                        label="Birim"
                        value={row.unit}
                        onChange={(e) => update(i, { unit: e.target.value })}
                        sx={{ width: 140 }}
                    >
                        {choices.map((u) => (
                            <MenuItem key={u} value={u}>
                                {u}
                            </MenuItem>
                        ))}
                    </TextField>
                    <Typography sx={{ pt: 1 }}>1 {row.unit || "birim"} =</Typography>
                    <TextField
                        size="small"
                        label="Katsayı"
                        value={row.factor}
                        onChange={(e) => update(i, { factor: e.target.value })}
                        error={Boolean(errors[i]) && row.unit !== ""}
                        helperText={row.unit ? errors[i] : undefined}
                        slotProps={{ htmlInput: { inputMode: "decimal" } }}
                        sx={{ width: 150 }}
                    />
                    <Typography sx={{ pt: 1 }}>{baseUnit}</Typography>
                    <TextField size="small" label="Barkod" value={row.barcode} onChange={(e) => update(i, { barcode: e.target.value })} sx={{ width: 200 }} />
                    <IconButton aria-label={`Birim ${i + 1} sil`} onClick={() => onChange(units.filter((_, j) => j !== i))}>
                        <IconTrash size={18} />
                    </IconButton>
                </Box>
            ))}
            <div>
                <Button variant="outlined" onClick={() => onChange([...units, { unit: "", factor: "", barcode: "" }])} disabled={choices.length === 0}>
                    Birim ekle
                </Button>
            </div>
        </Stack>
    );
}
