"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { INTERVAL_LABELS, RECURRING_MODE_LABELS, sales, type RecurringRow } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { RECURRING_BASE } from "./RecurringEditor";

/** Tekrarlayan fatura şablonları (abonelikler). */
export default function RecurringList() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.recurring");
    const [rows, setRows] = useState<RecurringRow[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        sales.recurring.list().then(
            (r) => !cancelled && setRows(r),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setRows([]);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed]);

    if (!allowed) return <Alert severity="warning">Abonelikleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Abonelikler" subtitle="Tekrarlayan satış faturası şablonları" />
            <DashboardCard
                action={
                    <Button component={Link} href={`${RECURRING_BASE}/yeni`} variant="contained">
                        Yeni abonelik
                    </Button>
                }
            >
                <FormAlert error={error} />
                {rows === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : rows.length === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Henüz abonelik yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 820 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Numara</TableCell>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Müşteri</TableCell>
                                    <TableCell>Dönem</TableCell>
                                    <TableCell>Şekil</TableCell>
                                    <TableCell>Sonraki</TableCell>
                                    <TableCell>Durum</TableCell>
                                    <TableCell align="right">Tutar</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((r) => (
                                    <TableRow key={r.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`${RECURRING_BASE}/${r.id}`)}>
                                        <TableCell>{r.number}</TableCell>
                                        <TableCell>{r.name}</TableCell>
                                        <TableCell>{r.currentAccountName}</TableCell>
                                        <TableCell>
                                            {INTERVAL_LABELS[r.interval]} · her ayın {r.dayOfMonth}. günü
                                        </TableCell>
                                        <TableCell>{RECURRING_MODE_LABELS[r.mode]}</TableCell>
                                        <TableCell>{r.nextPeriod ? formatDate(r.nextPeriod) : "—"}</TableCell>
                                        <TableCell>
                                            <Chip size="small" color={r.isActive ? "success" : "default"} label={r.isActive ? "Aktif" : "Duraklatıldı"} />
                                        </TableCell>
                                        <TableCell align="right">{formatMoney(r.total, r.currency as Currency)}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>
        </>
    );
}
