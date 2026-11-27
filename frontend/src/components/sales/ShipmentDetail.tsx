"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import ReasonDialog from "@/components/purchasing/ReasonDialog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { SHIPMENT_STATUS_LABELS, sales, type ShipmentDto } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";
import { SHIPMENT_BASE, SHIPMENT_STATUS_COLOR } from "./Shipments";

const qty = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });

/** Sevk detayı: satırlar, teslim edildi, iptal ve (faturalanmamışsa) fatura taslağı. */
export default function ShipmentDetail({ id }: { id: string }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read");
    const [shipment, setShipment] = useState<ShipmentDto | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [dialog, setDialog] = useState<"deliver" | "cancel" | null>(null);
    const [receivedBy, setReceivedBy] = useState("");
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            setShipment(await sales.shipments.get(id));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    }, [id]);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [allowed, load]);

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

    if (!allowed) return <Alert severity="warning">Sevkleri görüntülemek için yetkiniz yok.</Alert>;
    if (!shipment) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={200} />;

    const canShip = hasPermission("satis.ship");
    const canInvoice = hasPermission("satis.manage") && hasPermission("fatura.manage");

    return (
        <>
            <PageHeader title={`Sevk ${shipment.number}`} subtitle={`${shipment.customerName} · ${formatDate(shipment.date)}`} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                    <Chip color={SHIPMENT_STATUS_COLOR[shipment.status]} label={SHIPMENT_STATUS_LABELS[shipment.status]} />
                    <Button component={Link} size="small" href={`/satis/siparisler/${shipment.salesOrderId}`}>
                        Sipariş {shipment.orderNumber}
                    </Button>
                    {shipment.invoiceId && (
                        <Button component={Link} size="small" href={`/satis/faturalar/${shipment.invoiceId}`}>
                            Faturayı aç
                        </Button>
                    )}
                    {shipment.cargoNote && <Chip variant="outlined" label={`Not: ${shipment.cargoNote}`} />}
                    {shipment.receivedBy && <Chip variant="outlined" label={`Teslim alan: ${shipment.receivedBy}`} />}
                    {shipment.cancelReason && <Chip variant="outlined" color="error" label={`İptal gerekçesi: ${shipment.cancelReason}`} />}
                </Stack>
                <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                    {canShip && shipment.status === "shipped" && (
                        <Button variant="contained" disabled={busy} onClick={() => setDialog("deliver")}>
                            Teslim edildi
                        </Button>
                    )}
                    {canInvoice && shipment.status !== "cancelled" && !shipment.invoiceId && (
                        <Button
                            variant="outlined"
                            disabled={busy}
                            onClick={() =>
                                void run(async () => {
                                    const invoice = await sales.shipments.invoiceDraft([shipment.id]);
                                    router.push(`/satis/faturalar/${invoice.id}`);
                                })
                            }
                        >
                            Fatura taslağı oluştur
                        </Button>
                    )}
                    {canShip && shipment.status === "shipped" && (
                        <Button color="error" disabled={busy} onClick={() => setDialog("cancel")}>
                            Sevki iptal et
                        </Button>
                    )}
                    <Button component={Link} href={SHIPMENT_BASE}>
                        Sevk listesi
                    </Button>
                </Stack>
                <DashboardCard title="Satırlar">
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Kalem</TableCell>
                                <TableCell align="right">Sevk</TableCell>
                                <TableCell align="right">Faturalanan</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {shipment.lines.map((l) => (
                                <TableRow key={l.id}>
                                    <TableCell>
                                        <Typography variant="body2">{l.description}</Typography>
                                        {(l.width !== null || l.length !== null) && (
                                            <Typography variant="caption" color="text.secondary">
                                                Ebat: {[l.width, l.length].filter((n) => n !== null).map((n) => (n as number).toFixed(2).replace(".", ",")).join(" × ")}
                                            </Typography>
                                        )}
                                    </TableCell>
                                    <TableCell align="right">
                                        {qty(l.quantity)} {l.unit}
                                    </TableCell>
                                    <TableCell align="right">{qty(l.invoicedQuantity)}</TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </DashboardCard>
            </Stack>

            <Dialog open={dialog === "deliver"} onClose={() => setDialog(null)} fullWidth maxWidth="xs">
                <DialogTitle>Teslim edildi</DialogTitle>
                <DialogContent>
                    <TextField sx={{ mt: 1 }} fullWidth size="small" label="Teslim alan" value={receivedBy} onChange={(e) => setReceivedBy(e.target.value)} />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDialog(null)}>Vazgeç</Button>
                    <Button
                        variant="contained"
                        disabled={busy}
                        onClick={() =>
                            void run(async () => {
                                setShipment(await sales.shipments.deliver(shipment.id, { receivedBy: receivedBy.trim() || null }));
                                setDialog(null);
                            })
                        }
                    >
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>
            <ReasonDialog
                open={dialog === "cancel"}
                title="Sevki iptal et"
                text="Stok çıkışı geri alınır ve rezervasyon yeniden aktif olur. Faturaya bağlı sevk iptal edilemez."
                confirmLabel="İptal et"
                onClose={() => setDialog(null)}
                onConfirm={async (reason) => {
                    setShipment(await sales.shipments.cancel(shipment.id, reason));
                    setDialog(null);
                }}
            />
        </>
    );
}
