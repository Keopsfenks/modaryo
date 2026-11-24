"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import Accordion from "@mui/material/Accordion";
import AccordionDetails from "@mui/material/AccordionDetails";
import AccordionSummary from "@mui/material/AccordionSummary";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Tab from "@mui/material/Tab";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Tabs from "@mui/material/Tabs";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconChevronDown } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import {
    formatQuantity,
    inventory,
    type StockDocument,
    type StockDocumentKind,
    type StockDocumentRow,
    type Transfer,
    type TransferRow,
    type WarehouseDto,
} from "@/lib/api/inventory";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import OpeningImport from "./OpeningImport";

const KIND_LABELS: Record<StockDocumentKind, string> = { receipt: "Giriş", issue: "Çıkış", opening: "Açılış" };
const formatDate = (iso: string): string => iso.split("-").reverse().join(".");

/** Stok fişleri ve transferler: liste, ayrıntı penceresi, iptal ve Excel'den açılış stoğu. ?belge= / ?transfer= ayrıntıyı açar. */
export default function StockDocuments() {
    const { hasPermission } = useSession();
    const params = useSearchParams();
    const canRead = hasPermission("stok.read");
    const canManage = hasPermission("stok.manage");
    const showCost = hasPermission("maliyet.read");

    const [tab, setTab] = useState(params.get("transfer") ? "transfer" : "fis");
    const [kind, setKind] = useState("");
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [docs, setDocs] = useState<StockDocumentRow[] | null>(null);
    const [transfers, setTransfers] = useState<TransferRow[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [openDoc, setOpenDoc] = useState<StockDocument | null>(null);
    const [openTransfer, setOpenTransfer] = useState<Transfer | null>(null);
    const [cancelReason, setCancelReason] = useState("");
    const [cancelling, setCancelling] = useState(false);

    const belge = params.get("belge");
    const transfer = params.get("transfer");

    const load = useCallback(async () => {
        try {
            const [d, t] = await Promise.all([inventory.documents({ kind: (kind || undefined) as StockDocumentKind | undefined, pageSize: 100 }), inventory.transfers(1, 100)]);
            setDocs(d.items);
            setTransfers(t.items);
        } catch (e) {
            setError(errorMessage(e));
            setDocs((c) => c ?? []);
            setTransfers((c) => c ?? []);
        }
    }, [kind]);

    useEffect(() => {
        if (!canRead) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [canRead, load]);

    useEffect(() => {
        if (canRead) inventory.warehouses().then(setWarehouses, () => undefined);
    }, [canRead]);

    useEffect(() => {
        if (!canRead) return;
        if (belge) inventory.document(belge).then(setOpenDoc, (e) => setError(errorMessage(e)));
        if (transfer) inventory.transfer(transfer).then(setOpenTransfer, (e) => setError(errorMessage(e)));
    }, [canRead, belge, transfer]);

    if (!canRead) return <Alert severity="warning">Stok fişlerini görmek için yetkiniz yok.</Alert>;

    const wname = (id: string) => warehouses.find((w) => w.id === id)?.name ?? "—";

    async function show(id: string) {
        setError(null);
        try {
            setOpenDoc(await inventory.document(id));
        } catch (e) {
            setError(errorMessage(e));
        }
    }

    async function showTransfer(id: string) {
        setError(null);
        try {
            setOpenTransfer(await inventory.transfer(id));
        } catch (e) {
            setError(errorMessage(e));
        }
    }

    async function cancelDoc() {
        if (!openDoc || !cancelReason.trim()) return;
        setCancelling(true);
        try {
            await inventory.cancelDocument(openDoc.id, cancelReason.trim());
            setOpenDoc(null);
            setCancelReason("");
            await load();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setCancelling(false);
        }
    }

    return (
        <>
            <PageHeader title="Stok fişleri" subtitle="Giriş, çıkış ve açılış fişleri ile depo transferleri." />
            <DashboardCard
                action={
                    canManage && (
                        <Stack direction="row" spacing={1}>
                            <Button component={Link} href="/stok/transferler/yeni" variant="outlined">
                                Yeni transfer
                            </Button>
                            <Button component={Link} href="/stok/fisler/yeni" variant="contained">
                                Yeni fiş
                            </Button>
                        </Stack>
                    )
                }
            >
                <FormAlert error={error} />
                <Tabs value={tab} onChange={(_, v: string) => setTab(v)} sx={{ mb: 2 }} aria-label="Fiş türleri">
                    <Tab value="fis" label="Fişler" />
                    <Tab value="transfer" label="Transferler" />
                </Tabs>

                {tab === "fis" && (
                    <>
                        <TextField select size="small" label="Tür" value={kind} onChange={(e) => setKind(e.target.value)} sx={{ minWidth: 160, mb: 2 }}>
                            <MenuItem value="">Tümü</MenuItem>
                            {(Object.keys(KIND_LABELS) as StockDocumentKind[]).map((k) => (
                                <MenuItem key={k} value={k}>
                                    {KIND_LABELS[k]}
                                </MenuItem>
                            ))}
                        </TextField>
                        {docs === null ? (
                            <Skeleton variant="rounded" height={120} />
                        ) : docs.length === 0 ? (
                            <Typography color="text.secondary">Henüz fiş yok.</Typography>
                        ) : (
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tür</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Depo</TableCell>
                                        <TableCell>Satır</TableCell>
                                        <TableCell>Durum</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {docs.map((d) => (
                                        <TableRow key={d.id} hover>
                                            <TableCell>
                                                <Button size="small" onClick={() => void show(d.id)}>
                                                    {d.number}
                                                </Button>
                                            </TableCell>
                                            <TableCell>{KIND_LABELS[d.kind]}</TableCell>
                                            <TableCell>{formatDate(d.date)}</TableCell>
                                            <TableCell>{wname(d.warehouseId)}</TableCell>
                                            <TableCell>{d.lineCount}</TableCell>
                                            <TableCell>{d.status === "cancelled" ? <Chip size="small" label="İptal" /> : "Geçerli"}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        )}
                    </>
                )}

                {tab === "transfer" &&
                    (transfers === null ? (
                        <Skeleton variant="rounded" height={120} />
                    ) : transfers.length === 0 ? (
                        <Typography color="text.secondary">Henüz transfer yok.</Typography>
                    ) : (
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Numara</TableCell>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Kaynak</TableCell>
                                    <TableCell>Hedef</TableCell>
                                    <TableCell>Satır</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {transfers.map((t) => (
                                    <TableRow key={t.id} hover>
                                        <TableCell>
                                            <Button size="small" onClick={() => void showTransfer(t.id)}>
                                                {t.number}
                                            </Button>
                                        </TableCell>
                                        <TableCell>{formatDate(t.date)}</TableCell>
                                        <TableCell>{wname(t.fromWarehouseId)}</TableCell>
                                        <TableCell>{wname(t.toWarehouseId)}</TableCell>
                                        <TableCell>{t.lineCount}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    ))}
            </DashboardCard>

            {canManage && (
                <Accordion sx={{ mt: 3 }} disableGutters>
                    <AccordionSummary expandIcon={<IconChevronDown size={18} />}>Açılış stoklarını Excel&apos;den yükle</AccordionSummary>
                    <AccordionDetails>
                        <OpeningImport onDone={() => void load()} />
                    </AccordionDetails>
                </Accordion>
            )}

            <Dialog open={openDoc !== null} onClose={() => setOpenDoc(null)} fullWidth maxWidth="md">
                <DialogTitle>
                    {openDoc?.number} · {openDoc && KIND_LABELS[openDoc.kind]} fişi {openDoc?.status === "cancelled" && "(iptal)"}
                </DialogTitle>
                <DialogContent>
                    {openDoc && (
                        <Stack spacing={2}>
                            <Typography variant="body2">
                                {formatDate(openDoc.date)} · {wname(openDoc.warehouseId)} {openDoc.reason ? `· ${openDoc.reason}` : ""}
                            </Typography>
                            {openDoc.cancelReason && <Alert severity="info">İptal nedeni: {openDoc.cancelReason}</Alert>}
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Miktar</TableCell>
                                        {showCost && <TableCell align="right">Birim maliyet</TableCell>}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {openDoc.lines.map((l) => (
                                        <TableRow key={l.id}>
                                            <TableCell>
                                                {l.productCode} · {l.productName}
                                            </TableCell>
                                            <TableCell align="right">
                                                {formatQuantity(l.quantity)} {l.unit}
                                            </TableCell>
                                            {showCost && <TableCell align="right">{l.unitCost === null ? "—" : formatMoney(l.unitCost, "TRY")}</TableCell>}
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                            {canManage && openDoc.status === "active" && (
                                <TextField size="small" label="İptal nedeni" value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} helperText="İptal, hareketlerin tersini bugünün tarihiyle yazar; hiçbir şey silinmez." />
                            )}
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpenDoc(null)}>Kapat</Button>
                    {canManage && openDoc?.status === "active" && (
                        <Button color="error" variant="contained" disabled={!cancelReason.trim() || cancelling} onClick={() => void cancelDoc()}>
                            Fişi iptal et
                        </Button>
                    )}
                </DialogActions>
            </Dialog>

            <Dialog open={openTransfer !== null} onClose={() => setOpenTransfer(null)} fullWidth maxWidth="md">
                <DialogTitle>{openTransfer?.number} · Transfer</DialogTitle>
                <DialogContent>
                    {openTransfer && (
                        <Stack spacing={2}>
                            <Typography variant="body2">
                                {formatDate(openTransfer.date)} · {wname(openTransfer.fromWarehouseId)} → {wname(openTransfer.toWarehouseId)} {openTransfer.note ? `· ${openTransfer.note}` : ""}
                            </Typography>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Miktar</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {openTransfer.lines.map((l) => (
                                        <TableRow key={l.id}>
                                            <TableCell>
                                                {l.productCode} · {l.productName}
                                            </TableCell>
                                            <TableCell align="right">
                                                {formatQuantity(l.quantity)} {l.unit}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpenTransfer(null)}>Kapat</Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
