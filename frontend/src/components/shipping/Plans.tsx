"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
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
import { PLAN_STATUS_LABELS, shipping, type PlanRow, type PlanStatus } from "@/lib/api/shipping";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";

/** Teslimat planları: tarih ve durum süzgeçli liste; satır plan düzenleyicisine gider. */
export default function Plans() {
    const router = useRouter();
    const { hasPermission, hasModule } = useSession();
    const allowed = hasPermission("sevkiyat.read") && hasModule("teslimat-plani");
    const [date, setDate] = useState("");
    const [status, setStatus] = useState<PlanStatus | "">("");
    const [rows, setRows] = useState<PlanRow[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        shipping.plans.list({ date: date || undefined, status: status || undefined }).then(
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
    }, [allowed, date, status]);

    if (!allowed) return <Alert severity="warning">Teslimat planlarını görüntülemek için yetkiniz yok ya da modül firmanızda kapalı.</Alert>;

    return (
        <>
            <PageHeader title="Teslimat planları" subtitle="Kendi aracımızla yapılacak teslimat rotaları" />
            <DashboardCard>
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2 }}>
                    <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    <TextField
                        select
                        size="small"
                        label="Durum"
                        value={status}
                        onChange={(e) => setStatus(e.target.value as PlanStatus | "")}
                        sx={{ minWidth: 180 }}
                        slotProps={{ select: { native: true }, htmlInput: { "aria-label": "Durum" }, inputLabel: { shrink: true } }}
                    >
                        <option value="">Tümü</option>
                        {(Object.keys(PLAN_STATUS_LABELS) as PlanStatus[]).map((s) => (
                            <option key={s} value={s}>
                                {PLAN_STATUS_LABELS[s]}
                            </option>
                        ))}
                    </TextField>
                    <Box sx={{ flexGrow: 1 }} />
                    {hasPermission("sevkiyat.plan") && (
                        <Button variant="contained" onClick={() => router.push("/sevkiyat/planlar/yeni")}>
                            Yeni plan
                        </Button>
                    )}
                </Stack>
                <FormAlert error={error} />
                {rows === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : rows.length === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek plan yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 640 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Numara</TableCell>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Araç</TableCell>
                                    <TableCell>Sürücü</TableCell>
                                    <TableCell>Durum</TableCell>
                                    <TableCell align="right">Teslim / Durak</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((p) => (
                                    <TableRow key={p.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`/sevkiyat/planlar/${p.id}`)}>
                                        <TableCell>{p.number}</TableCell>
                                        <TableCell>{formatDate(p.date)}</TableCell>
                                        <TableCell>{p.vehicle ?? "—"}</TableCell>
                                        <TableCell>{p.driverName ?? "—"}</TableCell>
                                        <TableCell>
                                            <Chip size="small" color={p.status === "completed" ? "success" : p.status === "cancelled" ? "error" : "info"} label={PLAN_STATUS_LABELS[p.status]} />
                                        </TableCell>
                                        <TableCell align="right">{`${p.doneCount} / ${p.stopCount}`}</TableCell>
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
