"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import type { ProductDto } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatQuantity, formatWithBreakdown, inventory, type BalanceRow } from "@/lib/api/inventory";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { itemCardHref } from "./StockStatus";

/** Ürün kartının "Stok" sekmesi: depo bazında bakiyeler (varyantlar ayrı satır) ve stok kartı bağlantısı. */
export default function ProductStockTab({ product }: { product: ProductDto }) {
    const { hasPermission } = useSession();
    const canRead = hasPermission("stok.read");
    const showCost = hasPermission("maliyet.read");
    const [rows, setRows] = useState<BalanceRow[] | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!canRead) return;
        let cancelled = false;
        inventory.balances({ search: product.code, pageSize: 200 }).then(
            (result) => {
                if (!cancelled) setRows(result.items.filter((r) => r.productId === product.id));
            },
            (e) => {
                if (cancelled) return;
                setError(errorMessage(e));
                setRows([]);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [canRead, product.id, product.code]);

    if (!canRead) return <Alert severity="warning">Stok bilgisini görmek için yetkiniz yok.</Alert>;
    if (error) return <Alert severity="error">{error}</Alert>;
    if (rows === null) return <Skeleton variant="rounded" height={120} />;
    if (rows.length === 0) return <Typography color="text.secondary">Bu kart için henüz stok hareketi yok.</Typography>;

    const units = product.units.map((u) => ({ unit: u.unit, factor: u.factor }));
    const total = rows.reduce((sum, r) => sum + r.physical, 0);

    return (
        <Table size="small">
            <TableHead>
                <TableRow>
                    <TableCell>Depo</TableCell>
                    {product.variants.length > 0 && <TableCell>Varyant</TableCell>}
                    <TableCell align="right">Fiziksel</TableCell>
                    <TableCell align="right">Rezerve</TableCell>
                    <TableCell align="right">Kullanılabilir</TableCell>
                    {showCost && <TableCell align="right">Ort. maliyet</TableCell>}
                    <TableCell />
                </TableRow>
            </TableHead>
            <TableBody>
                {rows.map((r) => (
                    <TableRow key={`${r.warehouseId}-${r.variantId ?? ""}`}>
                        <TableCell>{r.warehouseName}</TableCell>
                        {product.variants.length > 0 && <TableCell>{r.variantCode ?? "—"}</TableCell>}
                        <TableCell align="right">{formatWithBreakdown(r.physical, r.unit, units)}</TableCell>
                        <TableCell align="right">{formatQuantity(r.reserved)}</TableCell>
                        <TableCell align="right">{formatQuantity(r.available)}</TableCell>
                        {showCost && <TableCell align="right">{r.avgCost === null ? "—" : formatMoney(r.avgCost, "TRY")}</TableCell>}
                        <TableCell align="right">
                            <Link href={itemCardHref(r.productId, r.variantId, r.warehouseId)}>Stok kartı</Link>
                        </TableCell>
                    </TableRow>
                ))}
                <TableRow>
                    <TableCell colSpan={product.variants.length > 0 ? 2 : 1}>
                        <strong>Toplam</strong>
                    </TableCell>
                    <TableCell align="right">
                        <strong>{formatQuantity(total)} {product.baseUnit}</strong>
                    </TableCell>
                    <TableCell colSpan={showCost ? 4 : 3} />
                </TableRow>
            </TableBody>
        </Table>
    );
}
