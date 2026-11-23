"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import FormControlLabel from "@mui/material/FormControlLabel";
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
import { catalog, flattenCategories, type BrandDto, type CategoryNode, type ProductKind, type ProductListItem } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const DEBOUNCE_MS = 300;

type ProductListProps = {
    title: string;
    /** Verilirse yalnızca o türdeki kartlar listelenir. */
    kind?: ProductKind;
};

/** Debounce edilmiş metin filtresi: yazılan değer ile API'ye giden değeri ayırır. */
function useDebounced(value: string): string {
    const [debounced, setDebounced] = useState(value);
    useEffect(() => {
        const handle = setTimeout(() => setDebounced(value.trim()), DEBOUNCE_MS);
        return () => clearTimeout(handle);
    }, [value]);
    return debounced;
}

/** Ürün ve hizmet listesi: arama, kategori/marka/etiket süzgeçleri, sayfalama, Excel dışa aktarma. */
export default function ProductList({ title, kind }: ProductListProps) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("katalog.manage");
    const isService = kind === "service";

    const [searchInput, setSearchInput] = useState("");
    const [tagInput, setTagInput] = useState("");
    const search = useDebounced(searchInput);
    const tag = useDebounced(tagInput);
    const [categoryId, setCategoryId] = useState("");
    const [brandId, setBrandId] = useState("");
    const [includeInactive, setIncludeInactive] = useState(false);
    // Süzgeç değişince sayfa kendiliğinden başa döner: sayfa, hangi süzgeç anahtarına ait olduğunu da taşır.
    const filterKey = [search, tag, categoryId, brandId, includeInactive].join("|");
    const [pageState, setPageState] = useState({ key: filterKey, page: 0 });
    const page = pageState.key === filterKey ? pageState.page : 0;
    const setPage = (next: number) => setPageState({ key: filterKey, page: next });
    const [pageSize, setPageSize] = useState(25);

    const [categories, setCategories] = useState<CategoryNode[]>([]);
    const [brands, setBrands] = useState<BrandDto[]>([]);
    const [result, setResult] = useState<{ items: ProductListItem[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [exporting, setExporting] = useState(false);

    useEffect(() => {
        catalog.categories().then(setCategories, () => undefined);
        catalog.brands().then(setBrands, () => undefined);
    }, []);

    useEffect(() => {
        let cancelled = false;
        catalog
            .listProducts({
                search: search || undefined,
                kind,
                categoryId: categoryId || undefined,
                brandId: brandId || undefined,
                tag: tag || undefined,
                includeInactive: includeInactive || undefined,
                page: page + 1,
                pageSize,
            })
            .then(
                (loaded) => {
                    if (cancelled) return;
                    setError(null);
                    setResult(loaded);
                },
                (e: unknown) => {
                    if (cancelled) return;
                    if (!(e instanceof ApiError)) throw e;
                    setError(e);
                    setResult((current) => current ?? { items: [], total: 0 });
                },
            );
        return () => {
            cancelled = true;
        };
    }, [search, tag, kind, categoryId, brandId, includeInactive, page, pageSize]);

    const categoryOptions = useMemo(() => flattenCategories(categories), [categories]);

    async function exportXlsx() {
        setExporting(true);
        try {
            await catalog.exportProducts({ kind });
            setError(null);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setExporting(false);
        }
    }

    const hasFilter = Boolean(search || tag || categoryId || brandId);
    const isEmpty = result !== null && result.total === 0 && !hasFilter && !error;
    const noun = isService ? "hizmet" : "ürün";
    const newHref = isService ? "/stok/urunler/yeni?tur=hizmet" : "/stok/urunler/yeni";
    const newButton = (
        <Button component={Link} href={newHref} variant="contained">
            {isService ? "Yeni hizmet" : "Yeni ürün"}
        </Button>
    );
    const toolbar = (
        <Stack direction="row" spacing={1}>
            <Button variant="outlined" onClick={exportXlsx} disabled={exporting}>
                Dışa aktar
            </Button>
            {canManage && (
                <Button component={Link} href="/stok/ice-aktar" variant="outlined">
                    İçe aktar
                </Button>
            )}
            {canManage && newButton}
        </Stack>
    );

    return (
        <>
            <PageHeader title={title} subtitle={isService ? "Stok takibi olmayan hizmet kartları" : "Ürün kartları, varyantlar ve satış fiyatları"} />
            <DashboardCard action={toolbar}>
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2, flexWrap: "wrap", alignItems: { md: "center" } }}>
                    <TextField
                        size="small"
                        label="Ara"
                        placeholder="Ad, kod, barkod veya tedarikçi kodu"
                        value={searchInput}
                        onChange={(e) => setSearchInput(e.target.value)}
                        sx={{ minWidth: 280 }}
                    />
                    <TextField
                        size="small"
                        select
                        label="Kategori"
                        value={categoryId}
                        onChange={(e) => setCategoryId(e.target.value)}
                        slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
                        sx={{ minWidth: 180 }}
                    >
                        <option value="">Tümü</option>
                        {categoryOptions.map(({ node, depth }) => (
                            <option key={node.id} value={node.id}>
                                {`${"— ".repeat(depth)}${node.name}`}
                            </option>
                        ))}
                    </TextField>
                    <TextField
                        size="small"
                        select
                        label="Marka"
                        value={brandId}
                        onChange={(e) => setBrandId(e.target.value)}
                        slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
                        sx={{ minWidth: 160 }}
                    >
                        <option value="">Tümü</option>
                        {brands.map((b) => (
                            <option key={b.id} value={b.id}>
                                {b.name}
                            </option>
                        ))}
                    </TextField>
                    <TextField size="small" label="Etiket" value={tagInput} onChange={(e) => setTagInput(e.target.value)} sx={{ minWidth: 140 }} />
                    <FormControlLabel
                        control={<Switch checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />}
                        label="Pasifleri göster"
                    />
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : isEmpty ? (
                    <Box sx={{ py: 6, textAlign: "center" }}>
                        <Typography variant="h6" sx={{ mb: 1 }}>
                            {includeInactive ? `Henüz ${noun} yok` : `Gösterilecek ${noun} yok`}
                        </Typography>
                        <Typography color="text.secondary" sx={{ mb: 2 }}>
                            {includeInactive ? `İlk ${noun} kartınızı oluşturarak başlayın.` : "Yeni bir kart oluşturabilir veya pasifleri göstermeyi deneyebilirsiniz."}
                        </Typography>
                    </Box>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 820, "& td, & th": { px: 1, py: 1.5 } }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kod</TableCell>
                                        <TableCell>Ad</TableCell>
                                        <TableCell>Kategori</TableCell>
                                        <TableCell>Marka</TableCell>
                                        <TableCell>Birim</TableCell>
                                        <TableCell align="right">Satış fiyatı</TableCell>
                                        <TableCell>Durum</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((p) => (
                                        <TableRow key={p.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`/stok/urunler/${p.id}`)}>
                                            <TableCell>{p.code}</TableCell>
                                            <TableCell>
                                                <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                                    {p.name}
                                                </Typography>
                                                {p.variantCount > 0 && (
                                                    <Typography variant="caption" color="text.secondary">
                                                        {p.variantCount} varyant
                                                    </Typography>
                                                )}
                                            </TableCell>
                                            <TableCell>{p.categoryPath ?? "—"}</TableCell>
                                            <TableCell>{p.brandName ?? "—"}</TableCell>
                                            <TableCell>{p.baseUnit}</TableCell>
                                            <TableCell align="right">
                                                {p.defaultSalePrice ? formatMoney(p.defaultSalePrice.amount, p.defaultSalePrice.currency) : "—"}
                                            </TableCell>
                                            <TableCell>{p.isActive ? <Chip size="small" color="success" variant="outlined" label="Aktif" /> : <Chip size="small" label="Pasif" />}</TableCell>
                                        </TableRow>
                                    ))}
                                    {result.items.length === 0 && (
                                        <TableRow>
                                            <TableCell colSpan={7} align="center" sx={{ py: 4, color: "text.secondary" }}>
                                                Sonuç bulunamadı
                                            </TableCell>
                                        </TableRow>
                                    )}
                                </TableBody>
                            </Table>
                        </TableContainer>
                        <TablePagination
                            component="div"
                            count={result.total}
                            page={page}
                            rowsPerPage={pageSize}
                            rowsPerPageOptions={[25, 50, 100]}
                            labelRowsPerPage="Sayfa başına:"
                            labelDisplayedRows={({ from, to, count }) => `${from}–${to} / ${count}`}
                            onPageChange={(_, next) => setPage(next)}
                            onRowsPerPageChange={(e) => {
                                setPageSize(Number(e.target.value));
                                setPage(0);
                            }}
                        />
                    </>
                )}
            </DashboardCard>
        </>
    );
}
