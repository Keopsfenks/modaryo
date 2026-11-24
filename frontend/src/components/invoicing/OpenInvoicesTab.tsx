"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
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
import { erp, type CurrentAccountDto } from "@/lib/api/erp";
import { formatDate } from "@/lib/date";
import { formatMoney } from "@/lib/money";

type Row = { entryId: string; invoiceId: string; number: string; date: string; dueDate: string; remaining: number; overdue: boolean };

/** Carinin kesilmiş faturalarından açık bakiyesi kalanlar: ekstredeki fatura hareketleri ile açık vade kalemlerinin birleşimi. */
export default function OpenInvoicesTab({ account }: { account: CurrentAccountDto }) {
    const [rows, setRows] = useState<Row[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        let ignore = false;
        Promise.all([erp.statement(account.id), erp.dueItems(account.id)]).then(
            ([statement, items]) => {
                if (ignore) return;
                const invoices = new Map(
                    statement.rows.filter((r) => !r.cancelled && r.source === "invoice" && r.sourceId).map((r) => [r.id, r]),
                );
                const byEntry = new Map<string, Row>();
                for (const item of items) {
                    const entry = invoices.get(item.entryId);
                    if (!entry || !entry.sourceId) continue;
                    const existing = byEntry.get(item.entryId);
                    if (existing) {
                        existing.remaining += item.remaining;
                        if (item.dueDate < existing.dueDate) existing.dueDate = item.dueDate;
                        existing.overdue = existing.overdue || item.daysOverdue > 0;
                    } else {
                        byEntry.set(item.entryId, {
                            entryId: item.entryId,
                            invoiceId: entry.sourceId,
                            number: entry.documentNo ?? "-",
                            date: entry.date,
                            dueDate: item.dueDate,
                            remaining: item.remaining,
                            overdue: item.daysOverdue > 0,
                        });
                    }
                }
                setRows([...byEntry.values()].sort((a, b) => a.dueDate.localeCompare(b.dueDate)));
                setError(null);
            },
            (e: unknown) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            ignore = true;
        };
    }, [account.id]);

    const href = (r: Row) => `${account.isSupplier && !account.isCustomer ? "/satin-alma" : "/satis"}/faturalar/${r.invoiceId}`;

    return (
        <DashboardCard title="Açık faturalar">
            <FormAlert error={error} />
            {rows && rows.length === 0 ? (
                <Typography color="text.secondary">Açık bakiyesi olan fatura yok.</Typography>
            ) : (
                rows && (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Fatura</TableCell>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Vade</TableCell>
                                    <TableCell align="right">Açık tutar</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((r) => (
                                    <TableRow key={r.entryId} sx={r.overdue ? { "& td": { color: "error.main" } } : undefined}>
                                        <TableCell>
                                            <Link href={href(r)}>{r.number}</Link>
                                        </TableCell>
                                        <TableCell>{formatDate(r.date)}</TableCell>
                                        <TableCell>{formatDate(r.dueDate)}</TableCell>
                                        <TableCell align="right">{formatMoney(r.remaining, account.currency)}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )
            )}
        </DashboardCard>
    );
}
