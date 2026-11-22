import { useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Grid from "@mui/material/Grid";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { erp, type AddEntryResult, type CurrentAccountDto, type EntryDirection, type EntryInput, type EntryInputType } from "@/lib/api/erp";
import { addDaysIso, todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";

const MAX_MONEY = 999_999_999_999.99;

type EntryFormProps = {
    account: CurrentAccountDto;
    onAdded: (result: AddEntryResult) => void;
};

type Errors = Partial<Record<string, string>>;

const blankToNull = (text: string) => text.trim() || null;

/** Sunucu hata kodlarının Türkçe karşılıkları. */
function serverMessage(e: ApiError): string | null {
    if (e.code === "account_inactive") return "Pasif cari hesaba hareket girilemez.";
    if (e.code === "opening_exists") return "Bu carinin zaten bir açılış bakiyesi var.";
    return null;
}

/**
 * Yeni hareket formu. Borçta vade tarihi, kullanıcı elle değiştirmedikçe hareket tarihi + kartın
 * varsayılan vade günü olarak türetilir.
 */
export default function EntryForm({ account, onAdded }: EntryFormProps) {
    const [type, setType] = useState<EntryInputType>("debit");
    const [direction, setDirection] = useState<EntryDirection | "">("");
    const [date, setDate] = useState(todayIso);
    // null: kullanıcı dokunmadı (türetilir); string: kullanıcının girdiği değer.
    const [dueEdited, setDueEdited] = useState<string | null>(null);
    const [amount, setAmount] = useState("");
    const [documentNo, setDocumentNo] = useState("");
    const [description, setDescription] = useState("");
    const [errors, setErrors] = useState<Errors>({});
    const [alert, setAlert] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    const dueDate = dueEdited ?? (type === "debit" ? addDaysIso(date, account.defaultDueDays) : "");

    const validate = (): Errors => {
        const found: Errors = {};
        const parsed = parseAmount(amount);
        if (parsed === null || parsed <= 0 || parsed > MAX_MONEY) {
            found.amount = "Tutar 0'dan büyük olmalı (en çok 2 ondalık, 999.999.999.999,99'a kadar). Örn. 1.500,00";
        }
        if (!date) found.date = "Tarih seçin.";
        if (type === "opening" && !direction) found.direction = "Yön seçin (Borç veya Alacak).";
        if (documentNo.trim().length > 50) found.documentNo = "Belge no en çok 50 karakter olabilir.";
        if (description.trim().length > 500) found.description = "Açıklama en çok 500 karakter olabilir.";
        return found;
    };

    const submit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;
        const found = validate();
        setErrors(found);
        setAlert(null);
        if (Object.keys(found).length > 0) return;

        const body: EntryInput = {
            type,
            date,
            amount: parseAmount(amount) as number,
            documentNo: blankToNull(documentNo),
            description: blankToNull(description),
            dueDate: type === "opening" ? null : blankToNull(dueDate),
            ...(type === "opening" ? { direction: direction as EntryDirection } : {}),
        };
        setBusy(true);
        try {
            const result = await erp.addEntry(account.id, body);
            setAmount("");
            setDocumentNo("");
            setDescription("");
            setDueEdited(null);
            onAdded(result);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setErrors(e.fieldErrors);
            setAlert(serverMessage(e) ?? e);
        } finally {
            setBusy(false);
        }
    };

    return (
        <Box component="form" noValidate onSubmit={submit}>
            <FormAlert error={alert} />
            <Grid container spacing={2}>
                <Grid size={{ xs: 12, sm: 4, md: 3 }}>
                    <TextField select id="entry-type" label="Tür" fullWidth value={type} onChange={(e) => setType(e.target.value as EntryInputType)}>
                        <MenuItem value="opening">Açılış</MenuItem>
                        <MenuItem value="debit">Borç</MenuItem>
                        <MenuItem value="credit">Alacak</MenuItem>
                    </TextField>
                </Grid>
                {type === "opening" && (
                    <Grid size={{ xs: 12, sm: 4, md: 3 }}>
                        <TextField
                            select
                            id="entry-direction"
                            label="Yön"
                            fullWidth
                            value={direction}
                            onChange={(e) => setDirection(e.target.value as EntryDirection)}
                            error={Boolean(errors.direction)}
                            helperText={errors.direction}
                        >
                            <MenuItem value="debit">Borç</MenuItem>
                            <MenuItem value="credit">Alacak</MenuItem>
                        </TextField>
                    </Grid>
                )}
                <Grid size={{ xs: 12, sm: 4, md: 3 }}>
                    <TextField
                        id="entry-date"
                        label="Tarih"
                        type="date"
                        fullWidth
                        value={date}
                        onChange={(e) => setDate(e.target.value)}
                        error={Boolean(errors.date)}
                        helperText={errors.date}
                        slotProps={{ inputLabel: { shrink: true } }}
                    />
                </Grid>
                <Grid size={{ xs: 12, sm: 4, md: 3 }}>
                    <TextField
                        id="entry-amount"
                        label="Tutar"
                        fullWidth
                        value={amount}
                        onChange={(e) => setAmount(e.target.value)}
                        error={Boolean(errors.amount)}
                        helperText={errors.amount}
                        slotProps={{ htmlInput: { inputMode: "decimal" } }}
                    />
                </Grid>
                {type !== "opening" && (
                    <Grid size={{ xs: 12, sm: 4, md: 3 }}>
                        <TextField
                            id="entry-due"
                            label="Vade tarihi"
                            type="date"
                            fullWidth
                            value={dueDate}
                            onChange={(e) => setDueEdited(e.target.value)}
                            error={Boolean(errors.dueDate)}
                            helperText={errors.dueDate}
                            slotProps={{ inputLabel: { shrink: true } }}
                        />
                    </Grid>
                )}
                <Grid size={{ xs: 12, sm: 4, md: 3 }}>
                    <TextField
                        id="entry-document"
                        label="Belge no"
                        fullWidth
                        value={documentNo}
                        onChange={(e) => setDocumentNo(e.target.value)}
                        error={Boolean(errors.documentNo)}
                        helperText={errors.documentNo}
                    />
                </Grid>
                <Grid size={{ xs: 12, md: 6 }}>
                    <TextField
                        id="entry-description"
                        label="Açıklama"
                        fullWidth
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        error={Boolean(errors.description)}
                        helperText={errors.description}
                    />
                </Grid>
            </Grid>
            <Box sx={{ mt: 2, display: "flex", justifyContent: "flex-end" }}>
                <Button type="submit" variant="contained" disabled={busy}>
                    Hareketi kaydet
                </Button>
            </Box>
        </Box>
    );
}
