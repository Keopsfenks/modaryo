"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
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
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatQuantity } from "@/lib/api/inventory";
import { purchasing, type SuggestionGroup, type SuggestionLine } from "@/lib/api/purchasing";
import { formatMoney, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const lineKey = (l: SuggestionLine) => `${l.productId}|${l.variantId ?? ""}|${l.warehouseId}`;
const groupKey = (g: SuggestionGroup) => g.supplierId ?? "none";

/** Minimum stoğun altındaki kalemler için sipariş önerileri: tedarikçiye göre gruplu, miktarlar düzenlenebilir, seçili gruplardan taslak siparişler üretilir. */
export default function Suggestions() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satinalma.read");
    const canCreate = hasPermission("satinalma.manage") && hasPermission("fiyat.alis");
    const showPrices = hasPermission("fiyat.alis");
    const [groups, setGroups] = useState<SuggestionGroup[] | null>(null);
    const [quantities, setQuantities] = useState<Record<string, string>>({});
    const [excluded, setExcluded] = useState<Record<string, boolean>>({});
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        purchasing.suggestions.get().then(
            (r) => {
                if (cancelled) return;
                setGroups(r.groups);
                setQuantities(Object.fromEntries(r.groups.flatMap((g) => g.lines.map((l) => [lineKey(l), String(l.suggestedQty).replace(".", ",")]))));
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setGroups([]);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed]);

    if (!allowed) return <Alert severity="warning">Sipariş önerilerini görüntülemek için yetkiniz yok.</Alert>;

    const parsed = (l: SuggestionLine) => parseAmount(quantities[lineKey(l)] ?? "", { maxDecimals: 4 });

    async function create() {
        if (!groups) return;
        const payload: { supplierId: string; lines: { productId: string; variantId: string | null; warehouseId: string; quantity: number }[] }[] = [];
        for (const g of groups) {
            if (g.supplierId === null || excluded[groupKey(g)]) continue;
            const lines = [];
            for (const l of g.lines) {
                const text = (quantities[lineKey(l)] ?? "").trim();
                if (!text || text === "0") continue;
                const q = parsed(l);
                if (q === null || q <= 0) return setError(`${l.productCode}: miktar geçersiz.`);
                lines.push({ productId: l.productId, variantId: l.variantId, warehouseId: l.warehouseId, quantity: q });
            }
            if (lines.length > 0) payload.push({ supplierId: g.supplierId, lines });
        }
        if (payload.length === 0) return setError("Sipariş oluşturmak için en az bir tedarikçi grubunda miktar girin.");
        setBusy(true);
        setError(null);
        try {
            await purchasing.suggestions.createOrders({ groups: payload });
            router.push("/satin-alma/siparisler");
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title="Sipariş önerileri" subtitle="Minimum stoğun altına düşen kalemler, varsayılan tedarikçiye göre gruplanır" />
            <FormAlert error={error} />
            {groups === null ? (
                <Skeleton variant="rounded" height={200} />
            ) : groups.length === 0 ? (
                <DashboardCard>
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Minimum stoğun altında kalem yok; sipariş önerisi bulunmuyor.
                    </Typography>
                </DashboardCard>
            ) : (
                <Stack spacing={3}>
                    {groups.map((g) => {
                        const noSupplier = g.supplierId === null;
                        return (
                            <DashboardCard
                                key={groupKey(g)}
                                title={g.supplierName}
                                action={
                                    !noSupplier && canCreate ? (
                                        <FormControlLabel
                                            control={<Checkbox checked={!excluded[groupKey(g)]} onChange={(e) => setExcluded((cur) => ({ ...cur, [groupKey(g)]: !e.target.checked }))} />}
                                            label="Sipariş oluştur"
                                        />
                                    ) : undefined
                                }
                            >
                                {noSupplier && (
                                    <Alert severity="info" sx={{ mb: 2 }}>
                                        Bu kalemlerin varsayılan tedarikçisi yok; sipariş taslağı oluşturulamaz. Ürün kartından varsayılan tedarikçi tanımlayın.
                                    </Alert>
                                )}
                                <TableContainer>
                                    <Table size="small" sx={{ minWidth: 640 }}>
                                        <TableHead>
                                            <TableRow>
                                                <TableCell>Kalem</TableCell>
                                                <TableCell align="right">Kullanılabilir</TableCell>
                                                <TableCell align="right">Yolda</TableCell>
                                                <TableCell align="right">Minimum</TableCell>
                                                {showPrices && <TableCell align="right">Son alış</TableCell>}
                                                <TableCell sx={{ width: 140 }}>Önerilen miktar</TableCell>
                                            </TableRow>
                                        </TableHead>
                                        <TableBody>
                                            {g.lines.map((l) => (
                                                <TableRow key={lineKey(l)}>
                                                    <TableCell>
                                                        {l.productCode} · {l.productName}
                                                    </TableCell>
                                                    <TableCell align="right">{formatQuantity(l.available)}</TableCell>
                                                    <TableCell align="right">{formatQuantity(l.onOrder)}</TableCell>
                                                    <TableCell align="right">{formatQuantity(l.minQty)}</TableCell>
                                                    {showPrices && <TableCell align="right">{l.lastPrice === null ? "-" : formatMoney(l.lastPrice, "TRY")}</TableCell>}
                                                    <TableCell>
                                                        <TextField
                                                            size="small"
                                                            value={quantities[lineKey(l)] ?? ""}
                                                            disabled={noSupplier || !canCreate}
                                                            error={(quantities[lineKey(l)] ?? "").trim() !== "" && parsed(l) === null}
                                                            onChange={(e) => setQuantities((cur) => ({ ...cur, [lineKey(l)]: e.target.value }))}
                                                            slotProps={{ htmlInput: { "aria-label": `${l.productCode} miktar`, inputMode: "decimal" } }}
                                                        />
                                                    </TableCell>
                                                </TableRow>
                                            ))}
                                        </TableBody>
                                    </Table>
                                </TableContainer>
                            </DashboardCard>
                        );
                    })}
                    {canCreate && (
                        <Stack direction="row">
                            <Button variant="contained" disabled={busy} onClick={() => void create()}>
                                {busy ? "Oluşturuluyor..." : "Sipariş taslakları oluştur"}
                            </Button>
                        </Stack>
                    )}
                </Stack>
            )}
        </>
    );
}
