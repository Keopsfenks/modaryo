"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
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
import { errorMessage } from "@/lib/api/errorMessages";
import type { CurrentAccountListItem } from "@/lib/api/erp";
import { treasury, type ExchangeDifferenceDto, type TreasuryAccountDto } from "@/lib/api/treasury";
import { addDaysIso, todayIso } from "@/lib/date";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import CariPicker from "./CariPicker";

const VAT_RATES = [0, 1, 10, 20];
const round2 = (n: number) => Math.round(n * 100) / 100;

/** Kur farkı aracı: tahsilat/ödeme ile fatura arasındaki kur farkını listeler; pozitif farktan fatura, negatif farktan gider fişi üretir. */
export default function ExchangeDifferences() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canRead = hasPermission("finans.read");
    const canInvoice = hasPermission("finans.manage");
    const canExpense = hasPermission("masraf.manage");

    const [from, setFrom] = useState(addDaysIso(todayIso(), -90));
    const [to, setTo] = useState(todayIso());
    const [cari, setCari] = useState<CurrentAccountListItem | null>(null);
    const [rows, setRows] = useState<ExchangeDifferenceDto[] | null>(null);
    const [selected, setSelected] = useState<string[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [dialog, setDialog] = useState<"invoice" | "expense" | null>(null);
    const [vatRate, setVatRate] = useState(20);
    const [accountId, setAccountId] = useState("");
    const [accounts, setAccounts] = useState<TreasuryAccountDto[]>([]);
    const [busy, setBusy] = useState(false);
    const [done, setDone] = useState<string | null>(null);

    const load = useCallback(async () => {
        try {
            setRows(await treasury.exchangeDifferences({ currentAccountId: cari?.id, from, to }));
            setSelected([]);
            setError(null);
        } catch (e) {
            setError(errorMessage(e));
            setRows((r) => r ?? []);
        }
    }, [cari, from, to]);

    useEffect(() => {
        if (!canRead) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- filtre değişince yeniden yükleme
        void load();
    }, [canRead, load]);

    useEffect(() => {
        if (dialog !== "expense") return;
        treasury.accounts().then((list) => setAccounts(list.filter((a) => a.isActive && a.currency === "TRY" && a.kind !== "cardPos")), () => setAccounts([]));
    }, [dialog]);

    if (!canRead) return <Alert severity="warning">Kur farkı araçları için yetkiniz yok.</Alert>;

    const chosen = (rows ?? []).filter((r) => r.allocationIds.some((id) => selected.includes(id)));
    const total = round2(chosen.reduce((sum, r) => sum + r.difference, 0));
    const lockedCari = chosen[0]?.currentAccountId;
    const key = (r: ExchangeDifferenceDto) => r.allocationIds.join(",");

    const toggle = (r: ExchangeDifferenceDto, on: boolean) =>
        setSelected((s) => (on ? [...s, ...r.allocationIds] : s.filter((id) => !r.allocationIds.includes(id))));

    async function run(action: () => Promise<void>) {
        setBusy(true);
        setError(null);
        try {
            await action();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setDialog(null);
            setError(errorMessage(e));
            await load();
        } finally {
            setBusy(false);
        }
    }

    const createInvoice = () =>
        run(async () => {
            const result = await treasury.createDifferenceInvoice({ allocationIds: selected, vatRate });
            router.push(`/satis/faturalar/${result.invoiceId}`);
        });

    const createExpense = () =>
        run(async () => {
            const result = await treasury.createDifferenceExpense({ allocationIds: selected, accountId: accountId || null });
            setDialog(null);
            setDone(`Kur farkı gider fişi oluşturuldu: ${result.number}`);
            await load();
        });

    return (
        <>
            <PageHeader title="Kur farkı" subtitle="Döviz faturası ile tahsilat/ödeme arasındaki kur farkı; her fark yalnızca bir kez işlenir." />
            <DashboardCard>
                <Stack spacing={2}>
                    {done && <Alert severity="success">{done}</Alert>}
                    <FormAlert error={error} />
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                        <CariPicker value={cari} onChange={setCari} />
                        <TextField size="small" type="date" label="Başlangıç" value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField size="small" type="date" label="Bitiş" value={to} onChange={(e) => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>
                    {rows === null ? (
                        <Skeleton variant="rounded" height={160} />
                    ) : rows.length === 0 ? (
                        <Typography color="text.secondary" sx={{ py: 3 }}>
                            Bu aralıkta işlenecek kur farkı yok.
                        </Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell padding="checkbox" />
                                        <TableCell>Cari</TableCell>
                                        <TableCell>Fatura</TableCell>
                                        <TableCell align="right">Döviz tutarı</TableCell>
                                        <TableCell align="right">Fatura kuru</TableCell>
                                        <TableCell align="right">Ödeme kuru</TableCell>
                                        <TableCell align="right">Kur farkı (TL)</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {rows.map((r) => (
                                        <TableRow key={key(r)} hover>
                                            <TableCell padding="checkbox">
                                                <Checkbox
                                                    checked={r.allocationIds.every((id) => selected.includes(id))}
                                                    disabled={lockedCari !== undefined && lockedCari !== r.currentAccountId}
                                                    onChange={(_, on) => toggle(r, on)}
                                                    slotProps={{ input: { "aria-label": `${r.invoiceNumber} seç` } }}
                                                />
                                            </TableCell>
                                            <TableCell>{r.currentAccountName}</TableCell>
                                            <TableCell>{r.invoiceNumber}</TableCell>
                                            <TableCell align="right">{formatMoney(r.foreignAmount, r.currency)}</TableCell>
                                            <TableCell align="right">{r.invoiceRate.toLocaleString("tr-TR", { minimumFractionDigits: 4 })}</TableCell>
                                            <TableCell align="right">{r.paymentRate.toLocaleString("tr-TR", { minimumFractionDigits: 4 })}</TableCell>
                                            <TableCell align="right" sx={{ color: r.difference < 0 ? "error.main" : "success.main" }}>
                                                {formatMoney(r.difference, "TRY")}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                    <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                        <Typography>Seçilen toplam: {formatMoney(total, "TRY")}</Typography>
                        {canInvoice && (
                            <Button variant="contained" disabled={total <= 0} onClick={() => setDialog("invoice")}>
                                Kur farkı faturası oluştur
                            </Button>
                        )}
                        {canExpense && (
                            <Button variant="outlined" color="warning" disabled={total >= 0} onClick={() => setDialog("expense")}>
                                Kur farkı gideri oluştur
                            </Button>
                        )}
                    </Stack>
                </Stack>
            </DashboardCard>

            <Dialog open={dialog === "invoice"} onClose={() => setDialog(null)} fullWidth maxWidth="xs">
                <DialogTitle>Kur farkı faturası</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <Typography variant="body2">Tutar KDV dahil {formatMoney(total, "TRY")}; taslak fatura oluşturulur ve açılır.</Typography>
                        <TextField select label="KDV oranı (%)" value={vatRate} onChange={(e) => setVatRate(Number(e.target.value))}>
                            {VAT_RATES.map((v) => (
                                <MenuItem key={v} value={v}>
                                    %{v}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDialog(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy} onClick={() => void createInvoice()}>
                        Faturayı oluştur
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={dialog === "expense"} onClose={() => setDialog(null)} fullWidth maxWidth="xs">
                <DialogTitle>Kur farkı gideri</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <Typography variant="body2">Gider tutarı {formatMoney(Math.abs(total), "TRY")}. Hesap seçilmezse cari hesaba alacak yazılır.</Typography>
                        <TextField select label="Ödeme hesabı (isteğe bağlı)" value={accountId} onChange={(e) => setAccountId(e.target.value)}>
                            <MenuItem value="">Cariye yaz</MenuItem>
                            {accounts.map((a) => (
                                <MenuItem key={a.id} value={a.id}>
                                    {a.name}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDialog(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy} onClick={() => void createExpense()}>
                        Gideri oluştur
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
