import { useState } from "react";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import Grid from "@mui/material/Grid";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { erp, type AddressDto, type AddressInput } from "@/lib/api/erp";

type AddressDialogProps = {
    accountId: string;
    /** Verilirse düzenleme, verilmezse yeni adres. */
    address?: AddressDto;
    onClose: () => void;
    /** Kayıt başarılı olunca çağrılır; çağıran hesabı yeniden yükler. */
    onSaved: () => void;
};

type TextKey = "title" | "line" | "district" | "city" | "postalCode" | "country";

type Values = Record<TextKey, string> & {
    isBilling: boolean;
    isShipping: boolean;
    isDefaultBilling: boolean;
    isDefaultShipping: boolean;
};

type Errors = Partial<Record<string, string>>;

const blankToNull = (text: string) => text.trim() || null;

function validate(v: Values): Errors {
    const errors: Errors = {};
    const title = v.title.trim();
    const line = v.line.trim();
    if (title.length < 1 || title.length > 100) errors.title = "Başlık 1 ile 100 karakter arasında olmalı.";
    if (line.length < 1 || line.length > 500) errors.line = "Adres 1 ile 500 karakter arasında olmalı.";
    if (!v.isBilling && !v.isShipping) errors.type = "Fatura veya teslimat adresinden en az biri seçilmeli.";
    return errors;
}

/** Adres ekleme / düzenleme penceresi. */
export default function AddressDialog({ accountId, address, onClose, onSaved }: AddressDialogProps) {
    const [v, setV] = useState<Values>({
        title: address?.title ?? "",
        line: address?.line ?? "",
        district: address?.district ?? "",
        city: address?.city ?? "",
        postalCode: address?.postalCode ?? "",
        country: address?.country ?? "Türkiye",
        isBilling: address?.isBilling ?? false,
        isShipping: address?.isShipping ?? false,
        isDefaultBilling: address?.isDefaultBilling ?? false,
        isDefaultShipping: address?.isDefaultShipping ?? false,
    });
    const [errors, setErrors] = useState<Errors>({});
    const [alert, setAlert] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    const set = <K extends keyof Values>(key: K, value: Values[K]) => setV((prev) => ({ ...prev, [key]: value }));
    const text = (key: TextKey, label: string, extra: Partial<React.ComponentProps<typeof TextField>> = {}) => (
        <TextField
            id={`address-${key}`}
            label={label}
            fullWidth
            value={v[key]}
            onChange={(e) => set(key, e.target.value)}
            error={Boolean(errors[key])}
            helperText={errors[key]}
            {...extra}
        />
    );

    const submit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;
        const found = validate(v);
        setErrors(found);
        setAlert(null);
        if (Object.keys(found).length > 0) return;

        const body: AddressInput = {
            title: v.title.trim(),
            line: v.line.trim(),
            district: blankToNull(v.district),
            city: blankToNull(v.city),
            postalCode: blankToNull(v.postalCode),
            country: v.country.trim() || "Türkiye",
            isBilling: v.isBilling,
            isShipping: v.isShipping,
            isDefaultBilling: v.isBilling && v.isDefaultBilling,
            isDefaultShipping: v.isShipping && v.isDefaultShipping,
        };
        setBusy(true);
        try {
            if (address) {
                await erp.updateAddress(accountId, address.id, body);
            } else {
                await erp.addAddress(accountId, body);
            }
            onSaved();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setErrors(e.fieldErrors);
            setAlert(e);
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <form noValidate onSubmit={submit}>
                <DialogTitle>{address ? "Adresi düzenle" : "Adres ekle"}</DialogTitle>
                <DialogContent>
                    <FormAlert error={alert} />
                    <Grid container spacing={2} sx={{ mt: 0.5 }}>
                        <Grid size={12}>{text("title", "Başlık")}</Grid>
                        <Grid size={12}>{text("line", "Adres", { multiline: true, minRows: 2 })}</Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>{text("district", "İlçe")}</Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>{text("city", "İl")}</Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>{text("postalCode", "Posta kodu")}</Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>{text("country", "Ülke")}</Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>
                            <FormControlLabel
                                control={<Checkbox checked={v.isBilling} onChange={(e) => set("isBilling", e.target.checked)} />}
                                label="Fatura adresi"
                            />
                            <FormControlLabel
                                control={
                                    <Checkbox
                                        checked={v.isBilling && v.isDefaultBilling}
                                        disabled={!v.isBilling}
                                        onChange={(e) => set("isDefaultBilling", e.target.checked)}
                                    />
                                }
                                label="Varsayılan fatura adresi"
                            />
                        </Grid>
                        <Grid size={{ xs: 12, sm: 6 }}>
                            <FormControlLabel
                                control={<Checkbox checked={v.isShipping} onChange={(e) => set("isShipping", e.target.checked)} />}
                                label="Teslimat adresi"
                            />
                            <FormControlLabel
                                control={
                                    <Checkbox
                                        checked={v.isShipping && v.isDefaultShipping}
                                        disabled={!v.isShipping}
                                        onChange={(e) => set("isDefaultShipping", e.target.checked)}
                                    />
                                }
                                label="Varsayılan teslimat adresi"
                            />
                        </Grid>
                        {errors.type && (
                            <Grid size={12}>
                                <FormHelperText error>{errors.type}</FormHelperText>
                            </Grid>
                        )}
                    </Grid>
                </DialogContent>
                <DialogActions>
                    <Button onClick={onClose}>Vazgeç</Button>
                    <Button type="submit" variant="contained" disabled={busy}>
                        Kaydet
                    </Button>
                </DialogActions>
            </form>
        </Dialog>
    );
}
