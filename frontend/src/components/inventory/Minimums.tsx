"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import ItemPicker from "@/components/pricing/ItemPicker";
import type { SellableItem } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatQuantity, inventory, type WarehouseDto } from "@/lib/api/inventory";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

type Row = { key: string; productId: string; variantId: string | null; code: string; name: string; min: string; reorder: string };

const text = (n: number | null) => (n === null ? "" : formatQuantity(n));

/** Minimum stok tanımı: deponun tüm satırları tek seferde kaydedilir (listede olmayan satır silinir). */
export default function Minimums() {
    const { hasPermission } = useSession();
    const canRead = hasPermission("stok.read");
    const canManage = hasPermission("stok.manage");

    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [warehouseId, setWarehouseId] = useState("");
    const [rows, setRows] = useState<Row[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        if (!canRead) return;
        inventory.warehouses().then((list) => {
            const normal = list.filter((w) => w.kind === "normal" && w.isActive);
            setWarehouses(normal);
            setWarehouseId((current) => current || normal[0]?.id || "");
        }, () => undefined);
    }, [canRead]);

    useEffect(() => {
        if (!canRead || !warehouseId) return;
        let cancelled = false;
        inventory.minimums(warehouseId).then(
            (list) => {
                if (cancelled) return;
                setError(null);
                setRows(list.map((m) => ({ key: `${m.productId}-${m.variantId ?? ""}`, productId: m.productId, variantId: m.variantId, code: m.productCode, name: m.productName, min: text(m.minQty), reorder: text(m.reorderQty) })));
            },
            (e) => {
                if (!cancelled) setError(errorMessage(e));
            },
        );
        return () => {
            cancelled = true;
        };
    }, [canRead, warehouseId]);

    if (!canRead) return <Alert severity="warning">Minimum stokları görmek için yetkiniz yok.</Alert>;

    const patch = (key: string, change: Partial<Row>) => {
        setSaved(false);
        setRows((current) => current?.map((r) => (r.key === key ? { ...r, ...change } : r)) ?? null);
    };

    const add = (item: SellableItem) => {
        const key = `${item.productId}-${item.variantId ?? ""}`;
        setSaved(false);
        setRows((current) => (current?.some((r) => r.key === key) ? current : [...(current ?? []), { key, productId: item.productId, variantId: item.variantId, code: item.code, name: item.name, min: "", reorder: "" }]));
    };

    async function save() {
        if (!rows) return;
        setError(null);
        setSaved(false);
        const input = [];
        for (const r of rows) {
            const min = parseAmount(r.min, { maxDecimals: 4 });
            const reorder = r.reorder.trim() ? parseAmount(r.reorder, { maxDecimals: 4 }) : null;
            if (min === null || min < 0 || (r.reorder.trim() && reorder === null)) {
                setError(`${r.code} satırındaki miktarlar geçersiz.`);
                return;
            }
            input.push({ productId: r.productId, variantId: r.variantId, minQty: min, reorderQty: reorder });
        }
        try {
            await inventory.setMinimums(warehouseId, input);
            setSaved(true);
        } catch (e) {
            setError(errorMessage(e));
        }
    }

    return (
        <>
            <PageHeader title="Minimum stok" subtitle="Kullanılabilir miktar minimumun altına düştüğünde uyarı üretilir." />
            <DashboardCard
                action={
                    <Button component={Link} href="/stok/durum" variant="text">
                        Minimumun altındakiler
                    </Button>
                }
            >
                <Stack spacing={2}>
                    <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} sx={{ maxWidth: 280 }}>
                        {warehouses.map((w) => (
                            <MenuItem key={w.id} value={w.id}>
                                {w.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <FormAlert error={error} />
                    {saved && <Alert severity="success">Minimum stoklar kaydedildi.</Alert>}
                    {canManage && <ItemPicker label="Kalem ekle" onPick={add} clearOnPick />}
                    {rows && rows.length === 0 ? (
                        <Typography color="text.secondary">Bu depo için minimum stok tanımlı değil.</Typography>
                    ) : (
                        rows && (
                            <TableContainer>
                                <Table size="small">
                                    <TableHead>
                                        <TableRow>
                                            <TableCell>Kod</TableCell>
                                            <TableCell>Ürün</TableCell>
                                            <TableCell>Minimum</TableCell>
                                            <TableCell>Sipariş miktarı</TableCell>
                                            {canManage && <TableCell />}
                                        </TableRow>
                                    </TableHead>
                                    <TableBody>
                                        {rows.map((r) => (
                                            <TableRow key={r.key}>
                                                <TableCell>{r.code}</TableCell>
                                                <TableCell>{r.name}</TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={r.min} disabled={!canManage} onChange={(e) => patch(r.key, { min: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${r.code} minimum` } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={r.reorder} disabled={!canManage} onChange={(e) => patch(r.key, { reorder: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${r.code} sipariş miktarı` } }} />
                                                </TableCell>
                                                {canManage && (
                                                    <TableCell align="right">
                                                        <IconButton aria-label={`${r.code} kaldır`} onClick={() => setRows((current) => current?.filter((x) => x.key !== r.key) ?? null)}>
                                                            <IconTrash size={18} />
                                                        </IconButton>
                                                    </TableCell>
                                                )}
                                            </TableRow>
                                        ))}
                                    </TableBody>
                                </Table>
                            </TableContainer>
                        )
                    )}
                    {canManage && (
                        <div>
                            <Button variant="contained" disabled={!rows} onClick={() => void save()}>
                                Kaydet
                            </Button>
                        </div>
                    )}
                </Stack>
            </DashboardCard>
        </>
    );
}
