"use client";

import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableFooter from "@mui/material/TableFooter";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import { formatQuantity } from "@/lib/api/inventory";
import { COST_TYPE_LABELS, type CostType, type LandedAllocationDto } from "@/lib/api/purchasing";
import { formatMoney } from "@/lib/money";

type AllocationPreviewProps = { allocations: LandedAllocationDto[]; totalTl: number };

const cents = (n: number) => Math.round(n * 100);
const costLabel = (type: string) => COST_TYPE_LABELS[type as CostType] ?? type;

/** Mal kabul satırı başına TL dağıtım tablosu. Dağıtılan toplam masraf toplamına kuruşu kuruşuna eşit olmalıdır; değilse uyarır. */
export default function AllocationPreview({ allocations, totalTl }: AllocationPreviewProps) {
    const types = [...new Set(allocations.flatMap((a) => Object.keys(a.byCostType)))];
    const sum = allocations.reduce((s, a) => s + cents(a.allocatedTl), 0) / 100;
    const valueSum = allocations.reduce((s, a) => s + cents(a.tlValue), 0) / 100;
    const matches = cents(sum) === cents(totalTl);

    return (
        <Box>
            {!matches && (
                <Alert severity="error" sx={{ mb: 2 }}>
                    Dağıtım toplamı masraf toplamına eşit değil ({formatMoney(sum, "TRY")} / {formatMoney(totalTl, "TRY")}).
                </Alert>
            )}
            <TableContainer>
                <Table size="small" sx={{ minWidth: 720 }}>
                    <TableHead>
                        <TableRow>
                            <TableCell>Mal kabul</TableCell>
                            <TableCell>Kalem</TableCell>
                            <TableCell align="right">Miktar</TableCell>
                            <TableCell align="right">Değer</TableCell>
                            {types.length > 1 && types.map((t) => <TableCell key={t} align="right">{costLabel(t)}</TableCell>)}
                            <TableCell align="right">Dağıtılan</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {allocations.map((a) => (
                            <TableRow key={a.receiptLineId}>
                                <TableCell>{a.receiptNumber}</TableCell>
                                <TableCell>
                                    {a.productCode} · {a.productName}
                                </TableCell>
                                <TableCell align="right">{formatQuantity(a.baseQuantity)}</TableCell>
                                <TableCell align="right">{formatMoney(a.tlValue, "TRY")}</TableCell>
                                {types.length > 1 && types.map((t) => <TableCell key={t} align="right">{formatMoney(a.byCostType[t] ?? 0, "TRY")}</TableCell>)}
                                <TableCell align="right">{formatMoney(a.allocatedTl, "TRY")}</TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                    <TableFooter>
                        <TableRow>
                            <TableCell colSpan={3}>
                                <Typography variant="subtitle2">Dağıtılan toplam</Typography>
                            </TableCell>
                            <TableCell align="right">{formatMoney(valueSum, "TRY")}</TableCell>
                            {types.length > 1 && types.map((t) => <TableCell key={t} />)}
                            <TableCell align="right">
                                <Box data-testid="allocation-total" sx={{ fontWeight: 700 }}>
                                    <span>{formatMoney(sum, "TRY")}</span>
                                </Box>
                            </TableCell>
                        </TableRow>
                    </TableFooter>
                </Table>
            </TableContainer>
        </Box>
    );
}
