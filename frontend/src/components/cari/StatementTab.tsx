import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { erp, type CurrentAccountDto, type LedgerEntryType, type StatementResult } from "@/lib/api/erp";
import { formatDate, todayIso } from "@/lib/date";
import { formatBalance, formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const TYPE_LABELS: Record<LedgerEntryType, string> = {
    opening: "Açılış",
    debitNote: "Borç dekontu",
    creditNote: "Alacak dekontu",
};

const yearStartIso = () => `${new Date().getFullYear()}-01-01`;

/** Ekstre: tarih aralığı, devreden bakiye, hareketler (eskiden yeniye) ve toplam satırı. Yazdırılabilir. */
export default function StatementTab({ account }: { account: CurrentAccountDto }) {
    const { session } = useSession();
    const [range, setRange] = useState(() => ({ from: yearStartIso(), to: todayIso() }));
    const [applied, setApplied] = useState(range);
    const [result, setResult] = useState<StatementResult | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);

    useEffect(() => {
        let ignore = false;
        erp.statement(account.id, applied.from, applied.to).then(
            (r) => {
                if (ignore) return;
                setResult(r);
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
    }, [account.id, applied]);

    const currency = account.currency;
    const bold = { fontWeight: 600 };

    return (
        <DashboardCard title="Ekstre">
            <Box
                sx={{
                    display: "none",
                    mb: 2,
                    "@media print": { display: "block" },
                }}
            >
                <Typography variant="h5">{session.activeCompany?.name}</Typography>
                <Typography>
                    {account.code} - {account.name}
                </Typography>
                <Typography>
                    {formatDate(applied.from)} - {formatDate(applied.to)}
                </Typography>
            </Box>

            <Box className="no-print" sx={{ display: "flex", flexWrap: "wrap", gap: 2, alignItems: "center", mb: 2 }}>
                <TextField
                    label="Başlangıç"
                    type="date"
                    size="small"
                    value={range.from}
                    onChange={(e) => setRange({ ...range, from: e.target.value })}
                    slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                    label="Bitiş"
                    type="date"
                    size="small"
                    value={range.to}
                    onChange={(e) => setRange({ ...range, to: e.target.value })}
                    slotProps={{ inputLabel: { shrink: true } }}
                />
                <Button variant="contained" disabled={!range.from || !range.to} onClick={() => setApplied(range)}>
                    Uygula
                </Button>
                <Button variant="outlined" onClick={() => window.print()}>
                    Yazdır
                </Button>
            </Box>

            <FormAlert error={loadError} />
            {result && (
                <TableContainer>
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Tarih</TableCell>
                                <TableCell>Tür</TableCell>
                                <TableCell>Belge no</TableCell>
                                <TableCell>Açıklama</TableCell>
                                <TableCell align="right">Borç</TableCell>
                                <TableCell align="right">Alacak</TableCell>
                                <TableCell align="right">Bakiye</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            <TableRow>
                                <TableCell colSpan={6} sx={bold}>
                                    Devreden bakiye
                                </TableCell>
                                <TableCell align="right" sx={bold}>
                                    {formatBalance(result.openingBalance, currency)}
                                </TableCell>
                            </TableRow>
                            {result.rows.map((r) => {
                                const struck = r.cancelled ? { textDecoration: "line-through" } : undefined;
                                const description = (
                                    <Box component="span" sx={struck}>
                                        {r.description}
                                    </Box>
                                );
                                return (
                                    <TableRow key={r.id}>
                                        <TableCell sx={struck}>{formatDate(r.date)}</TableCell>
                                        <TableCell sx={struck}>{TYPE_LABELS[r.type]}</TableCell>
                                        <TableCell sx={struck}>{r.documentNo}</TableCell>
                                        <TableCell>
                                            {r.cancelled ? (
                                                <Tooltip title={`İptal nedeni: ${r.cancelReason ?? ""}`}>{description}</Tooltip>
                                            ) : (
                                                description
                                            )}
                                        </TableCell>
                                        <TableCell align="right" sx={struck}>
                                            {r.debit > 0 ? formatMoney(r.debit, currency) : ""}
                                        </TableCell>
                                        <TableCell align="right" sx={struck}>
                                            {r.credit > 0 ? formatMoney(r.credit, currency) : ""}
                                        </TableCell>
                                        <TableCell align="right" sx={struck}>
                                            {formatBalance(r.runningBalance, currency)}
                                        </TableCell>
                                    </TableRow>
                                );
                            })}
                            <TableRow>
                                <TableCell colSpan={4} sx={bold}>
                                    Toplam
                                </TableCell>
                                <TableCell align="right" sx={bold}>
                                    {formatMoney(result.totalDebit, currency)}
                                </TableCell>
                                <TableCell align="right" sx={bold}>
                                    {formatMoney(result.totalCredit, currency)}
                                </TableCell>
                                <TableCell align="right" sx={bold}>
                                    {formatBalance(result.closingBalance, currency)}
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </TableContainer>
            )}
        </DashboardCard>
    );
}
