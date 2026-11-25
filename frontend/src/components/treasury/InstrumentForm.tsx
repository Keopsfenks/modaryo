"use client";

import { useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { CurrentAccountListItem } from "@/lib/api/erp";
import { treasury, type InstrumentDirection, type InstrumentInput, type InstrumentKind, type TreasuryAccountDto } from "@/lib/api/treasury";
import { todayIso } from "@/lib/date";
import { KIND_LABELS } from "@/lib/instruments";
import { CURRENCIES, parseAmount, type Currency } from "@/lib/money";
import CariPicker from "./CariPicker";

type InstrumentFormProps = {
    open: boolean;
    direction: InstrumentDirection;
    onClose(): void;
    onSaved(): void;
};

/** Çek/senet girişi: alınan (müşteriden) ya da verilen (tedarikçiye). Verilen çekte ödeme yapılacak banka hesabı seçilir. */
export default function InstrumentForm({ open, direction, onClose, onSaved }: InstrumentFormProps) {
    const received = direction === "received";
    const [kind, setKind] = useState<InstrumentKind>("check");
    const [cari, setCari] = useState<CurrentAccountListItem | null>(null);
    const [number, setNumber] = useState("");
    const [date, setDate] = useState(todayIso());
    const [dueDate, setDueDate] = useState(todayIso());
    const [amountText, setAmountText] = useState("");
    const [currency, setCurrency] = useState<Currency>("TRY");
    const [drawer, setDrawer] = useState("");
    const [bankName, setBankName] = useState("");
    const [bankBranch, setBankBranch] = useState("");
    const [accountNo, setAccountNo] = useState("");
    const [endorsements, setEndorsements] = useState("0");
    const [bankAccountId, setBankAccountId] = useState("");
    const [accounts, setAccounts] = useState<TreasuryAccountDto[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!open || received) return;
        treasury.accounts().then((list) => setAccounts(list.filter((a) => a.isActive && a.kind === "bank")), () => setAccounts([]));
    }, [open, received]);

    const amount = parseAmount(amountText);
    const bankOptions = accounts.filter((a) => a.currency === currency);

    async function submit() {
        if (busy) return;
        setError(null);
        if (!cari) return setError(received ? "Müşteri cari seçilmelidir." : "Tedarikçi cari seçilmelidir.");
        if (!number.trim()) return setError("Çek/senet numarası zorunludur.");
        if (amount === null || amount <= 0) return setError("Tutar 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");
        if (received && !drawer.trim()) return setError("Keşideci/borçlu adı zorunludur.");
        if (received && kind === "check" && !bankName.trim()) return setError("Çek için banka adı zorunludur.");
        if (!received && kind === "check" && !bankAccountId) return setError("Verilen çek için ödeme yapılacak banka hesabı seçilmelidir.");

        const body: InstrumentInput = {
            kind,
            currentAccountId: cari.id,
            number: number.trim(),
            dueDate,
            amount,
            currency,
            date,
            drawer: drawer.trim() || null,
            bankName: received ? bankName.trim() || null : null,
            bankBranch: received ? bankBranch.trim() || null : null,
            accountNo: received ? accountNo.trim() || null : null,
        };

        setBusy(true);
        try {
            if (received) await treasury.receiveInstrument({ ...body, endorsementCount: Number(endorsements) || 0 });
            else await treasury.issueInstrument({ ...body, bankAccountId: bankAccountId || null });
            onSaved();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{received ? "Müşteriden çek/senet al" : "Tedarikçiye çek/senet ver"}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <TextField select label="Tür" value={kind} onChange={(e) => setKind(e.target.value as InstrumentKind)}>
                        {(Object.keys(KIND_LABELS) as InstrumentKind[]).map((k) => (
                            <MenuItem key={k} value={k}>
                                {KIND_LABELS[k]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <CariPicker label={received ? "Müşteri" : "Tedarikçi"} role={received ? "customer" : "supplier"} value={cari} onChange={(c) => { setCari(c); if (c) setCurrency(c.currency); }} />
                    <Stack direction="row" spacing={2}>
                        <TextField label="Numara" value={number} onChange={(e) => setNumber(e.target.value)} sx={{ flexGrow: 1 }} />
                        <TextField label="Tutar" value={amountText} onChange={(e) => setAmountText(e.target.value)} error={amountText.trim() !== "" && amount === null} slotProps={{ htmlInput: { inputMode: "decimal" } }} />
                        <TextField select label="Para birimi" value={currency} onChange={(e) => setCurrency(e.target.value as Currency)} sx={{ minWidth: 110 }}>
                            {CURRENCIES.map((c) => (
                                <MenuItem key={c} value={c}>
                                    {c}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Stack>
                    <Stack direction="row" spacing={2}>
                        <TextField type="date" label="İşlem tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField type="date" label="Vade" value={dueDate} onChange={(e) => setDueDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>
                    {received ? (
                        <>
                            <TextField label="Keşideci / borçlu" value={drawer} onChange={(e) => setDrawer(e.target.value)} />
                            {kind === "check" && (
                                <Stack direction="row" spacing={2}>
                                    <TextField label="Banka" value={bankName} onChange={(e) => setBankName(e.target.value)} sx={{ flexGrow: 1 }} />
                                    <TextField label="Şube" value={bankBranch} onChange={(e) => setBankBranch(e.target.value)} />
                                    <TextField label="Hesap no" value={accountNo} onChange={(e) => setAccountNo(e.target.value)} />
                                </Stack>
                            )}
                            <TextField label="Üzerindeki ciro sayısı" value={endorsements} onChange={(e) => setEndorsements(e.target.value)} slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ maxWidth: 220 }} />
                        </>
                    ) : (
                        <>
                            <TextField label="Keşideci / borçlu" value={drawer} onChange={(e) => setDrawer(e.target.value)} helperText="Boş bırakılırsa &quot;Firma&quot; yazılır." />
                            <TextField select label="Ödeme hesabı (banka)" value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)} helperText={kind === "check" ? "Verilen çek için zorunludur." : "İsteğe bağlı."}>
                                <MenuItem value="">—</MenuItem>
                                {bankOptions.map((a) => (
                                    <MenuItem key={a.id} value={a.id}>
                                        {a.name}
                                    </MenuItem>
                                ))}
                            </TextField>
                        </>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Kaydediliyor..." : "Kaydet"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
