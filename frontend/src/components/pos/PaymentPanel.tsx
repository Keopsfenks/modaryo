"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { SalePaymentInput } from "@/lib/api/pos";
import { money, parseNumber, round2 } from "./format";

export const CASH_FIELD_ID = "pos-cash";
const QUICK_CASH = [50, 100, 200, 500, 1000];
const TOUCH = { minHeight: 48 } as const;

type PaymentPanelProps = {
    total: number;
    customer: { id: string; name: string } | null;
    busy: boolean;
    /** Bağlantı yoksa satış engellenir. */
    offline: boolean;
    onSubmit: (payments: SalePaymentInput[]) => void;
    /** Değişim farkında veresiye verilmez. */
    allowOnAccount?: boolean;
    submitLabel?: string;
    /** Sunucu tutarı henüz hesaplanıyor ya da alınamadı: tutar kesin değildir, ödeme kapalıdır. */
    pending?: boolean;
};

/** Ödeme paneli: hızlı nakit düğmeleri, kart alanı, devasa para üstü ve (müşteri seçiliyse) veresiye. Nakit fazlası para üstüdür; kart toplamı aşamaz. */
export default function PaymentPanel({ total, customer, busy, offline, onSubmit, allowOnAccount = true, submitLabel = "Öde", pending = false }: PaymentPanelProps) {
    const [cash, setCash] = useState("");
    const [card, setCard] = useState("");

    const cashV = parseNumber(cash) ?? 0;
    const cardV = parseNumber(card) ?? 0;
    const cardOver = cardV > total;
    const afterCard = round2(Math.max(0, total - cardV));
    const paid = round2(cashV + cardV);
    const change = cardOver ? 0 : round2(Math.max(0, cashV - afterCard));
    const missing = round2(total - paid);
    const canPay = !busy && !offline && !pending && total > 0 && !cardOver && missing <= 0;
    const onAccount = round2(total - Math.min(cashV, afterCard) - Math.min(cardV, total));
    const canVeresiye = allowOnAccount && !busy && !offline && !pending && !!customer && total > 0 && !cardOver && onAccount > 0;

    const payments = (extra?: SalePaymentInput): SalePaymentInput[] => {
        const list: SalePaymentInput[] = [];
        if (cashV > 0) list.push({ method: "cash", amount: round2(extra ? Math.min(cashV, afterCard) : cashV) });
        if (cardV > 0) list.push({ method: "card", amount: round2(cardV) });
        if (extra) list.push(extra);
        return list;
    };

    return (
        <Stack spacing={2}>
            <Box>
                <Typography variant="body2" color="text.secondary">
                    Ödenecek
                </Typography>
                <Typography variant="h3" aria-label="Ödenecek tutar">
                    {money(total)}
                </Typography>
                {pending && (
                    <Typography variant="body2" color="warning.main" role="status">
                        Tutar hesaplanıyor...
                    </Typography>
                )}
            </Box>
            <Stack direction="row" useFlexGap sx={{ flexWrap: "wrap", gap: 1 }}>
                <Button variant="outlined" sx={TOUCH} disabled={pending} onClick={() => setCash(String(afterCard).replace(".", ","))}>
                    Tam
                </Button>
                {QUICK_CASH.map((n) => (
                    <Button key={n} variant="outlined" sx={TOUCH} onClick={() => setCash(String(n))}>
                        {n}
                    </Button>
                ))}
            </Stack>
            <Stack direction="row" spacing={1}>
                <TextField
                    id={CASH_FIELD_ID}
                    label="Nakit"
                    value={cash}
                    onChange={(e) => setCash(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter" && canPay) onSubmit(payments());
                    }}
                    slotProps={{ htmlInput: { inputMode: "decimal", style: { fontSize: 22 } } }}
                    fullWidth
                />
                <TextField
                    label="Kart"
                    value={card}
                    onChange={(e) => setCard(e.target.value)}
                    error={cardOver}
                    helperText={cardOver ? "Kart tutarı toplamı aşamaz." : undefined}
                    slotProps={{ htmlInput: { inputMode: "decimal", style: { fontSize: 22 } } }}
                    fullWidth
                />
            </Stack>
            <Box sx={{ textAlign: "center", py: 1, borderRadius: 2, bgcolor: change > 0 ? "success.light" : "action.hover" }}>
                <Typography variant="body2" color={change > 0 ? "success.contrastText" : "text.secondary"}>
                    Para üstü
                </Typography>
                <Typography role="status" aria-label="Para üstü" sx={{ fontSize: 48, fontWeight: 700, lineHeight: 1.1 }} color={change > 0 ? "success.contrastText" : "text.primary"}>
                    {money(change)}
                </Typography>
            </Box>
            {missing > 0 && paid > 0 && !cardOver && (
                <Typography variant="body2" color="warning.main">
                    Eksik: {money(missing)}
                </Typography>
            )}
            <Button variant="contained" size="large" sx={{ minHeight: 56, fontSize: 20 }} disabled={!canPay} onClick={() => onSubmit(payments())}>
                {busy ? "İşleniyor..." : submitLabel}
            </Button>
            {allowOnAccount && (
                <Stack spacing={0.5}>
                    <Button variant="outlined" color="secondary" sx={TOUCH} disabled={!canVeresiye} onClick={() => onSubmit(payments({ method: "onAccount", amount: onAccount }))}>
                        Veresiye
                    </Button>
                    {!customer && (
                        <Alert severity="info" sx={{ py: 0 }}>
                            Veresiye için müşteri seçin (F4).
                        </Alert>
                    )}
                </Stack>
            )}
        </Stack>
    );
}
