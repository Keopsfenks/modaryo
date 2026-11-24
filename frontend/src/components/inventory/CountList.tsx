"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
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
import { catalog, flattenCategories, type CategoryNode } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { inventory, type CountRow, type WarehouseDto } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";

const STATUS_LABELS = { open: "Açık", posted: "Kapatıldı", cancelled: "İptal" } as const;

/** Sayım listesi ve yeni sayım başlatma (depo, isteğe bağlı kategori). */
export default function CountList() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canCount = hasPermission("stok.sayim");

    const [counts, setCounts] = useState<CountRow[] | null>(null);
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [categories, setCategories] = useState<CategoryNode[]>([]);
    const [warehouseId, setWarehouseId] = useState("");
    const [categoryId, setCategoryId] = useState("");
    const [name, setName] = useState("");
    const [error, setError] = useState<string | null>(null);

    const load = useCallback(async () => {
        try {
            setCounts(await inventory.counts());
        } catch (e) {
            setError(errorMessage(e));
            setCounts((current) => current ?? []);
        }
    }, []);

    useEffect(() => {
        if (!canCount) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
        inventory.warehouses().then((list) => {
            const normal = list.filter((w) => w.isActive);
            setWarehouses(normal);
            setWarehouseId((current) => current || (normal.find((w) => w.isDefault) ?? normal[0])?.id || "");
        }, () => undefined);
        catalog.categories().then(setCategories, () => undefined);
    }, [canCount, load]);

    if (!canCount) return <Alert severity="warning">Stok sayımı için yetkiniz yok.</Alert>;

    const warehouseName = (id: string) => warehouses.find((w) => w.id === id)?.name ?? "—";

    async function start() {
        setError(null);
        try {
            const created = await inventory.createCount({ warehouseId, categoryId: categoryId || null, name: name.trim() || null });
            router.push(`/stok/sayim/${created.id}`);
        } catch (e) {
            setError(errorMessage(e));
        }
    }

    return (
        <>
            <PageHeader title="Stok sayımı" subtitle="Sayım başlarken beklenen miktarlar sabitlenir; fark, sayılan ile sayım anındaki beklenen arasındadır." />
            <DashboardCard title="Yeni sayım">
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                    <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} sx={{ minWidth: 220 }}>
                        {warehouses.map((w) => (
                            <MenuItem key={w.id} value={w.id}>
                                {w.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField select size="small" label="Kategori" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} sx={{ minWidth: 220 }}>
                        <MenuItem value="">Tümü</MenuItem>
                        {flattenCategories(categories).map(({ node, depth }) => (
                            <MenuItem key={node.id} value={node.id}>
                                {`${"— ".repeat(depth)}${node.name}`}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField size="small" label="Ad (isteğe bağlı)" value={name} onChange={(e) => setName(e.target.value)} />
                    <Button variant="contained" disabled={!warehouseId} onClick={() => void start()}>
                        Sayım başlat
                    </Button>
                </Stack>
            </DashboardCard>
            <div style={{ height: 24 }} />
            <DashboardCard title="Sayımlar">
                <FormAlert error={error} />
                {counts === null ? (
                    <Skeleton variant="rounded" height={120} />
                ) : counts.length === 0 ? (
                    <Typography color="text.secondary">Henüz sayım yok.</Typography>
                ) : (
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Numara</TableCell>
                                <TableCell>Ad</TableCell>
                                <TableCell>Depo</TableCell>
                                <TableCell>Tarih</TableCell>
                                <TableCell>Kalem</TableCell>
                                <TableCell>Durum</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {counts.map((c) => (
                                <TableRow key={c.id} hover>
                                    <TableCell>
                                        <Link href={`/stok/sayim/${c.id}`}>{c.number}</Link>
                                    </TableCell>
                                    <TableCell>{c.name ?? "—"}</TableCell>
                                    <TableCell>{warehouseName(c.warehouseId)}</TableCell>
                                    <TableCell>{c.date.split("-").reverse().join(".")}</TableCell>
                                    <TableCell>{c.lineCount}</TableCell>
                                    <TableCell>{STATUS_LABELS[c.status]}</TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                )}
            </DashboardCard>
        </>
    );
}
