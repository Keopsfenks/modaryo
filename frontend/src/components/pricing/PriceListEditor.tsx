"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import { alpha } from "@mui/material/styles";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconListDetails, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import type { SellableItem } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type PriceItemDto, type PriceListDto, type PriceRowInput } from "@/lib/api/pricing";
import { useSession } from "@/session/SessionProvider";
import BulkAdjustDialog from "./BulkAdjustDialog";
import ItemPicker from "./ItemPicker";
import { derivedText, validityText } from "./PriceLists";
import PriceListDialog from "./PriceListDialog";
import TierEditor from "./TierEditor";
import { emptyTiers, parseTiers, summarizeTiers, toTierText, validateTiers, type TierText } from "./tiers";
import { useDebounced } from "./useDebounced";

const PAGE_SIZE = 50;
const STALE_MESSAGE = "Bu satır başka biri tarafından değiştirildi.";

type Row = {
    key: string;
    id: string | null;
    productId: string;
    variantId: string | null;
    productCode: string;
    productName: string;
    variantCode: string | null;
    unit: string;
    /** Yeni satırda seçilebilecek birimler. */
    units: string[];
    tiers: TierText[];
    /** Sunucudaki sürüm; yeni ve türetilmiş satırlarda null. */
    version: number | null;
    derived: boolean;
    isNew: boolean;
};

const rowKey = (productId: string, variantId: string | null, unit: string) => `${productId}|${variantId ?? ""}|${unit}`;

const fromDto = (d: PriceItemDto): Row => ({
    key: rowKey(d.productId, d.variantId, d.unit),
    id: d.id,
    productId: d.productId,
    variantId: d.variantId,
    productCode: d.productCode,
    productName: d.productName,
    variantCode: d.variantCode,
    unit: d.unit,
    units: [d.unit],
    tiers: toTierText(d.tiers),
    version: d.derived || !d.id ? null : d.version,
    derived: d.derived,
    isNew: false,
});

function newRow(item: SellableItem): Row {
    const units = [...new Set([item.baseUnit, item.measureUnit, ...item.units.map((u) => u.unit)].filter((u): u is string => Boolean(u)))];
    const unit = item.soldByMeasure && item.measureUnit ? item.measureUnit : item.baseUnit;
    return {
        key: rowKey(item.productId, item.variantId, unit),
        id: null,
        productId: item.productId,
        variantId: item.variantId,
        productCode: item.code,
        productName: item.name,
        variantCode: null,
        unit,
        units,
        tiers: emptyTiers(),
        version: null,
        derived: false,
        isNew: true,
    };
}

/** Fiyat listesi düzenleyicisi: kalem ekleme, kademeli fiyat, türetilmiş satırlar, toplu ayar ve iyimser kilit çakışması. */
export default function PriceListEditor({ id }: { id: string }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("fiyat.manage");

    const [list, setList] = useState<PriceListDto | null>(null);
    const [baseName, setBaseName] = useState<string | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput);
    const [page, setPage] = useState(0);
    const [data, setData] = useState<{ items: PriceItemDto[]; total: number } | null>(null);
    const [reloadTick, setReloadTick] = useState(0);

    const [edits, setEdits] = useState<Record<string, Row>>({});
    const [rowErrors, setRowErrors] = useState<Record<string, string>>({});
    const [conflicts, setConflicts] = useState<Set<string>>(new Set());
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    const [tierKey, setTierKey] = useState<string | null>(null);
    const [bulkOpen, setBulkOpen] = useState(false);
    const [editLists, setEditLists] = useState<PriceListDto[] | null>(null);
    const [deleting, setDeleting] = useState<Row | null>(null);
    const [confirmLeave, setConfirmLeave] = useState(false);

    const dirtyCount = Object.keys(edits).length;
    const dirty = dirtyCount > 0;

    useEffect(() => {
        let ignore = false;
        pricing.getList(id).then(
            (dto) => !ignore && setList(dto),
            (e) => !ignore && setLoadError(errorMessage(e)),
        );
        return () => {
            ignore = true;
        };
    }, [id]);

    const baseListId = list?.baseListId ?? null;
    useEffect(() => {
        if (!baseListId) return;
        let ignore = false;
        pricing.getList(baseListId).then(
            (dto) => !ignore && setBaseName(dto.name),
            () => undefined,
        );
        return () => {
            ignore = true;
        };
    }, [baseListId]);

    useEffect(() => {
        if (!list) return;
        let ignore = false;
        pricing.listItems(id, { search: search || undefined, page: page + 1, pageSize: PAGE_SIZE }).then(
            (result) => !ignore && setData(result),
            (e) => {
                if (!ignore) {
                    setError(errorMessage(e));
                    setData((current) => current ?? { items: [], total: 0 });
                }
            },
        );
        return () => {
            ignore = true;
        };
    }, [id, list, search, page, reloadTick]);

    // Kaydedilmemiş değişiklik varken sekmeyi kapatma ya da yenileme uyarısı.
    useEffect(() => {
        if (!dirty) return;
        const handler = (event: BeforeUnloadEvent) => {
            event.preventDefault();
        };
        window.addEventListener("beforeunload", handler);
        return () => window.removeEventListener("beforeunload", handler);
    }, [dirty]);

    const rows = useMemo<Row[]>(() => {
        const created = Object.values(edits).filter((r) => r.isNew);
        const server = (data?.items ?? []).map((d) => {
            const base = fromDto(d);
            return edits[base.key] ?? base;
        });
        return [...created, ...server.filter((r) => !r.isNew)];
    }, [edits, data]);

    const put = useCallback((row: Row) => setEdits((prev) => ({ ...prev, [row.key]: row })), []);
    const patchRow = (row: Row, change: Partial<Row>) => {
        setNotice(null);
        put({ ...row, ...change });
    };

    function addItem(item: SellableItem) {
        setNotice(null);
        const row = newRow(item);
        const exists = rows.some((r) => r.key === row.key);
        if (exists) {
            setError(`${item.code} kalemi bu listede zaten var; mevcut satırı düzenleyin.`);
            return;
        }
        setError(null);
        put(row);
    }

    function changeUnit(row: Row, unit: string) {
        const key = rowKey(row.productId, row.variantId, unit);
        setEdits((prev) => {
            const next = { ...prev };
            delete next[row.key];
            next[key] = { ...row, key, unit };
            return next;
        });
    }

    function discard() {
        setEdits({});
        setRowErrors({});
        setConflicts(new Set());
        setNotice(null);
    }

    async function reloadPage() {
        const fresh = await pricing.listItems(id, { search: search || undefined, page: page + 1, pageSize: PAGE_SIZE });
        setData(fresh);
        return fresh;
    }

    async function save() {
        if (!list || busy) return;
        const found: Record<string, string> = {};
        const inputs: PriceRowInput[] = [];
        for (const row of Object.values(edits)) {
            const message = validateTiers(row.tiers);
            if (message) {
                found[row.key] = message;
                continue;
            }
            inputs.push({
                productId: row.productId,
                variantId: row.variantId,
                unit: row.unit,
                tiers: parseTiers(row.tiers)!,
                ...(row.version !== null ? { version: row.version } : {}),
            });
        }
        setRowErrors(found);
        setNotice(null);
        if (Object.keys(found).length > 0) {
            setError(`${Object.keys(found).length} satırda hata var; satırların altındaki iletilere bakın.`);
            return;
        }

        setBusy(true);
        setError(null);
        try {
            await pricing.upsertItems(list.id, inputs);
            setEdits({});
            setConflicts(new Set());
            setNotice(`${inputs.length} satır kaydedildi.`);
            await reloadPage();
        } catch (e) {
            if (e instanceof ApiError && e.code === "version_conflict") await handleConflict();
            else setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    // Sürümü eskiyen satırları bulup işaretler; girilen değerler korunur, sürüm tazelenir (bilinçli yeniden kayıtla üzerine yazılır).
    async function handleConflict() {
        let fresh: { items: PriceItemDto[]; total: number };
        try {
            fresh = await reloadPage();
        } catch (e) {
            setError(errorMessage(e));
            return;
        }
        const stale = new Set<string>();
        const nextEdits = { ...edits };
        for (const row of Object.values(edits)) {
            if (row.version === null) continue;
            const server = fresh.items.find((d) => rowKey(d.productId, d.variantId, d.unit) === row.key);
            if (!server || server.version !== row.version) {
                stale.add(row.key);
                nextEdits[row.key] = { ...row, version: server && !server.derived ? server.version : null };
            }
        }
        setEdits(nextEdits);
        setConflicts(stale);
        setError(
            stale.size > 0
                ? "Bazı satırlar siz düzenlerken başka biri tarafından değiştirildi. İşaretli satırları gözden geçirip yeniden kaydedin."
                : "Liste siz düzenlerken değişti. Sayfa yenilendi; değişikliklerinizi kontrol edip yeniden kaydedin.",
        );
    }

    async function confirmDelete() {
        const row = deleting;
        if (!row || !list) return;
        setDeleting(null);
        setError(null);
        try {
            if (row.id) await pricing.deleteItem(list.id, row.id);
            setEdits((prev) => {
                const next = { ...prev };
                delete next[row.key];
                return next;
            });
            await reloadPage();
        } catch (e) {
            setError(errorMessage(e));
        }
    }

    function removeRow(row: Row) {
        if (row.isNew) {
            setEdits((prev) => {
                const next = { ...prev };
                delete next[row.key];
                return next;
            });
        } else {
            setDeleting(row);
        }
    }

    async function openEdit() {
        if (!list) return;
        try {
            setEditLists(await pricing.lists({ kind: list.kind, includeInactive: true }));
        } catch {
            setEditLists([]);
        }
    }

    if (loadError) return <FormAlert error={loadError} />;
    if (!list) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress aria-label="Yükleniyor" />
            </Box>
        );
    }

    const derivedLabel = derivedText(list, baseName ? [{ ...list, id: list.baseListId!, name: baseName }] : []);
    const tierRow = tierKey ? rows.find((r) => r.key === tierKey) : undefined;
    const leaveTo = list.kind === "purchase" ? "/satin-alma/alis-fiyatlari" : "/satis/fiyat-listeleri";

    return (
        <>
            <PageHeader
                title={list.name}
                subtitle={`${list.kind === "purchase" ? "Alış" : "Satış"} listesi · ${list.currency} · ${list.vatIncluded ? "KDV dahil" : "KDV hariç"} · ${validityText(list.validFrom, list.validTo)}`}
            />
            <Stack direction="row" spacing={1} sx={{ mb: 2, alignItems: "center", flexWrap: "wrap" }}>
                {list.isDefault && <Chip size="small" color="primary" label="Varsayılan" />}
                {!list.isActive && <Chip size="small" label="Pasif" />}
                {derivedLabel && <Chip size="small" variant="outlined" label={`Türetilmiş: ${derivedLabel}`} />}
                <Button size="small" variant="outlined" onClick={() => (dirty ? setConfirmLeave(true) : router.push(leaveTo))}>
                    Listelere dön
                </Button>
                {canManage && (
                    <Button size="small" variant="outlined" onClick={() => void openEdit()}>
                        Liste bilgileri
                    </Button>
                )}
            </Stack>

            <FormAlert error={error} />
            {notice && !error && (
                <Alert severity="success" sx={{ mb: 2 }}>
                    {notice}
                </Alert>
            )}
            {!canManage && (
                <Alert severity="info" sx={{ mb: 2 }}>
                    Listeyi düzenlemek için yetkiniz yok; fiyatlar salt okunur.
                </Alert>
            )}
            {dirty && (
                <Alert severity="warning" sx={{ mb: 2 }}>
                    {dirtyCount} satırda kaydedilmemiş değişiklik var.
                </Alert>
            )}

            <DashboardCard
                action={
                    canManage && (
                        <Stack direction="row" spacing={1}>
                            <Button variant="outlined" disabled={dirty} onClick={() => setBulkOpen(true)}>
                                Toplu fiyat ayarı
                            </Button>
                            <Button variant="text" disabled={!dirty || busy} onClick={discard}>
                                Değişiklikleri at
                            </Button>
                            <Button variant="contained" disabled={!dirty || busy} onClick={() => void save()}>
                                {busy ? "Kaydediliyor..." : "Kaydet"}
                            </Button>
                        </Stack>
                    )
                }
            >
                <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ mb: 2 }}>
                    {canManage && <ItemPicker label="Kalem ekle" clearOnPick onPick={addItem} />}
                    <TextField size="small" label="Listede ara" value={searchInput} onChange={(e) => { setSearchInput(e.target.value); setPage(0); }} sx={{ minWidth: 220 }} />
                </Stack>

                {rows.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        {data === null ? "Yükleniyor..." : "Bu listede henüz kalem yok."}
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kalem</TableCell>
                                    <TableCell>Birim</TableCell>
                                    <TableCell>Fiyat</TableCell>
                                    <TableCell align="right">İşlem</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((row) => {
                                    const stale = conflicts.has(row.key);
                                    const greyed = row.derived && !edits[row.key];
                                    return (
                                        <TableRow
                                            key={row.key}
                                            data-testid={`row-${row.productCode}`}
                                            sx={stale ? { bgcolor: (theme) => alpha(theme.palette.error.main, 0.08), outline: "2px solid", outlineColor: "error.main", outlineOffset: -2 } : undefined}
                                        >
                                            <TableCell>
                                                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                                                    {row.productCode} · {row.productName}
                                                </Typography>
                                                {row.variantCode && (
                                                    <Typography variant="caption" color="text.secondary">
                                                        Varyant: {row.variantCode}
                                                    </Typography>
                                                )}
                                                {stale && (
                                                    <Typography variant="caption" color="error" sx={{ display: "block" }}>
                                                        {STALE_MESSAGE}
                                                    </Typography>
                                                )}
                                            </TableCell>
                                            <TableCell sx={{ width: 120 }}>
                                                {row.isNew && row.units.length > 1 ? (
                                                    <TextField select size="small" value={row.unit} onChange={(e) => changeUnit(row, e.target.value)} slotProps={{ htmlInput: { "aria-label": `${row.productCode} birimi` } }}>
                                                        {row.units.map((u) => (
                                                            <MenuItem key={u} value={u}>
                                                                {u}
                                                            </MenuItem>
                                                        ))}
                                                    </TextField>
                                                ) : (
                                                    row.unit
                                                )}
                                            </TableCell>
                                            <TableCell>
                                                {greyed ? (
                                                    <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                                                        <Typography color="text.disabled" data-testid={`derived-${row.productCode}`}>
                                                            {summarizeTiers(parseTiers(row.tiers) ?? [])}
                                                        </Typography>
                                                        <Chip size="small" variant="outlined" label="Türetilmiş" />
                                                        {canManage && (
                                                            <Button size="small" onClick={() => patchRow(row, { derived: false, version: null })}>
                                                                Elle fiyat ver
                                                            </Button>
                                                        )}
                                                    </Stack>
                                                ) : row.tiers.length === 1 ? (
                                                    <TextField
                                                        size="small"
                                                        value={row.tiers[0].price}
                                                        disabled={!canManage}
                                                        onChange={(e) => patchRow(row, { tiers: [{ ...row.tiers[0], price: e.target.value }] })}
                                                        error={Boolean(rowErrors[row.key])}
                                                        helperText={rowErrors[row.key]}
                                                        slotProps={{ htmlInput: { inputMode: "decimal", "aria-label": `${row.productCode} fiyatı` } }}
                                                        sx={{ width: 160 }}
                                                    />
                                                ) : (
                                                    <>
                                                        <Typography variant="body2">{summarizeTiers(parseTiers(row.tiers) ?? [])}</Typography>
                                                        {rowErrors[row.key] && (
                                                            <Typography variant="caption" color="error">
                                                                {rowErrors[row.key]}
                                                            </Typography>
                                                        )}
                                                    </>
                                                )}
                                            </TableCell>
                                            <TableCell align="right">
                                                {!greyed && (
                                                    <IconButton aria-label={`${row.productCode} kademeleri`} size="small" onClick={() => setTierKey(row.key)}>
                                                        <IconListDetails size={18} />
                                                    </IconButton>
                                                )}
                                                {canManage && !row.derived && (
                                                    <IconButton aria-label={`${row.productCode} satırını sil`} size="small" onClick={() => removeRow(row)}>
                                                        <IconTrash size={18} />
                                                    </IconButton>
                                                )}
                                            </TableCell>
                                        </TableRow>
                                    );
                                })}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
                {data && (
                    <TablePagination
                        component="div"
                        count={data.total}
                        page={page}
                        rowsPerPage={PAGE_SIZE}
                        rowsPerPageOptions={[PAGE_SIZE]}
                        onPageChange={(_, next) => setPage(next)}
                        labelDisplayedRows={({ from, to, count }) => `${from}–${to} / ${count}`}
                    />
                )}
            </DashboardCard>

            <Dialog open={Boolean(tierRow)} onClose={() => setTierKey(null)} fullWidth maxWidth="sm">
                <DialogTitle>Kademeler{tierRow ? ` — ${tierRow.productCode} (${tierRow.unit})` : ""}</DialogTitle>
                <DialogContent>
                    {tierRow && (
                        <Box sx={{ pt: 1 }}>
                            <TierEditor tiers={tierRow.tiers} unit={tierRow.unit} disabled={!canManage} onChange={(tiers) => patchRow(tierRow, { tiers })} />
                        </Box>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setTierKey(null)}>Tamam</Button>
                </DialogActions>
            </Dialog>

            <BulkAdjustDialog
                open={bulkOpen}
                listId={list.id}
                onClose={() => setBulkOpen(false)}
                onDone={(updated) => {
                    setBulkOpen(false);
                    setNotice(`${updated} satır güncellendi.`);
                    setReloadTick((n) => n + 1);
                }}
            />

            <PriceListDialog
                open={editLists !== null}
                kind={list.kind}
                list={list}
                lists={editLists ?? []}
                onClose={() => setEditLists(null)}
                onSaved={(saved) => {
                    setEditLists(null);
                    setList(saved);
                }}
            />

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Satır silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        {deleting?.productCode} · {deleting?.productName} ({deleting?.unit}) bu listeden kaldırılacak.
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={confirmLeave} onClose={() => setConfirmLeave(false)}>
                <DialogTitle>Değişiklikler kaydedilmedi</DialogTitle>
                <DialogContent>
                    <DialogContentText>Kaydedilmemiş değişiklikler var; çıkarsanız kaybolacak.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmLeave(false)}>Düzenlemeye dön</Button>
                    <Button color="error" variant="contained" onClick={() => router.push(leaveTo)}>
                        Kaydetmeden çık
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
