"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Grid from "@mui/material/Grid";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { erp, type Currency, type DueBucket, type DueSummary } from "@/lib/api/erp";
import { formatMoney } from "@/lib/money";

function BucketCard({ title, bucket, currency, color }: { title: string; bucket: DueBucket; currency: Currency; color: string }) {
    const rows: [string, number, string?][] = [
        ["Vadesi geçmiş", bucket.overdue, bucket.overdue > 0 ? "error.main" : undefined],
        ["30 gün içinde", bucket.within30],
        ["Daha sonra", bucket.later],
    ];
    return (
        <DashboardCard title={title}>
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1, borderLeft: 4, borderColor: color, pl: 2 }}>
                {rows.map(([label, amount, tone]) => (
                    <Box key={label} sx={{ display: "flex", justifyContent: "space-between", gap: 2 }}>
                        <Typography color="text.secondary">{label}</Typography>
                        <Typography sx={{ fontWeight: 600, color: tone }}>{formatMoney(amount, currency)}</Typography>
                    </Box>
                ))}
            </Box>
        </DashboardCard>
    );
}

/** Firma geneli vade özeti: para birimi başına tahsil/ödeme kartları ve en çok geciken cariler. */
export default function DueOverview() {
    const router = useRouter();
    const [summary, setSummary] = useState<DueSummary | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);

    useEffect(() => {
        let ignore = false;
        erp.dueSummary().then(
            (result) => {
                if (ignore) return;
                setSummary(result);
                setLoadError(null);
            },
            (e) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setLoadError(e);
            },
        );
        return () => {
            ignore = true;
        };
    }, []);

    const empty = summary && summary.byCurrency.length === 0 && summary.topOverdue.length === 0;

    return (
        <Box sx={{ display: "flex", flexDirection: "column", gap: 3 }}>
            <Typography variant="h4" component="h1">
                Vade Takibi
            </Typography>
            <FormAlert error={loadError} />
            {empty && <Typography color="text.secondary">Açık vadeli kalem yok</Typography>}

            {summary?.byCurrency.map((c) => (
                <Box key={c.currency} role="group" aria-label={c.currency} sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
                    <Typography variant="h6" component="h2">
                        {c.currency}
                    </Typography>
                    <Grid container spacing={2}>
                        <Grid size={{ xs: 12, md: 6 }}>
                            <BucketCard title="Tahsil edilecek" bucket={c.receivable} currency={c.currency} color="success.main" />
                        </Grid>
                        <Grid size={{ xs: 12, md: 6 }}>
                            <BucketCard title="Ödenecek" bucket={c.payable} currency={c.currency} color="warning.main" />
                        </Grid>
                    </Grid>
                </Box>
            ))}

            {summary && summary.topOverdue.length > 0 && (
                <DashboardCard title="En çok geciken cariler">
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kod</TableCell>
                                    <TableCell>Cari</TableCell>
                                    <TableCell align="right">Vadesi geçmiş alacak</TableCell>
                                    <TableCell align="right">En çok gecikme (gün)</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {summary.topOverdue.map((a) => (
                                    <TableRow key={a.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`/cari/${a.id}?sekme=vade`)}>
                                        <TableCell>{a.code}</TableCell>
                                        <TableCell>{a.name}</TableCell>
                                        <TableCell align="right">{formatMoney(a.overdueReceivable, a.currency)}</TableCell>
                                        <TableCell align="right">{a.maxDaysOverdue}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                </DashboardCard>
            )}
        </Box>
    );
}
