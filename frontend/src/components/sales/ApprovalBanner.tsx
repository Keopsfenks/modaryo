"use client";

import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import type { OrderDto } from "@/lib/api/sales";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

type ApprovalBannerProps = {
    order: Pick<OrderDto, "status" | "currency" | "prepaymentRequired" | "advanceTotal" | "warnings">;
    busy?: boolean;
    onApproveLimit(): void;
    onCollectAdvance(): void;
};

/** Onay bekleyen siparişin nedeni: kredi limiti aşımı, ön ödeme ya da stok uyarısı. */
export default function ApprovalBanner({ order, busy, onApproveLimit, onCollectAdvance }: ApprovalBannerProps) {
    const { hasPermission } = useSession();
    const money = (n: number) => formatMoney(n, order.currency as Currency);
    const stockWarning = order.warnings.includes("insufficient_stock");

    return (
        <Stack spacing={1}>
            {order.status === "awaitingLimitApproval" && (
                <Alert
                    severity="warning"
                    action={
                        hasPermission("satis.limit-asimi") ? (
                            <Button color="inherit" size="small" disabled={busy} onClick={onApproveLimit}>
                                Limit aşımını onayla
                            </Button>
                        ) : undefined
                    }
                >
                    <Typography variant="body2">Cari kredi limiti aşılıyor; sipariş limit onayı bekliyor.</Typography>
                    {!hasPermission("satis.limit-asimi") && <Typography variant="caption">Limit aşımını onaylamak için yetkili bir kullanıcı gerekir.</Typography>}
                </Alert>
            )}
            {order.status === "awaitingPayment" && (
                <Alert
                    severity="info"
                    action={
                        hasPermission("tahsilat.manage") ? (
                            <Button color="inherit" size="small" onClick={onCollectAdvance}>
                                Peşinat al
                            </Button>
                        ) : undefined
                    }
                >
                    Ön ödeme bekleniyor: tahsil edilen {money(order.advanceTotal)} / gereken {money(order.prepaymentRequired)}. Tutar tamamlanınca sipariş onaylanır.
                </Alert>
            )}
            {stockWarning && <Alert severity="warning">Stok yetersiz; eksi stok açık olduğu için sipariş onaylandı.</Alert>}
        </Stack>
    );
}
