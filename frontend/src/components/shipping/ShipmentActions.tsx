"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import FormAlert from "@/components/auth/FormAlert";
import ReasonDialog from "@/components/purchasing/ReasonDialog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { ShipmentDto } from "@/lib/api/sales";
import { shipping } from "@/lib/api/shipping";
import { useSession } from "@/session/SessionProvider";
import DeliveryDialog from "./DeliveryDialog";
import DispatchDialog from "./DispatchDialog";
import FailDialog from "./FailDialog";

type ShipmentActionsProps = {
    shipment: ShipmentDto;
    size?: "small" | "medium";
    onChange(updated: ShipmentDto): void;
};

type DialogKind = "dispatch" | "deliver" | "fail" | "return" | null;

/** Durum makinesine göre bir sevkin yapılabilir adımları: toplama, paketleme, sevk, teslim, başarısız, yeniden gönder, iade. */
export default function ShipmentActions({ shipment, size = "small", onChange }: ShipmentActionsProps) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const [dialog, setDialog] = useState<DialogKind>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const canPick = hasPermission("sevkiyat.pick");
    const canPack = hasPermission("sevkiyat.pack");
    const canShip = hasPermission("sevkiyat.ship");
    const canDeliver = hasPermission("sevkiyat.deliver");
    const target = [{ id: shipment.id, number: shipment.number, customerName: shipment.customerName }];

    async function run(action: () => Promise<void>) {
        setBusy(true);
        setError(null);
        try {
            await action();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const status = shipment.status;
    return (
        <Stack spacing={1}>
            {error && <FormAlert error={error} />}
            <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                {status === "draft" && canPick && (
                    <Button
                        size={size}
                        variant="outlined"
                        disabled={busy}
                        onClick={() =>
                            void run(async () => {
                                const list = await shipping.shipments.pickList(shipment.id);
                                router.push(`/sevkiyat/toplama/${list.id}`);
                            })
                        }
                    >
                        Toplamaya başla
                    </Button>
                )}
                {status === "draft" && canShip && (
                    <Button size={size} variant="text" disabled={busy} onClick={() => setDialog("dispatch")}>
                        Doğrudan sevk et
                    </Button>
                )}
                {status === "picking" && shipment.pickListId && (
                    <Button size={size} component={Link} href={`/sevkiyat/toplama/${shipment.pickListId}`} variant="outlined">
                        Toplama
                    </Button>
                )}
                {(status === "picking" || status === "packed") && canPack && (
                    <Button size={size} component={Link} href={`/sevkiyat/paketleme/${shipment.id}`} variant="outlined">
                        Paketleme
                    </Button>
                )}
                {status === "packed" && canShip && (
                    <Button size={size} variant="contained" disabled={busy} onClick={() => setDialog("dispatch")}>
                        Sevk et
                    </Button>
                )}
                {status === "shipped" && canDeliver && (
                    <>
                        <Button size={size} variant="contained" disabled={busy} onClick={() => setDialog("deliver")}>
                            Teslim edildi
                        </Button>
                        <Button size={size} color="error" disabled={busy} onClick={() => setDialog("fail")}>
                            Teslim edilemedi
                        </Button>
                    </>
                )}
                {status === "deliveryFailed" && canShip && (
                    <>
                        <Button
                            size={size}
                            variant="contained"
                            disabled={busy}
                            onClick={() =>
                                void run(async () => {
                                    onChange(await shipping.shipments.resend(shipment.id));
                                })
                            }
                        >
                            Yeniden gönder
                        </Button>
                        <Button size={size} color="error" disabled={busy} onClick={() => setDialog("return")}>
                            İade al
                        </Button>
                    </>
                )}
            </Stack>

            <DispatchDialog open={dialog === "dispatch"} shipments={target} onClose={() => setDialog(null)} onDone={(shipped) => shipped[0] && onChange(shipped[0])} />
            <DeliveryDialog
                open={dialog === "deliver"}
                onClose={() => setDialog(null)}
                onSubmit={async (body) => {
                    onChange(await shipping.shipments.deliver(shipment.id, body));
                }}
            />
            <FailDialog
                open={dialog === "fail"}
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    onChange(await shipping.shipments.fail(shipment.id, reason));
                    setDialog(null);
                }}
            />
            <ReasonDialog
                open={dialog === "return"}
                title="Sevki iade al"
                text="Faturasız sevkte stok çıkışı geri alınır ve sipariş satırı yeniden açılır; faturalıysa iade belgesi (ve iade taslak faturası) oluşturulur."
                confirmLabel="İade al"
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    onChange(await shipping.shipments.returnShipment(shipment.id, reason));
                    setDialog(null);
                }}
            />
        </Stack>
    );
}
