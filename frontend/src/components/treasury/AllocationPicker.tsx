"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import Checkbox from "@mui/material/Checkbox";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { Currency } from "@/lib/api/erp";
import type { AllocationInput, OpenItemForAllocation } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { formatMoney, parseAmount } from "@/lib/money";

type Row = { checked: boolean; text: string };

export type AllocationChange = {
    allocations: AllocationInput[];
    total: number;
    /** Satır hatası ya da toplam aşımı varsa false. */
    valid: boolean;
};

type AllocationPickerProps = {
    /** Eşleştirilebilecek açık vade satırları (karşı yöndekiler). */
    items: OpenItemForAllocation[];
    /** Dağıtılabilecek tutar (cari para biriminde); null ise otomatik doldurma yapılmaz ve toplam sınırı denetlenmez. */
    budget: number | null;
    currency: Currency;
    onChange(change: AllocationChange): void;
};

const round2 = (n: number) => Math.round(n * 100) / 100;
const toText = (n: number) => String(n).replace(".", ",");

/** Kalemleri FIFO (liste sırası) ile bütçe bitene dek doldurur. */
function fifo(items: OpenItemForAllocation[], budget: number | null): Record<string, Row> {
    const rows: Record<string, Row> = {};
    let left = budget ?? 0;
    for (const item of items) {
        const take = budget === null ? 0 : round2(Math.min(left, item.open));
        rows[item.dueLineId] = take > 0 ? { checked: true, text: toText(take) } : { checked: false, text: "" };
        left = round2(left - take);
    }
    return rows;
}

/** Açık vade satırları: onay kutusu ve tutar. İlk açılışta FIFO ile doldurulur; kullanıcı düzenleyebilir. */
export default function AllocationPicker({ items, budget, currency, onChange }: AllocationPickerProps) {
    const [rows, setRows] = useState<Record<string, Row>>(() => fifo(items, budget));
    const touched = useRef(false);
    const itemKey = items.map((i) => i.dueLineId).join(",");

    useEffect(() => {
        if (touched.current) return;
        setRows(fifo(items, budget));
        // eslint-disable-next-line react-hooks/exhaustive-deps -- itemKey kalemlerin kimliğidir
    }, [itemKey, budget]);

    const view = useMemo(() => {
        const errors: Record<string, string> = {};
        const allocations: AllocationInput[] = [];
        let total = 0;
        for (const item of items) {
            const row = rows[item.dueLineId];
            if (!row?.checked) continue;
            const amount = parseAmount(row.text);
            if (amount === null || amount <= 0) errors[item.dueLineId] = "Geçerli bir tutar girin.";
            else if (amount > item.open) errors[item.dueLineId] = "Açık tutarı aşıyor.";
            else {
                allocations.push({ dueLineId: item.dueLineId, amount });
                total = round2(total + amount);
            }
        }
        const overBudget = budget !== null && total > budget;
        return { errors, allocations, total, overBudget };
    }, [rows, items, budget]);

    const onChangeRef = useRef(onChange);
    useEffect(() => {
        onChangeRef.current = onChange;
    });
    useEffect(() => {
        onChangeRef.current({ allocations: view.allocations, total: view.total, valid: Object.keys(view.errors).length === 0 && !view.overBudget });
    }, [view]);

    function update(id: string, patch: Partial<Row>) {
        touched.current = true;
        setRows((current) => ({ ...current, [id]: { ...(current[id] ?? { checked: false, text: "" }), ...patch } }));
    }

    if (items.length === 0) return <Typography color="text.secondary">Eşleştirilebilecek açık kalem yok.</Typography>;

    return (
        <>
            <TableContainer>
                <Table size="small">
                    <TableHead>
                        <TableRow>
                            <TableCell padding="checkbox" />
                            <TableCell>Belge</TableCell>
                            <TableCell>Vade</TableCell>
                            <TableCell align="right">Açık tutar</TableCell>
                            <TableCell align="right">Eşleştirilecek</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {items.map((item) => {
                            const row = rows[item.dueLineId] ?? { checked: false, text: "" };
                            const name = item.documentNo ?? item.description ?? "Kalem";
                            const label = `${name} taksit ${item.sequence}`;
                            return (
                                <TableRow key={item.dueLineId}>
                                    <TableCell padding="checkbox">
                                        <Checkbox
                                            checked={row.checked}
                                            slotProps={{ input: { "aria-label": `${label} seç` } }}
                                            onChange={(e) => update(item.dueLineId, { checked: e.target.checked, text: e.target.checked && !row.text ? toText(item.open) : row.text })}
                                        />
                                    </TableCell>
                                    <TableCell>{name}</TableCell>
                                    <TableCell>{formatDate(item.dueDate)}</TableCell>
                                    <TableCell align="right">{formatMoney(item.open, currency)}</TableCell>
                                    <TableCell align="right">
                                        <TextField
                                            size="small"
                                            value={row.text}
                                            disabled={!row.checked}
                                            error={row.checked && !!view.errors[item.dueLineId]}
                                            helperText={row.checked ? view.errors[item.dueLineId] : undefined}
                                            onChange={(e) => update(item.dueLineId, { text: e.target.value })}
                                            slotProps={{ htmlInput: { "aria-label": `${label} tutar`, inputMode: "decimal", style: { textAlign: "right" } } }}
                                            sx={{ width: 150 }}
                                        />
                                    </TableCell>
                                </TableRow>
                            );
                        })}
                    </TableBody>
                </Table>
            </TableContainer>
            <Typography variant="body2" color={view.overBudget ? "error" : "text.secondary"} sx={{ mt: 1 }}>
                Eşleştirilen: {formatMoney(view.total, currency)}
                {budget !== null && ` / ${formatMoney(budget, currency)}`}
                {view.overBudget && " — tutarı aşıyor."}
            </Typography>
        </>
    );
}
