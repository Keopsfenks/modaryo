"use client";

import { useEffect, useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import Typography from "@mui/material/Typography";
import { parseAmount } from "@/lib/money";
import { dueScheduleError, splitDue, type DueRow } from "./invoiceModel";

type DueScheduleEditorProps = {
    /** Cari para biriminde ödenecek tutar (sunucu önizlemesinin ledgerAmount değeri). */
    total: number;
    /** null: tek vade (vade günü). */
    rows: DueRow[] | null;
    onChange(rows: DueRow[] | null): void;
    /** Tek vadede gösterilen tarih. */
    singleDueDate: string;
    /** Taksit başlangıcı için varsayılan ilk vade. */
    defaultFirstDate: string;
    disabled?: boolean;
};

/** "Tek vade" ya da "Taksitli": adet ve ilk vade seçilir, tutar eşit bölünür, kuruş farkı son satıra gider; satırlar düzenlenebilir ve toplam canlı doğrulanır. */
export default function DueScheduleEditor({ total, rows, onChange, singleDueDate, defaultFirstDate, disabled }: DueScheduleEditorProps) {
    const [count, setCount] = useState("3");
    const [first, setFirst] = useState(defaultFirstDate);
    const edited = useRef(rows !== null);
    const instalment = rows !== null;

    const countNumber = Math.min(24, Math.max(2, Math.floor(parseAmount(count) ?? 2)));

    // Tutar ya da plan parametreleri değişince, elle düzenlenmemiş plan yeniden bölünür.
    useEffect(() => {
        if (instalment && !edited.current && first) onChange(splitDue(total, countNumber, first));
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [total, countNumber, first]);

    const error = rows ? dueScheduleError(rows, total) : null;

    return (
        <Stack spacing={2}>
            <ToggleButtonGroup
                exclusive
                size="small"
                value={instalment ? "instalment" : "single"}
                disabled={disabled}
                onChange={(_, v: string | null) => {
                    if (v === "single") {
                        edited.current = false;
                        onChange(null);
                    } else if (v === "instalment") {
                        edited.current = false;
                        onChange(splitDue(total, countNumber, first || defaultFirstDate));
                    }
                }}
                aria-label="Vade türü"
            >
                <ToggleButton value="single">Tek vade</ToggleButton>
                <ToggleButton value="instalment">Taksitli</ToggleButton>
            </ToggleButtonGroup>

            {!instalment && <Typography variant="body2">Vade tarihi: {singleDueDate || "-"}</Typography>}

            {rows && (
                <>
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                        <TextField
                            size="small"
                            label="Taksit sayısı"
                            value={count}
                            disabled={disabled}
                            onChange={(e) => {
                                edited.current = false;
                                setCount(e.target.value);
                            }}
                            slotProps={{ htmlInput: { inputMode: "numeric" } }}
                            sx={{ width: 140 }}
                        />
                        <TextField
                            size="small"
                            type="date"
                            label="İlk vade"
                            value={first}
                            disabled={disabled}
                            onChange={(e) => {
                                edited.current = false;
                                setFirst(e.target.value);
                            }}
                            slotProps={{ inputLabel: { shrink: true } }}
                        />
                    </Stack>
                    {rows.map((r, i) => (
                        <Stack key={r.key} direction="row" spacing={2}>
                            <TextField
                                size="small"
                                type="date"
                                value={r.dueDate}
                                disabled={disabled}
                                onChange={(e) => {
                                    edited.current = true;
                                    onChange(rows.map((x) => (x.key === r.key ? { ...x, dueDate: e.target.value } : x)));
                                }}
                                slotProps={{ htmlInput: { "aria-label": `Vade ${i + 1} tarih` } }}
                            />
                            <TextField
                                size="small"
                                value={r.amount}
                                disabled={disabled}
                                onChange={(e) => {
                                    edited.current = true;
                                    onChange(rows.map((x) => (x.key === r.key ? { ...x, amount: e.target.value } : x)));
                                }}
                                slotProps={{ htmlInput: { "aria-label": `Vade ${i + 1} tutar`, inputMode: "decimal" } }}
                            />
                        </Stack>
                    ))}
                    {error && <Alert severity="error">{error}</Alert>}
                </>
            )}
        </Stack>
    );
}
