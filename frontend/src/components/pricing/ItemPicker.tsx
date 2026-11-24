"use client";

import { useEffect, useState } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import TextField from "@mui/material/TextField";
import { catalog, type SellableItem } from "@/lib/api/catalog";
import { useDebounced } from "./useDebounced";

export const itemLabel = (item: SellableItem): string => `${item.code} · ${item.name}`;

type ItemPickerProps = {
    label?: string;
    onPick: (item: SellableItem) => void;
    /** Seçimden sonra alan boşalır (liste düzenleyicisi gibi art arda ekleme için). */
    clearOnPick?: boolean;
    value?: SellableItem | null;
    disabled?: boolean;
    /** Yalnızca varyantları (variantId dolu kalemleri) göster. */
    variantsOnly?: boolean;
    size?: "small" | "medium";
};

/** Katalog kalem araması (GET /catalog/items): kod, ad, barkod ya da tedarikçi kodu ile. */
export default function ItemPicker({ label = "Kalem ara", onPick, clearOnPick = false, value, disabled, variantsOnly, size = "small" }: ItemPickerProps) {
    const [input, setInput] = useState("");
    const search = useDebounced(input);
    const [options, setOptions] = useState<SellableItem[]>([]);

    useEffect(() => {
        if (search.length < 1) return;
        let cancelled = false;
        catalog.searchItems(search).then(
            (items) => !cancelled && setOptions(variantsOnly ? items.filter((i) => i.variantId) : items),
            () => !cancelled && setOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [search, variantsOnly]);

    return (
        <Autocomplete
            size={size}
            options={options}
            disabled={disabled}
            filterOptions={(x) => x}
            getOptionLabel={itemLabel}
            isOptionEqualToValue={(a, b) => a.productId === b.productId && a.variantId === b.variantId}
            value={clearOnPick ? null : (value ?? null)}
            inputValue={input}
            onInputChange={(_, text, reason) => {
                if (reason !== "reset" || !clearOnPick) setInput(text);
                if (reason === "reset" && clearOnPick) setInput("");
            }}
            onChange={(_, item) => {
                if (item) onPick(item);
            }}
            noOptionsText={input.trim() ? "Kalem bulunamadı." : "Aramak için yazın."}
            renderInput={(params) => <TextField {...params} label={label} />}
            sx={{ minWidth: 280 }}
        />
    );
}
