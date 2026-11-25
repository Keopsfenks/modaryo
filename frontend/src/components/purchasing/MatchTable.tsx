"use client";

import Chip from "@mui/material/Chip";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import type { MatchRow, PoLineDto } from "@/lib/api/purchasing";
import { formatMoney, type Currency } from "@/lib/money";

type MatchTableProps = {
    rows: MatchRow[];
    lines: PoLineDto[];
    currency: string;
    /** fiyat.alis yoksa fiyat sütunları gösterilmez. */
    showPrices: boolean;
};

const money = (value: number | null, currency: string) => (value === null ? "—" : formatMoney(value, currency as Currency));
const qty = (value: number) => value.toLocaleString("tr-TR", { maximumFractionDigits: 4 });

/** Üçlü karşılaştırma: sipariş, mal kabul ve fatura miktarı/fiyatı. Miktar uyuşmazlığı ve fiyat farkı satırları vurgulanır. */
export default function MatchTable({ rows, lines, currency, showPrices }: MatchTableProps) {
    if (rows.length === 0) {
        return (
            <Typography color="text.secondary" sx={{ py: 3, textAlign: "center" }}>
                Karşılaştırılacak satır yok.
            </Typography>
        );
    }

    return (
        <TableContainer>
            <Table size="small" sx={{ minWidth: 720 }}>
                <TableHead>
                    <TableRow>
                        <TableCell>Kalem</TableCell>
                        <TableCell align="right">Sipariş</TableCell>
                        <TableCell align="right">Mal kabul</TableCell>
                        <TableCell align="right">Fatura</TableCell>
                        {showPrices && <TableCell align="right">Sipariş fiyatı</TableCell>}
                        {showPrices && <TableCell align="right">Kabul fiyatı</TableCell>}
                        {showPrices && <TableCell align="right">Fatura fiyatı</TableCell>}
                        <TableCell>Durum</TableCell>
                    </TableRow>
                </TableHead>
                <TableBody>
                    {rows.map((row) => {
                        const line = lines.find((l) => l.id === row.poLineId);
                        const flagged = row.qtyMismatch || row.priceVariance;
                        return (
                            <TableRow key={row.poLineId} data-flagged={flagged ? "true" : "false"} sx={flagged ? { bgcolor: "warning.light" } : undefined}>
                                <TableCell>{line ? `${line.productCode} · ${line.productName}` : row.poLineId}</TableCell>
                                <TableCell align="right">{qty(row.ordered)}</TableCell>
                                <TableCell align="right">{qty(row.received)}</TableCell>
                                <TableCell align="right">{qty(row.invoiced)}</TableCell>
                                {showPrices && <TableCell align="right">{money(row.poPrice, currency)}</TableCell>}
                                {showPrices && <TableCell align="right">{money(row.receiptPrice, currency)}</TableCell>}
                                {showPrices && <TableCell align="right">{money(row.invoicePrice, currency)}</TableCell>}
                                <TableCell>
                                    {row.qtyMismatch && <Chip size="small" color="warning" label="Miktar farkı" sx={{ mr: 0.5 }} />}
                                    {row.priceVariance && <Chip size="small" color="error" label="Fiyat farkı" />}
                                    {!flagged && <Chip size="small" color="success" variant="outlined" label="Uyumlu" />}
                                </TableCell>
                            </TableRow>
                        );
                    })}
                </TableBody>
            </Table>
        </TableContainer>
    );
}
