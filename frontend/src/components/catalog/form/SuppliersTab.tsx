"use client";

import { useEffect, useState } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import Radio from "@mui/material/Radio";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import { erp, type CurrentAccountListItem } from "@/lib/api/erp";
import type { SupplierRow } from "./model";

type SuppliersTabProps = {
    suppliers: SupplierRow[];
    onChange: (suppliers: SupplierRow[]) => void;
};

/** Tedarikçiler sekmesi: cari (tedarikçi rolü) seçimi, tedarikçi kodu ve varsayılan işareti. */
export default function SuppliersTab({ suppliers, onChange }: SuppliersTabProps) {
    const [input, setInput] = useState("");
    const [options, setOptions] = useState<CurrentAccountListItem[]>([]);

    useEffect(() => {
        let cancelled = false;
        const handle = setTimeout(() => {
            erp.listAccounts({ search: input.trim() || undefined, role: "supplier", pageSize: 20 }).then(
                (r) => {
                    if (!cancelled) setOptions(r.items);
                },
                () => undefined,
            );
        }, 250);
        return () => {
            cancelled = true;
            clearTimeout(handle);
        };
    }, [input]);

    const update = (index: number, patch: Partial<SupplierRow>) => onChange(suppliers.map((s, i) => (i === index ? { ...s, ...patch } : s)));
    const chosen = new Set(suppliers.map((s) => s.currentAccountId));

    return (
        <Stack spacing={2}>
            <Autocomplete
                sx={{ maxWidth: 420 }}
                options={options.filter((o) => !chosen.has(o.id))}
                getOptionLabel={(o) => `${o.code} - ${o.name}`}
                inputValue={input}
                onInputChange={(_, value, reason) => reason !== "reset" && setInput(value)}
                value={null}
                onChange={(_, account) => {
                    if (account) {
                        onChange([...suppliers, { currentAccountId: account.id, name: account.name, supplierCode: "", isDefault: suppliers.length === 0 }]);
                        setInput("");
                    }
                }}
                noOptionsText="Tedarikçi bulunamadı"
                renderInput={(params) => <TextField {...params} size="small" label="Tedarikçi ekle" />}
            />
            {suppliers.length === 0 && <Typography color="text.secondary">Henüz tedarikçi eklenmedi.</Typography>}
            {suppliers.map((row, i) => (
                <Box key={row.currentAccountId} sx={{ display: "flex", gap: 1.5, alignItems: "center", flexWrap: "wrap" }} role="group" aria-label={row.name}>
                    <Typography sx={{ minWidth: 200, fontWeight: 600 }}>{row.name}</Typography>
                    <TextField
                        size="small"
                        label="Tedarikçi kodu"
                        value={row.supplierCode}
                        onChange={(e) => update(i, { supplierCode: e.target.value })}
                        sx={{ width: 200 }}
                    />
                    <FormControlLabel
                        control={
                            <Radio
                                checked={row.isDefault}
                                onChange={() => onChange(suppliers.map((s, j) => ({ ...s, isDefault: j === i })))}
                                slotProps={{ input: { "aria-label": `${row.name} varsayılan` } }}
                            />
                        }
                        label="Varsayılan"
                    />
                    <IconButton aria-label={`${row.name} kaldır`} onClick={() => onChange(suppliers.filter((_, j) => j !== i))}>
                        <IconTrash size={18} />
                    </IconButton>
                </Box>
            ))}
        </Stack>
    );
}
