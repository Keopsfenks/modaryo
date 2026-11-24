"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
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
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { ApiError } from "@/lib/api/client";
import { invoicing, KIND_LABELS, MODE_LABELS, STATUS_LABELS, type InvoiceKind, type InvoiceListResult, type InvoiceStatus } from "@/lib/api/invoicing";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

export type InvoiceGroup = "sales" | "purchase";

export const invoiceBase = (group: InvoiceGroup): string => (group === "sales" ? "/satis/faturalar" : "/satin-alma/faturalar");
export const groupKinds = (group: InvoiceGroup): InvoiceKind[] =>
    group === "sales" ? ["sales", "salesReturn", "exchangeDifference"] : ["purchase", "purchaseReturn"];

const STATUS_COLOR: Record<InvoiceStatus, "default" | "success" | "error"> = { draft: "default", issued: "success", cancelled: "error" };

/** Fatura listesi (satış ya da alış grubu): tür, durum, arama ve tarih süzgeçleri; toplamlar iptaller hariç TL. */
export default function InvoiceList({ group }: { group: InvoiceGroup }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("fatura.manage");
    const allowed = hasPermission("fatura.read") && (group === "sales" || hasPermission("fiyat.alis"));
    const kinds = groupKinds(group);

    const [kind, setKind] = useState<InvoiceKind>(kinds[0]);
    const [status, setStatus] = useState<InvoiceStatus | "">("");
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [from, setFrom] = useState("");
    const [to, setTo] = useState("");
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<InvoiceListResult | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        invoicing.list({ kind, status: status || undefined, search: search || undefined, from: from || undefined, to: to || undefined, page: page + 1, pageSize }).then(
            (r) => {
                if (cancelled) return;
                setError(null);
                setResult(r);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setResult((c) => c ?? { items: [], total: 0, totals: { netTl: 0, vatTl: 0, payableTl: 0 } });
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, kind, status, search, from, to, page, pageSize]);

    const title = group === "sales" ? "Faturalar" : "Alış faturaları";
    if (!allowed) return <Alert severity="warning">Faturaları görüntülemek için yetkiniz yok.</Alert>;

    const base = invoiceBase(group);
    const reset = () => setPage(0);

    return (
        <>
            <PageHeader title={title} subtitle="Taslak, kesilmiş ve iptal edilmiş faturalar" />
            <DashboardCard
                action={
                    canManage ? (
                        <Button component={Link} href={`${base}/yeni`} variant="contained">
                            Yeni fatura
                        </Button>
                    ) : undefined
                }
            >
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2 }}>
                    {kinds.length > 1 && (
                        <TextField select size="small" label="Tür" value={kind} onChange={(e) => { setKind(e.target.value as InvoiceKind); reset(); }} sx={{ minWidth: 190 }}>
                            {kinds.map((k) => (
                                <MenuItem key={k} value={k}>
                                    {KIND_LABELS[k]}
                                </MenuItem>
                            ))}
                        </TextField>
                    )}
                    <TextField select size="small" label="Durum" value={status} onChange={(e) => { setStatus(e.target.value as InvoiceStatus | ""); reset(); }} sx={{ minWidth: 150 }}>
                        <MenuItem value="">Tümü</MenuItem>
                        {(Object.keys(STATUS_LABELS) as InvoiceStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>
                                {STATUS_LABELS[s]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField size="small" label="Ara" placeholder="Numara veya cari" value={searchInput} onChange={(e) => { setSearchInput(e.target.value); reset(); }} sx={{ minWidth: 220 }} />
                    <TextField size="small" type="date" label="Başlangıç" value={from} onChange={(e) => { setFrom(e.target.value); reset(); }} slotProps={{ inputLabel: { shrink: true } }} />
                    <TextField size="small" type="date" label="Bitiş" value={to} onChange={(e) => { setTo(e.target.value); reset(); }} slotProps={{ inputLabel: { shrink: true } }} />
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : result.total === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek fatura yok.
                    </Typography>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 800 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Cari</TableCell>
                                        <TableCell>Durum</TableCell>
                                        <TableCell>Mod</TableCell>
                                        <TableCell align="right">Net</TableCell>
                                        <TableCell align="right">KDV</TableCell>
                                        <TableCell align="right">Ödenecek</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((i) => (
                                        <TableRow key={i.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`${base}/${i.id}`)}>
                                            <TableCell>{i.number ?? "(numarasız)"}{i.externalNumber ? ` · ${i.externalNumber}` : ""}</TableCell>
                                            <TableCell>{formatDate(i.date)}</TableCell>
                                            <TableCell>{i.currentAccountName}</TableCell>
                                            <TableCell>
                                                <Chip size="small" color={STATUS_COLOR[i.status]} variant={i.status === "draft" ? "outlined" : "filled"} label={STATUS_LABELS[i.status]} />
                                            </TableCell>
                                            <TableCell>{i.status === "draft" ? "" : MODE_LABELS[i.mode]}</TableCell>
                                            <TableCell align="right">{formatMoney(i.net, i.currency as Currency)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.vat, i.currency as Currency)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.payable, i.currency as Currency)}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                            Toplam (iptaller hariç, TL): net {formatMoney(result.totals.netTl, "TRY")} · KDV {formatMoney(result.totals.vatTl, "TRY")} · ödenecek {formatMoney(result.totals.payableTl, "TRY")}
                        </Typography>
                        <TablePagination
                            component="div"
                            count={result.total}
                            page={page}
                            rowsPerPage={pageSize}
                            rowsPerPageOptions={[25, 50, 100]}
                            onPageChange={(_, p) => setPage(p)}
                            onRowsPerPageChange={(e) => {
                                setPageSize(Number(e.target.value));
                                setPage(0);
                            }}
                            labelRowsPerPage="Sayfa başına"
                        />
                    </>
                )}
            </DashboardCard>
        </>
    );
}
