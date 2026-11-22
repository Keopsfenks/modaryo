import { useEffect, useState } from "react";
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
import { erp, type CurrentAccountDto, type OpenItem } from "@/lib/api/erp";
import { formatDate } from "@/lib/date";
import { formatMoney } from "@/lib/money";

const DIRECTION_LABELS = { receivable: "Tahsil edilecek", payable: "Ödenecek" } as const;

/** Carinin açık (kapanmamış) kalemleri; vadesi geçenler kırmızı. */
export default function DueTab({ account }: { account: CurrentAccountDto }) {
    const [items, setItems] = useState<OpenItem[] | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);

    useEffect(() => {
        let ignore = false;
        erp.dueItems(account.id).then(
            (result) => {
                if (ignore) return;
                setItems(result);
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
    }, [account.id]);

    return (
        <DashboardCard title="Açık kalemler">
            <FormAlert error={loadError} />
            {items && items.length === 0 ? (
                <Typography color="text.secondary">Açık kalem yok</Typography>
            ) : (
                items && (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Yön</TableCell>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Vade</TableCell>
                                    <TableCell align="right">Kalan</TableCell>
                                    <TableCell align="right">Gecikme günü</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {items.map((i) => {
                                    const overdue = i.daysOverdue > 0;
                                    return (
                                        <TableRow key={i.entryId} data-overdue={overdue} sx={overdue ? { "& td": { color: "error.main" } } : undefined}>
                                            <TableCell>{DIRECTION_LABELS[i.direction]}</TableCell>
                                            <TableCell>{formatDate(i.date)}</TableCell>
                                            <TableCell>{formatDate(i.dueDate)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.remaining, account.currency)}</TableCell>
                                            <TableCell align="right">{overdue ? i.daysOverdue : ""}</TableCell>
                                        </TableRow>
                                    );
                                })}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )
            )}
        </DashboardCard>
    );
}
