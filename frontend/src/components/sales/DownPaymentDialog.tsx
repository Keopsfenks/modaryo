"use client";

import { useRouter } from "next/navigation";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import type { OrderDto } from "@/lib/api/sales";
import { formatMoney, type Currency } from "@/lib/money";

type DownPaymentDialogProps = {
    open: boolean;
    order: Pick<OrderDto, "id" | "number" | "currentAccountId" | "currency" | "plan" | "prepaymentRequired" | "advanceTotal">;
    onClose(): void;
};

/** Tutarı ve siparişi önceden doldurup P5 tahsilat formunu (avans) açar. */
export function downPaymentAmount(order: DownPaymentDialogProps["order"]): number {
    const fromPlan = order.plan?.downPaymentRemaining ?? 0;
    const fromPrepay = Math.max(0, order.prepaymentRequired - order.advanceTotal);
    return Math.max(fromPlan, fromPrepay);
}

export default function DownPaymentDialog({ open, order, onClose }: DownPaymentDialogProps) {
    const router = useRouter();
    const amount = downPaymentAmount(order);

    function go() {
        const params = new URLSearchParams({ cari: order.currentAccountId, avans: "1", siparis: order.id });
        if (amount > 0) params.set("tutar", amount.toFixed(2));
        router.push(`/finans/tahsilat/yeni?${params.toString()}`);
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>Peşinat al</DialogTitle>
            <DialogContent>
                <DialogContentText>
                    {order.number} numaralı sipariş için avans tahsilatı girilecek.
                    {amount > 0 ? ` Beklenen tutar: ${formatMoney(amount, order.currency as Currency)}.` : ""} Tahsilat ekranı sipariş ve tutar dolu olarak açılır.
                </DialogContentText>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" onClick={go}>
                    Tahsilat ekranına git
                </Button>
            </DialogActions>
        </Dialog>
    );
}
