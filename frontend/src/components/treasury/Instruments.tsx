"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Tab from "@mui/material/Tab";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Tabs from "@mui/material/Tabs";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { treasury, type InstrumentDirection, type InstrumentDto, type InstrumentStatus, type PortfolioRow } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { KIND_LABELS, STATUS_LABELS } from "@/lib/instruments";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import InstrumentForm from "./InstrumentForm";

export const MODULE_KEY = "cek-senet";

/** Çek/senet portföyü: yöne ve duruma göre liste, portföy özeti ve giriş. cek-senet modülü kapalıysa sayfa içeriği gösterilmez. */
export default function Instruments() {
    const { hasPermission, hasModule } = useSession();
    const enabled = hasModule(MODULE_KEY);
    const canRead = hasPermission("finans.read");
    const canManage = hasPermission("ceksenet.manage");

    const [direction, setDirection] = useState<InstrumentDirection>("received");
    const [status, setStatus] = useState<InstrumentStatus | "">("");
    const [rows, setRows] = useState<InstrumentDto[] | null>(null);
    const [portfolio, setPortfolio] = useState<PortfolioRow[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [creating, setCreating] = useState(false);

    const load = useCallback(async () => {
        try {
            const [list, summary] = await Promise.all([treasury.instruments({ direction, status: status || undefined }), treasury.portfolio()]);
            setRows(list);
            setPortfolio(summary);
            setError(null);
        } catch (e) {
            setError(errorMessage(e));
            setRows((r) => r ?? []);
        }
    }, [direction, status]);

    useEffect(() => {
        if (!enabled || !canRead) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- filtre değişince yeniden yükleme
        void load();
    }, [enabled, canRead, load]);

    if (!enabled) return <Alert severity="info">Çek/senet modülü firmanızda kapalı. Ayarlar &gt; Modüller bölümünden açabilirsiniz.</Alert>;
    if (!canRead) return <Alert severity="warning">Çek ve senetleri görmek için yetkiniz yok.</Alert>;

    const summary = portfolio.filter((p) => p.direction === direction);
    const statuses = (Object.keys(STATUS_LABELS) as InstrumentStatus[]).filter((s) => (direction === "received" ? s !== "issued" && s !== "paid" && s !== "returnedFromSupplier" : s === "issued" || s === "paid" || s === "returnedFromSupplier"));

    return (
        <>
            <PageHeader title="Çek / Senet" subtitle="Müşteriden alınan ve tedarikçiye verilen çek/senetler; durum geçişleri cari ve kasa/banka hareketini otomatik yazar." />
            <Stack spacing={3}>
                <Tabs
                    value={direction}
                    onChange={(_, v: InstrumentDirection) => {
                        setDirection(v);
                        setStatus("");
                        setRows(null);
                    }}
                    aria-label="Yön"
                >
                    <Tab value="received" label="Alınan (müşteri)" />
                    <Tab value="issued" label="Verilen (firma)" />
                </Tabs>
                {summary.length > 0 && (
                    <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1 }}>
                        {summary.map((p) => (
                            <Chip key={`${p.status}-${p.currency}`} variant="outlined" label={`${STATUS_LABELS[p.status]}: ${p.count} adet · ${formatMoney(p.total, p.currency)}`} />
                        ))}
                    </Stack>
                )}
                <DashboardCard
                    action={
                        <Stack direction="row" spacing={2}>
                            <TextField select size="small" label="Durum" value={status} onChange={(e) => setStatus(e.target.value as InstrumentStatus | "")} sx={{ minWidth: 180 }}>
                                <MenuItem value="">Tümü</MenuItem>
                                {statuses.map((s) => (
                                    <MenuItem key={s} value={s}>
                                        {STATUS_LABELS[s]}
                                    </MenuItem>
                                ))}
                            </TextField>
                            {canManage && (
                                <Button variant="contained" onClick={() => setCreating(true)}>
                                    {direction === "received" ? "Çek/senet al" : "Çek/senet ver"}
                                </Button>
                            )}
                        </Stack>
                    }
                >
                    <FormAlert error={error} />
                    {rows === null ? (
                        <Skeleton variant="rounded" height={160} />
                    ) : rows.length === 0 ? (
                        <Typography color="text.secondary" sx={{ py: 3 }}>
                            Kayıt yok.
                        </Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tür</TableCell>
                                        <TableCell>Cari</TableCell>
                                        <TableCell>Vade</TableCell>
                                        <TableCell align="right">Tutar</TableCell>
                                        <TableCell>Durum</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {rows.map((i) => (
                                        <TableRow key={i.id} hover>
                                            <TableCell>
                                                <Link href={`/finans/cek-senet/${i.id}`}>{i.number}</Link>
                                            </TableCell>
                                            <TableCell>{KIND_LABELS[i.kind]}</TableCell>
                                            <TableCell>{i.currentAccountName}</TableCell>
                                            <TableCell>{formatDate(i.dueDate)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.amount, i.currency)}</TableCell>
                                            <TableCell>
                                                {STATUS_LABELS[i.status]}
                                                {i.protestFlag && <Chip size="small" color="error" label="Protesto" sx={{ ml: 1 }} />}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                </DashboardCard>
            </Stack>
            {creating && (
                <InstrumentForm
                    open
                    direction={direction}
                    onClose={() => setCreating(false)}
                    onSaved={() => {
                        setCreating(false);
                        void load();
                    }}
                />
            )}
        </>
    );
}
