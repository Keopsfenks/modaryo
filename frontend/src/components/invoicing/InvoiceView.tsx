"use client";

import { useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { invoicing, isPurchaseKind, KIND_LABELS, MODE_LABELS, STATUS_LABELS, type InvoiceDto } from "@/lib/api/invoicing";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import CancelDialog from "./CancelDialog";
import EmailDialog from "./EmailDialog";
import PdfPreview from "./PdfPreview";
import TotalsPanel from "./TotalsPanel";

type InvoiceViewProps = { invoice: InvoiceDto; onChanged(): void; emailDefault?: string };

/** Taslak olmayan (ya da düzenleme yetkisi bulunmayan) faturanın salt okunur görünümü ve PDF / e-posta / iptal işlemleri. */
export default function InvoiceView({ invoice, onChanged, emailDefault = "" }: InvoiceViewProps) {
    const { hasPermission } = useSession();
    const [showPdf, setShowPdf] = useState(false);
    const [cancelOpen, setCancelOpen] = useState(false);
    const [emailOpen, setEmailOpen] = useState(false);
    const [sent, setSent] = useState(false);
    const currency = invoice.currency as Currency;
    const issued = invoice.status === "issued";
    const cancelAllowed = hasPermission("fatura.cancel") && issued && (!isPurchaseKind(invoice.kind) || hasPermission("fiyat.alis"));
    const emailAllowed = hasPermission("fatura.issue") && issued;
    const title = invoice.number ?? `${KIND_LABELS[invoice.kind]} (${STATUS_LABELS[invoice.status].toLowerCase()})`;

    return (
        <>
            <PageHeader title={title} subtitle={`${KIND_LABELS[invoice.kind]} · ${invoice.currentAccountName}`} />
            <Stack spacing={3}>
                {invoice.status === "cancelled" && <Alert severity="error">Bu fatura iptal edildi{invoice.cancelReason ? `: ${invoice.cancelReason}` : "."}</Alert>}
                {sent && <Alert severity="success">Fatura e-posta ile gönderildi.</Alert>}
                <DashboardCard>
                    <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap", gap: 1 }}>
                        <Chip label={STATUS_LABELS[invoice.status]} color={invoice.status === "issued" ? "success" : invoice.status === "cancelled" ? "error" : "default"} />
                        {invoice.status !== "draft" && <Chip variant="outlined" label={MODE_LABELS[invoice.mode]} />}
                        <Chip variant="outlined" label={formatDate(invoice.date)} />
                        <Chip variant="outlined" label={invoice.currency === "TRY" ? "TRY" : `${invoice.currency} · kur ${String(invoice.rate).replace(".", ",")}`} />
                        {invoice.externalNumber && <Chip variant="outlined" label={`Tedarikçi no: ${invoice.externalNumber}`} />}
                    </Stack>
                    <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                        <Button variant="outlined" onClick={() => setShowPdf((v) => !v)}>
                            {showPdf ? "PDF'i gizle" : "PDF önizle"}
                        </Button>
                        <Button component={Link} href={`/cari/${invoice.currentAccountId}`} variant="text">
                            Cari kartı
                        </Button>
                        {emailAllowed && (
                            <Button variant="outlined" onClick={() => setEmailOpen(true)}>
                                E-posta gönder
                            </Button>
                        )}
                        {cancelAllowed && (
                            <Button color="error" variant="outlined" onClick={() => setCancelOpen(true)}>
                                Faturayı iptal et
                            </Button>
                        )}
                    </Stack>
                    {showPdf && (
                        <div style={{ marginTop: 16 }}>
                            <PdfPreview title="Fatura PDF" load={() => invoicing.pdf(invoice.id)} refreshKey={invoice.version} />
                        </div>
                    )}
                </DashboardCard>

                <DashboardCard title="Satırlar">
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 700 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kalem</TableCell>
                                    <TableCell align="right">Miktar</TableCell>
                                    <TableCell align="right">Birim fiyat</TableCell>
                                    <TableCell align="right">KDV %</TableCell>
                                    <TableCell align="right">Net</TableCell>
                                    <TableCell align="right">Toplam</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {invoice.lines.map((l) => (
                                    <TableRow key={l.id}>
                                        <TableCell>
                                            {l.description}
                                            {l.exemptionCode && <Typography variant="caption" component="div">İstisna {l.exemptionCode}</Typography>}
                                            {l.withholdingCode && <Typography variant="caption" component="div">Tevkifat {l.withholdingCode}</Typography>}
                                        </TableCell>
                                        <TableCell align="right">
                                            {String(l.quantity).replace(".", ",")} {l.unit}
                                        </TableCell>
                                        <TableCell align="right">{formatMoney(l.unitPrice, currency)}</TableCell>
                                        <TableCell align="right">{l.vatRate}</TableCell>
                                        <TableCell align="right">{formatMoney(l.net, currency)}</TableCell>
                                        <TableCell align="right">{formatMoney(l.total, currency)}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                </DashboardCard>

                <Stack direction={{ xs: "column", md: "row" }} spacing={3}>
                    <DashboardCard title="Vade planı">
                        {invoice.dueSchedule.length === 0 ? (
                            <Typography color="text.secondary">{invoice.status === "draft" ? "Vade kesimde oluşur." : "Vade satırı yok."}</Typography>
                        ) : (
                            invoice.dueSchedule.map((d) => (
                                <Typography key={d.dueDate + d.amount} variant="body2">
                                    {formatDate(d.dueDate)} · {formatMoney(d.amount, currency)}
                                </Typography>
                            ))
                        )}
                        {invoice.notes && (
                            <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                                Not: {invoice.notes}
                            </Typography>
                        )}
                    </DashboardCard>
                    <DashboardCard>
                        <TotalsPanel input={null} currency={invoice.currency} fixed={{ totals: invoice.totals, totalsTl: invoice.totalsTl, vatSummary: invoice.vatSummary }} />
                    </DashboardCard>
                </Stack>
            </Stack>
            <CancelDialog
                open={cancelOpen}
                invoiceId={invoice.id}
                number={invoice.number}
                onClose={() => setCancelOpen(false)}
                onCancelled={() => {
                    setCancelOpen(false);
                    onChanged();
                }}
            />
            <EmailDialog
                key={emailDefault}
                open={emailOpen}
                invoiceId={invoice.id}
                defaultTo={emailDefault}
                onClose={() => setEmailOpen(false)}
                onSent={() => {
                    setEmailOpen(false);
                    setSent(true);
                }}
            />
        </>
    );
}
