"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import Alert from "@mui/material/Alert";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import ItemPicker from "@/components/pricing/ItemPicker";
import type { SellableItem } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import {
    MOVEMENT_LABELS,
    SOURCE_LABELS,
    formatQuantity,
    inventory,
    sourceHref,
    type ItemCard as ItemCardDto,
    type WarehouseDto,
} from "@/lib/api/inventory";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const formatDate = (iso: string): string => iso.split("-").reverse().join(".");

type Selection = { productId: string; variantId: string | null };

/** Stok kartı: kalemin hareket dökümü ve yürüyen bakiye. Kaynak hücresi belgeye bağlanır. */
export default function ItemCard() {
    const { hasPermission } = useSession();
    const canRead = hasPermission("stok.read");
    const showCost = hasPermission("maliyet.read");
    const params = useSearchParams();
    const urun = params.get("urun");
    const varyant = params.get("varyant");

    const [picked, setPicked] = useState<Selection | null>(null);
    const selection: Selection | null = picked ?? (urun ? { productId: urun, variantId: varyant } : null);
    const [warehouseId, setWarehouseId] = useState(params.get("depo") ?? "");
    const [from, setFrom] = useState("");
    const [to, setTo] = useState("");
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [card, setCard] = useState<ItemCardDto | null>(null);
    const [error, setError] = useState<string | null>(null);

    const productId = selection?.productId;
    const variantId = selection?.variantId ?? null;

    useEffect(() => {
        if (canRead) inventory.warehouses().then(setWarehouses, () => undefined);
    }, [canRead]);

    useEffect(() => {
        if (!canRead || !productId) return;
        let cancelled = false;
        inventory.itemCard({ productId, variantId, warehouseId: warehouseId || undefined, from: from || undefined, to: to || undefined }).then(
            (result) => {
                if (cancelled) return;
                setError(null);
                setCard(result);
            },
            (e) => {
                if (cancelled) return;
                setError(errorMessage(e));
                setCard(null);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [canRead, productId, variantId, warehouseId, from, to]);

    if (!canRead) return <Alert severity="warning">Stok hareketlerini görmek için yetkiniz yok.</Alert>;

    const warehouseName = (id: string) => warehouses.find((w) => w.id === id)?.name ?? "—";
    const onPick = (item: SellableItem) => {
        setCard(null);
        setPicked({ productId: item.productId, variantId: item.variantId });
    };

    return (
        <>
            <PageHeader title="Stok hareketleri" subtitle="Kalem bazında stok kartı: giriş, çıkış ve yürüyen miktar." />
            <DashboardCard>
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 3, flexWrap: "wrap" }}>
                    <ItemPicker label="Kalem seçin" onPick={onPick} />
                    <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} sx={{ minWidth: 200 }}>
                        <MenuItem value="">Tüm depolar</MenuItem>
                        {warehouses.map((w) => (
                            <MenuItem key={w.id} value={w.id}>
                                {w.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField size="small" type="date" label="Başlangıç" value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    <TextField size="small" type="date" label="Bitiş" value={to} onChange={(e) => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                </Stack>
                <FormAlert error={error} />
                {!selection ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Hareketlerini görmek için bir kalem seçin ya da Stok durumu sayfasından kalemin adına tıklayın.
                    </Typography>
                ) : !card ? (
                    !error && <Skeleton variant="rounded" height={160} />
                ) : (
                    <>
                        <Typography variant="h6" sx={{ mb: 1 }}>
                            {card.productCode} · {card.productName}
                        </Typography>
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Tür</TableCell>
                                        <TableCell>Depo</TableCell>
                                        <TableCell>Kaynak</TableCell>
                                        <TableCell align="right">Giriş</TableCell>
                                        <TableCell align="right">Çıkış</TableCell>
                                        <TableCell align="right">Bakiye</TableCell>
                                        {showCost && <TableCell align="right">Birim maliyet</TableCell>}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    <TableRow>
                                        <TableCell colSpan={6}>Devir</TableCell>
                                        <TableCell align="right" data-testid="running">
                                            {formatQuantity(card.openingQty)}
                                        </TableCell>
                                        {showCost && <TableCell />}
                                    </TableRow>
                                    {card.movements.map((m) => {
                                        const href = sourceHref(m.sourceType, m.sourceId);
                                        const label = SOURCE_LABELS[m.sourceType] ?? m.sourceType;
                                        return (
                                            <TableRow key={m.id}>
                                                <TableCell>{formatDate(m.date)}</TableCell>
                                                <TableCell>{MOVEMENT_LABELS[m.type] ?? m.type}</TableCell>
                                                <TableCell>{warehouseName(m.warehouseId)}</TableCell>
                                                <TableCell>{href ? <Link href={href}>{label}</Link> : label}</TableCell>
                                                <TableCell align="right">{m.in ? formatQuantity(m.in) : ""}</TableCell>
                                                <TableCell align="right">{m.out ? formatQuantity(m.out) : ""}</TableCell>
                                                <TableCell align="right" data-testid="running">
                                                    {formatQuantity(m.running)}
                                                </TableCell>
                                                {showCost && <TableCell align="right">{m.unitCost === null ? "—" : formatMoney(m.unitCost, "TRY")}</TableCell>}
                                            </TableRow>
                                        );
                                    })}
                                    <TableRow>
                                        <TableCell colSpan={6}>
                                            <strong>Kapanış ({card.unit})</strong>
                                        </TableCell>
                                        <TableCell align="right">
                                            <strong>{formatQuantity(card.closingQty)}</strong>
                                        </TableCell>
                                        {showCost && <TableCell />}
                                    </TableRow>
                                </TableBody>
                            </Table>
                        </TableContainer>
                    </>
                )}
            </DashboardCard>
        </>
    );
}
