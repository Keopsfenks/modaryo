"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
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
import { IconPencil } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { invoicing, type RateDto, type RateType } from "@/lib/api/invoicing";
import { formatDate, todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const TYPES: { type: RateType; label: string }[] = [
    { type: "forexBuying", label: "Döviz Alış" },
    { type: "forexSelling", label: "Döviz Satış" },
    { type: "banknoteBuying", label: "Efektif Alış" },
    { type: "banknoteSelling", label: "Efektif Satış" },
];
const CURRENCIES = ["USD", "EUR", "GBP"];

const typeLabel = (t: RateType) => TYPES.find((x) => x.type === t)?.label ?? t;
const rateText = (n: number) => n.toLocaleString("tr-TR", { minimumFractionDigits: 4, maximumFractionDigits: 6 });

type Editing = { currency: string; type: RateType; current: number | null };

/** Döviz kurları: tarih seçilir, para birimi x dört kur türü tablosu gösterilir; kur.manage ile elle düzeltilir ya da TCMB'den şimdi çekilir. */
export default function RatesTable() {
    const { hasPermission } = useSession();
    const canManage = hasPermission("kur.manage");
    const [date, setDate] = useState(todayIso());
    const [rates, setRates] = useState<RateDto[] | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [info, setInfo] = useState<string | null>(null);
    const [editing, setEditing] = useState<Editing | null>(null);
    const [value, setValue] = useState("");
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            setRates(await invoicing.rates.list(date));
            setError(null);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setRates([]);
        }
    }, [date]);

    useEffect(() => {
        let cancelled = false;
        invoicing.rates.list(date).then(
            (r) => {
                if (cancelled) return;
                setRates(r);
                setError(null);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setRates([]);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [date]);

    const find = (currency: string, type: RateType) => rates?.find((r) => r.currency === currency && r.type === type);
    const parsed = parseAmount(value, { maxDecimals: 4 });

    async function saveOverride() {
        if (!editing || parsed === null || parsed <= 0) return;
        setBusy(true);
        try {
            await invoicing.rates.setOverride({ date, currency: editing.currency, type: editing.type, rate: parsed });
            setEditing(null);
            setInfo("Kur kaydedildi.");
            await load();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    async function fetchNow() {
        setBusy(true);
        setInfo(null);
        try {
            const result = await invoicing.rates.fetchNow();
            setInfo(`TCMB'den ${result.stored} kur kaydedildi${result.failed ? `, ${result.failed} bülten alınamadı` : ""}.`);
            await load();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title="Döviz kurları" subtitle="TCMB kurları; tarihte kur yoksa en yakın önceki iş günü gösterilir" />
            <DashboardCard
                action={
                    canManage ? (
                        <Button variant="outlined" disabled={busy} onClick={() => void fetchNow()}>
                            Şimdi çek
                        </Button>
                    ) : undefined
                }
            >
                <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
                    <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => e.target.value && setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                </Stack>
                <FormAlert error={error} />
                {info && (
                    <Alert severity="success" sx={{ mb: 2 }}>
                        {info}
                    </Alert>
                )}
                {rates === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : (
                <TableContainer>
                    <Table size="small" sx={{ minWidth: 700 }}>
                        <TableHead>
                            <TableRow>
                                <TableCell>Para birimi</TableCell>
                                {TYPES.map((t) => (
                                    <TableCell key={t.type} align="right">
                                        {t.label}
                                    </TableCell>
                                ))}
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {CURRENCIES.map((currency) => (
                                <TableRow key={currency}>
                                    <TableCell>{currency}</TableCell>
                                    {TYPES.map((t) => {
                                        const r = find(currency, t.type);
                                        return (
                                            <TableCell key={t.type} align="right">
                                                {r ? (
                                                    <Stack direction="row" spacing={0.5} sx={{ justifyContent: "flex-end", alignItems: "center" }}>
                                                        <span>{rateText(r.rate)}</span>
                                                        <Chip size="small" variant="outlined" color={r.overridden ? "warning" : "default"} label={r.overridden ? "Elle" : "TCMB"} />
                                                        {canManage && (
                                                            <IconButton
                                                                size="small"
                                                                aria-label={`${currency} ${t.label} düzenle`}
                                                                onClick={() => {
                                                                    setEditing({ currency, type: t.type, current: r.rate });
                                                                    setValue(String(r.rate).replace(".", ","));
                                                                }}
                                                            >
                                                                <IconPencil size={16} />
                                                            </IconButton>
                                                        )}
                                                    </Stack>
                                                ) : (
                                                    <Stack direction="row" sx={{ justifyContent: "flex-end", alignItems: "center" }}>
                                                        <span>-</span>
                                                        {canManage && (
                                                            <IconButton
                                                                size="small"
                                                                aria-label={`${currency} ${t.label} düzenle`}
                                                                onClick={() => {
                                                                    setEditing({ currency, type: t.type, current: null });
                                                                    setValue("");
                                                                }}
                                                            >
                                                                <IconPencil size={16} />
                                                            </IconButton>
                                                        )}
                                                    </Stack>
                                                )}
                                            </TableCell>
                                        );
                                    })}
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </TableContainer>
                )}
                {rates && rates.length > 0 && rates[0].effectiveDate !== date && (
                    <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                        Seçilen tarihte bülten yok; {formatDate(rates[0].effectiveDate)} tarihli kurlar gösteriliyor.
                    </Typography>
                )}
            </DashboardCard>

            <Dialog open={!!editing} onClose={() => setEditing(null)} fullWidth maxWidth="xs">
                <DialogTitle>Kuru elle düzelt</DialogTitle>
                <DialogContent>
                    {editing && (
                        <Stack spacing={2} sx={{ pt: 1 }}>
                            <Typography variant="body2" color="text.secondary">
                                {editing.currency} · {typeLabel(editing.type)} · {formatDate(date)}. Elle girilen kur yalnızca bu firmada TCMB kurunun yerine geçer.
                            </Typography>
                            <TextField
                                label="Kur (TL)"
                                value={value}
                                onChange={(e) => setValue(e.target.value)}
                                error={value !== "" && (parsed === null || parsed <= 0)}
                                helperText="En fazla 4 ondalık"
                                autoFocus
                                slotProps={{ htmlInput: { inputMode: "decimal" } }}
                            />
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setEditing(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy || parsed === null || parsed <= 0} onClick={() => void saveOverride()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
