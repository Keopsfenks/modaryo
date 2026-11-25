"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
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
import { ALLOCATION_KEY_LABELS, LANDED_STATUS_LABELS, purchasing, type LandedCostRow, type LandedCostStatus } from "@/lib/api/purchasing";
import { formatDate } from "@/lib/date";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

/** Masraf dağıtımı listesi (satinalma.read + fiyat.alis). */
export default function LandedCostList() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satinalma.read") && hasPermission("fiyat.alis");
    const [status, setStatus] = useState<LandedCostStatus | "">("");
    const [rows, setRows] = useState<LandedCostRow[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        purchasing.landedCosts.list({ status: status || undefined }).then(
            (r) => {
                if (cancelled) return;
                setError(null);
                setRows(r);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setRows((c) => c ?? []);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, status]);

    if (!allowed) return <Alert severity="warning">Masraf dağıtımlarını görüntülemek için satın alma ve alış fiyatı yetkisi gerekir.</Alert>;

    return (
        <>
            <PageHeader title="Masraf dağıtımı" subtitle="Navlun, gümrük ve benzeri masrafların mal kabul maliyetine dağıtımı" />
            <DashboardCard
                action={
                    hasPermission("satinalma.manage") ? (
                        <Button component={Link} href="/satin-alma/masraf-dagitimi/yeni" variant="contained">
                            Yeni masraf dağıtımı
                        </Button>
                    ) : undefined
                }
            >
                <TextField select size="small" label="Durum" value={status} onChange={(e) => setStatus(e.target.value as LandedCostStatus | "")} sx={{ minWidth: 160, mb: 2 }}>
                    <MenuItem value="">Tümü</MenuItem>
                    {(Object.keys(LANDED_STATUS_LABELS) as LandedCostStatus[]).map((s) => (
                        <MenuItem key={s} value={s}>
                            {LANDED_STATUS_LABELS[s]}
                        </MenuItem>
                    ))}
                </TextField>
                <FormAlert error={error} />
                {rows === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : rows.length === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek masraf dağıtımı yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 560 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Numara</TableCell>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Anahtar</TableCell>
                                    <TableCell>Durum</TableCell>
                                    <TableCell align="right">Tutar</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((r) => (
                                    <TableRow key={r.id} hover>
                                        <TableCell>
                                            <Typography component={Link} href={`/satin-alma/masraf-dagitimi/${r.id}`} sx={{ color: "primary.main", fontWeight: 600 }}>
                                                {r.number}
                                            </Typography>
                                        </TableCell>
                                        <TableCell>{formatDate(r.date)}</TableCell>
                                        <TableCell>{ALLOCATION_KEY_LABELS[r.key]}</TableCell>
                                        <TableCell>
                                            <Chip size="small" label={LANDED_STATUS_LABELS[r.status]} color={r.status === "posted" ? "success" : r.status === "cancelled" ? "default" : "warning"} />
                                        </TableCell>
                                        <TableCell align="right">{formatMoney(r.totalTl, "TRY")}</TableCell>
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
