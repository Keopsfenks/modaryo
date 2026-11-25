"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
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
import Typography from "@mui/material/Typography";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { treasury, type InstrumentDetail as Detail } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { allowedTransitions, DIRECTION_LABELS, KIND_LABELS, STATUS_LABELS } from "@/lib/instruments";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { MODULE_KEY } from "./Instruments";
import InstrumentMoveDialog from "./InstrumentMoveDialog";

const Field = ({ label, children }: { label: string; children: React.ReactNode }) => (
    <div>
        <Typography variant="caption" color="text.secondary">
            {label}
        </Typography>
        <Typography>{children || "—"}</Typography>
    </div>
);

/** Çek/senet kartı: bilgiler, durum geçmişi ve durum geçişi. */
export default function InstrumentDetail({ id }: { id: string }) {
    const { hasPermission, hasModule } = useSession();
    const enabled = hasModule(MODULE_KEY);
    const canManage = hasPermission("ceksenet.manage");
    const [data, setData] = useState<Detail | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [moving, setMoving] = useState(false);
    const [warning, setWarning] = useState(false);

    const load = useCallback(async () => {
        try {
            setData(await treasury.instrument(id));
            setError(null);
        } catch (e) {
            setError(errorMessage(e));
        }
    }, [id]);

    useEffect(() => {
        if (!enabled) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [enabled, load]);

    if (!enabled) return <Alert severity="info">Çek/senet modülü firmanızda kapalı.</Alert>;
    if (error) return <Alert severity="error">{error}</Alert>;
    if (!data) return <Skeleton variant="rounded" height={240} />;

    const { instrument: i, history } = data;
    const canMove = canManage && allowedTransitions(i.direction, i.status).length > 0;

    return (
        <>
            <PageHeader title={`${KIND_LABELS[i.kind]} ${i.number}`} subtitle={`${DIRECTION_LABELS[i.direction]} · ${i.currentAccountName}`} />
            <Stack spacing={3}>
                {warning && <Alert severity="warning">Kasa bakiyesi eksiye düştü. Kasa hareketlerini kontrol edin.</Alert>}
                <DashboardCard
                    action={
                        <Stack direction="row" spacing={1}>
                            <Button component={Link} href="/finans/cek-senet" color="inherit">
                                Listeye dön
                            </Button>
                            {canMove && (
                                <Button variant="contained" onClick={() => setMoving(true)}>
                                    Durum değiştir
                                </Button>
                            )}
                        </Stack>
                    }
                >
                    <Stack direction="row" spacing={4} sx={{ flexWrap: "wrap", rowGap: 2 }}>
                        <Field label="Durum">
                            {STATUS_LABELS[i.status]}
                            {i.protestFlag && <Chip size="small" color="error" label="Protesto" sx={{ ml: 1 }} />}
                        </Field>
                        <Field label="Tutar">{formatMoney(i.amount, i.currency)}</Field>
                        <Field label="Vade">{formatDate(i.dueDate)}</Field>
                        <Field label="Keşideci / borçlu">{i.drawerName}</Field>
                        <Field label="Banka">{[i.bankName, i.bankBranch].filter(Boolean).join(" / ")}</Field>
                        <Field label="Hesap no / IBAN">{i.accountNo}</Field>
                        <Field label="Ciro sayısı">{String(i.endorsementCount)}</Field>
                        {i.endorsedToName && <Field label="Ciro edilen">{i.endorsedToName}</Field>}
                    </Stack>
                </DashboardCard>
                <DashboardCard title="Geçmiş">
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Durum</TableCell>
                                    <TableCell>Belge</TableCell>
                                    <TableCell>Not</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {history.map((h) => (
                                    <TableRow key={h.id}>
                                        <TableCell>{formatDate(h.date)}</TableCell>
                                        <TableCell>{h.from ? `${STATUS_LABELS[h.from]} → ${STATUS_LABELS[h.to]}` : STATUS_LABELS[h.to]}</TableCell>
                                        <TableCell>{h.documentNumber ?? "—"}</TableCell>
                                        <TableCell>{h.note ?? ""}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                </DashboardCard>
            </Stack>
            {moving && (
                <InstrumentMoveDialog
                    open
                    instrument={i}
                    onClose={() => setMoving(false)}
                    onMoved={(result) => {
                        setMoving(false);
                        setWarning(result.warnings.includes("cash_negative"));
                        void load();
                    }}
                />
            )}
        </>
    );
}
