"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Chip from "@mui/material/Chip";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { catalog, flattenCategories, type CategoryNode } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatQuantity, formatWithBreakdown, inventory, type BalanceRow, type UnitFactor, type WarehouseDto } from "@/lib/api/inventory";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const PAGE_SIZE = 50;

/** Stok kartı bağlantısı. */
export const itemCardHref = (productId: string, variantId: string | null, warehouseId?: string): string => {
    const params = new URLSearchParams({ urun: productId });
    if (variantId) params.set("varyant", variantId);
    if (warehouseId) params.set("depo", warehouseId);
    return `/stok/hareketler?${params.toString()}`;
};

/** Stok durumu: depo x kalem bakiyeleri; filtreler, en büyük birimle gösterim, maliyet yalnızca maliyet.read ile. */
export default function StockStatus() {
    const { hasPermission } = useSession();
    const canRead = hasPermission("stok.read");
    const showCost = hasPermission("maliyet.read");

    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [categories, setCategories] = useState<CategoryNode[]>([]);
    const [warehouseId, setWarehouseId] = useState("");
    const [categoryId, setCategoryId] = useState("");
    const [searchText, setSearchText] = useState("");
    const search = useDebounced(searchText);
    const [onlyNonZero, setOnlyNonZero] = useState(false);
    const [belowMinimum, setBelowMinimum] = useState(false);
    const [page, setPage] = useState(0);
    const [rows, setRows] = useState<BalanceRow[] | null>(null);
    const [total, setTotal] = useState(0);
    const [error, setError] = useState<string | null>(null);
    const [units, setUnits] = useState<Record<string, UnitFactor[]>>({});

    useEffect(() => {
        if (!canRead) return;
        inventory.warehouses().then(setWarehouses, () => undefined);
        catalog.categories().then(setCategories, () => undefined);
    }, [canRead]);

    useEffect(() => {
        if (!canRead) return;
        let cancelled = false;
        inventory
            .balances({
                warehouseId: warehouseId || undefined,
                categoryId: categoryId || undefined,
                search: search || undefined,
                onlyNonZero,
                belowMinimum,
                page: page + 1,
                pageSize: PAGE_SIZE,
            })
            .then(
                (result) => {
                    if (cancelled) return;
                    setError(null);
                    setRows(result.items);
                    setTotal(result.total);
                },
                (e) => {
                    if (cancelled) return;
                    setError(errorMessage(e));
                    setRows((current) => current ?? []);
                },
            );
        return () => {
            cancelled = true;
        };
    }, [canRead, warehouseId, categoryId, search, onlyNonZero, belowMinimum, page]);

    // Satırlardaki ürünlerin birimleri (en büyük birim gösterimi için) tembel yüklenir.
    useEffect(() => {
        if (!rows) return;
        const missing = [...new Set(rows.map((r) => r.productId))].filter((id) => !(id in units));
        if (missing.length === 0) return;
        let cancelled = false;
        Promise.all(
            missing.map((id) =>
                catalog.getProduct(id).then(
                    (p) => [id, p.units.map((u) => ({ unit: u.unit, factor: u.factor }))] as const,
                    () => [id, [] as UnitFactor[]] as const,
                ),
            ),
        ).then((entries) => {
            if (!cancelled) setUnits((current) => ({ ...current, ...Object.fromEntries(entries) }));
        });
        return () => {
            cancelled = true;
        };
    }, [rows, units]);

    const flat = useMemo(() => flattenCategories(categories), [categories]);

    if (!canRead) return <Alert severity="warning">Stok durumunu görmek için yetkiniz yok.</Alert>;

    const filter = <T,>(set: (value: T) => void) => (value: T) => {
        setPage(0);
        set(value);
    };

    return (
        <>
            <PageHeader title="Stok durumu" subtitle="Depo ve kalem bazında fiziksel, rezerve ve kullanılabilir miktarlar." />
            <DashboardCard>
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 3, flexWrap: "wrap", alignItems: { md: "center" } }}>
                    <TextField size="small" label="Ara" value={searchText} onChange={(e) => filter(setSearchText)(e.target.value)} sx={{ minWidth: 220 }} />
                    <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => filter(setWarehouseId)(e.target.value)} sx={{ minWidth: 200 }}>
                        <MenuItem value="">Tüm depolar</MenuItem>
                        {warehouses.map((w) => (
                            <MenuItem key={w.id} value={w.id}>
                                {w.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField select size="small" label="Kategori" value={categoryId} onChange={(e) => filter(setCategoryId)(e.target.value)} sx={{ minWidth: 200 }}>
                        <MenuItem value="">Tüm kategoriler</MenuItem>
                        {flat.map(({ node, depth }) => (
                            <MenuItem key={node.id} value={node.id}>
                                {`${"— ".repeat(depth)}${node.name}`}
                            </MenuItem>
                        ))}
                    </TextField>
                    <FormControlLabel control={<Switch checked={onlyNonZero} onChange={(e) => filter(setOnlyNonZero)(e.target.checked)} />} label="Sadece stokta olanlar" />
                    <FormControlLabel control={<Switch checked={belowMinimum} onChange={(e) => filter(setBelowMinimum)(e.target.checked)} />} label="Minimumun altında" />
                </Stack>
                <FormAlert error={error} />
                {rows === null ? (
                    <Skeleton variant="rounded" height={200} />
                ) : rows.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Kayıt bulunamadı.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kod</TableCell>
                                    <TableCell>Ürün</TableCell>
                                    <TableCell>Depo</TableCell>
                                    <TableCell align="right">Fiziksel</TableCell>
                                    <TableCell align="right">Rezerve</TableCell>
                                    <TableCell align="right">Kullanılabilir</TableCell>
                                    <TableCell align="right">Minimum</TableCell>
                                    {showCost && <TableCell align="right">Ort. maliyet</TableCell>}
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((r) => {
                                    const low = r.minQty !== null && r.available < r.minQty;
                                    const factors = units[r.productId] ?? [];
                                    return (
                                        <TableRow key={`${r.warehouseId}-${r.productId}-${r.variantId ?? ""}`} hover>
                                            <TableCell>{r.variantCode ?? r.productCode}</TableCell>
                                            <TableCell>
                                                <Link href={itemCardHref(r.productId, r.variantId, r.warehouseId)}>{r.productName}</Link>
                                            </TableCell>
                                            <TableCell>{r.warehouseName}</TableCell>
                                            <TableCell align="right">{formatWithBreakdown(r.physical, r.unit, factors)}</TableCell>
                                            <TableCell align="right">{formatQuantity(r.reserved)}</TableCell>
                                            <TableCell align="right">
                                                {formatWithBreakdown(r.available, r.unit, factors)} {low && <Chip size="small" color="error" label="Düşük" />}
                                            </TableCell>
                                            <TableCell align="right">{r.minQty === null ? "—" : formatQuantity(r.minQty)}</TableCell>
                                            {showCost && <TableCell align="right">{r.avgCost === null ? "—" : formatMoney(r.avgCost, "TRY")}</TableCell>}
                                        </TableRow>
                                    );
                                })}
                            </TableBody>
                        </Table>
                        <TablePagination
                            component="div"
                            count={total}
                            page={page}
                            rowsPerPage={PAGE_SIZE}
                            rowsPerPageOptions={[PAGE_SIZE]}
                            onPageChange={(_, p) => setPage(p)}
                            labelDisplayedRows={({ from, to, count }) => `${from}–${to} / ${count}`}
                        />
                    </TableContainer>
                )}
            </DashboardCard>
        </>
    );
}
