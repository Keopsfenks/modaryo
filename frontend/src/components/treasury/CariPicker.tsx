"use client";

import { useEffect, useState } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import TextField from "@mui/material/TextField";
import { useDebounced } from "@/components/pricing/useDebounced";
import { erp, type AccountRole, type CurrentAccountListItem } from "@/lib/api/erp";

type CariPickerProps = {
    label?: string;
    value: CurrentAccountListItem | null;
    onChange(item: CurrentAccountListItem | null): void;
    role?: AccountRole;
    disabled?: boolean;
};

/** Cari arama kutusu: yazdıkça sunucuda kod/ad aranır. */
export default function CariPicker({ label = "Cari", value, onChange, role, disabled }: CariPickerProps) {
    const [input, setInput] = useState("");
    const search = useDebounced(input, 250);
    const [options, setOptions] = useState<CurrentAccountListItem[]>([]);

    useEffect(() => {
        let cancelled = false;
        erp.listAccounts({ search: search || undefined, role, page: 1, pageSize: 20 }).then(
            (r) => {
                if (!cancelled) setOptions(r.items);
            },
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [search, role]);

    return (
        <Autocomplete
            size="small"
            sx={{ minWidth: 280, flexGrow: 1 }}
            disabled={disabled}
            options={options}
            filterOptions={(x) => x}
            getOptionLabel={(o) => `${o.code} · ${o.name}`}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            value={value}
            inputValue={input}
            onInputChange={(_, text, reason) => {
                if (reason !== "reset") setInput(text);
            }}
            onChange={(_, item) => onChange(item)}
            noOptionsText="Cari bulunamadı."
            renderInput={(params) => <TextField {...params} label={label} />}
        />
    );
}
