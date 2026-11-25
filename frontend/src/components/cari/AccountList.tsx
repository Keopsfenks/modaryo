"use client";

import { useEffect, useState } from "react";
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
import { ApiError } from "@/lib/api/client";
import { erp, type AccountRole, type CurrentAccountListItem } from "@/lib/api/erp";
import { formatBalance, formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const SEARCH_DEBOUNCE_MS = 300;

type AccountListProps = {
    /** Verilirse yalnızca o roldeki cariler listelenir. */
    role?: AccountRole;
    title: string;
};

/** Cari hesap listesi: arama, pasifleri göster, sayfalama. Satıra tıklayınca detay açılır. */
export default function AccountList({ role, title }: AccountListProps) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("cari.manage");

    const [searchInput, setSearchInput] = useState("");
    const [search, setSearch] = useState("");
    const [includeInactive, setIncludeInactive] = useState(false);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: CurrentAccountListItem[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        const handle = setTimeout(() => {
            const next = searchInput.trim();
            if (next !== search) {
                setSearch(next);
                setPage(0);
            }
        }, SEARCH_DEBOUNCE_MS);
        return () => clearTimeout(handle);
    }, [searchInput, search]);

    useEffect(() => {
        let cancelled = false;
        erp.listAccounts({ search: search || undefined, role, includeInactive: includeInactive || undefined, page: page + 1, pageSize }).then(
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
    }, [search, role, includeInactive, page, pageSize]);

    const newHref = `/cari/yeni${role === "customer" ? "?rol=musteri" : role === "supplier" ? "?rol=tedarikci" : ""}`;
    const newButton = (
        <Button component={Link} href={newHref} variant="contained">
            Yeni cari
        </Button>
    );
    const isEmpty = result !== null && result.total === 0 && !search && !error;
    const emptyTitle = role === "employee" ? "Henüz personel carisi yok" : role === "customer" ? "Henüz müşteri yok" : role === "supplier" ? "Henüz tedarikçi yok" : !includeInactive ? "Gösterilecek cari yok" : "Henüz cari yok";
    const emptyHint = role || !includeInactive ? "Yeni bir cari kartı oluşturabilir veya pasifleri göstermeyi deneyebilirsiniz." : "İlk cari kartınızı oluşturarak başlayın.";

    return (
        <>
            <PageHeader title={title} subtitle="Cari kartlar, bakiyeler ve vadesi geçen tutarlar" />
            <DashboardCard action={canManage && role !== "employee" && !isEmpty ? newButton : undefined}>
                <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ mb: 2, alignItems: { sm: "center" } }}>
                    <TextField
                        size="small"
                        label="Ara"
                        placeholder="Kod veya ad"
                        value={searchInput}
                        onChange={(e) => setSearchInput(e.target.value)}
                        sx={{ minWidth: 260 }}
                    />
                    <FormControlLabel
                        control={<Switch checked={includeInactive} onChange={(e) => { setIncludeInactive(e.target.checked); setPage(0); }} />}
                        label="Pasifleri göster"
                    />
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : isEmpty ? (
                    <Box sx={{ py: 6, textAlign: "center" }}>
                        <Typography variant="h6" sx={{ mb: 1 }}>
                            {emptyTitle}
                        </Typography>
                        <Typography color="text.secondary" sx={{ mb: 2 }}>
                            {emptyHint}
                        </Typography>
                        {canManage && role !== "employee" && newButton}
                    </Box>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 720, "& td, & th": { px: 1, py: 1.5 } }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kod</TableCell>
                                        <TableCell>Ad</TableCell>
                                        <TableCell>Rol</TableCell>
                                        <TableCell align="right">Bakiye</TableCell>
                                        <TableCell align="right">Vadesi geçmiş</TableCell>
                                        <TableCell />
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((a) => (
                                        <TableRow key={a.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`/cari/${a.id}`)}>
                                            <TableCell>{a.code}</TableCell>
                                            <TableCell>
                                                <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                                    {a.name}
                                                </Typography>
                                            </TableCell>
                                            <TableCell>
                                                <Stack direction="row" spacing={0.5}>
                                                    {a.isCustomer && <Chip size="small" color="primary" variant="outlined" label="Müşteri" />}
                                                    {a.isSupplier && <Chip size="small" color="secondary" variant="outlined" label="Tedarikçi" />}
                                                </Stack>
                                            </TableCell>
                                            <TableCell align="right">{formatBalance(a.balance, a.currency)}</TableCell>
                                            <TableCell align="right">{a.overdueAmount > 0 ? formatMoney(a.overdueAmount, a.currency) : "—"}</TableCell>
                                            <TableCell>
                                                <Stack direction="row" spacing={0.5}>
                                                    {a.overLimit && <Chip size="small" color="error" label="Limit aşıldı" />}
                                                    {!a.isActive && <Chip size="small" label="Pasif" />}
                                                </Stack>
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                    {result.items.length === 0 && (
                                        <TableRow>
                                            <TableCell colSpan={6} align="center" sx={{ py: 4, color: "text.secondary" }}>
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
