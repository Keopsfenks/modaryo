"use client";

import { useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import { catalog, flattenCategories, type BrandDto, type CategoryNode } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing } from "@/lib/api/pricing";
import { parseAmount } from "@/lib/money";

type BulkAdjustDialogProps = {
    open: boolean;
    listId: string;
    onClose: () => void;
    /** Güncellenen satır sayısıyla çağrılır. */
    onDone: (updated: number) => void;
};

/** Listenin açık satırlarına yüzde zam/indirim uygular; isteğe bağlı kategori ya da marka ile daraltılır. */
export default function BulkAdjustDialog({ open, listId, onClose, onDone }: BulkAdjustDialogProps) {
    const [percent, setPercent] = useState("");
    const [categoryId, setCategoryId] = useState("");
    const [brandId, setBrandId] = useState("");
    const [categories, setCategories] = useState<CategoryNode[]>([]);
    const [brands, setBrands] = useState<BrandDto[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [fieldError, setFieldError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!open) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- pencere her açıldığında sıfırlanır
        setPercent("");
        setCategoryId("");
        setBrandId("");
        setError(null);
        setFieldError(null);
        catalog.categories().then(setCategories, () => undefined);
        catalog.brands().then(setBrands, () => undefined);
    }, [open]);

    async function submit() {
        const negative = percent.trim().startsWith("-");
        const parsed = parseAmount(percent.trim().replace(/^[-+]/, ""), { maxDecimals: 4 });
        const value = parsed === null ? null : negative ? -parsed : parsed;
        if (value === null || value === 0 || value < -100 || value > 1000) {
            setFieldError("Yüzde 0'dan farklı, -100 ile 1000 arasında bir sayı olmalı.");
            return;
        }
        setFieldError(null);
        setBusy(true);
        setError(null);
        try {
            const result = await pricing.bulkAdjust(listId, { percent: value, categoryId: categoryId || null, brandId: brandId || null });
            onDone(result.updated);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>Toplu fiyat ayarı</DialogTitle>
            <DialogContent>
                <Stack spacing={2.5} sx={{ pt: 1 }}>
                    <DialogContentText>Listedeki açık satırların fiyatları yüzde oranında değişir; yuvarlama ayarlardaki kurala göre yapılır.</DialogContentText>
                    <FormAlert error={error} />
                    <TextField
                        label="Yüzde (%)"
                        value={percent}
                        onChange={(e) => setPercent(e.target.value)}
                        error={Boolean(fieldError)}
                        helperText={fieldError ?? "Örn. 10 zam, -5 indirim."}
                        slotProps={{ htmlInput: { inputMode: "decimal" } }}
                        autoFocus
                    />
                    <TextField select label="Kategori" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                        <MenuItem value="">Tüm kategoriler</MenuItem>
                        {flattenCategories(categories).map(({ node, depth }) => (
                            <MenuItem key={node.id} value={node.id}>
                                {"— ".repeat(depth)}
                                {node.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField select label="Marka" value={brandId} onChange={(e) => setBrandId(e.target.value)}>
                        <MenuItem value="">Tüm markalar</MenuItem>
                        {brands.map((b) => (
                            <MenuItem key={b.id} value={b.id}>
                                {b.name}
                            </MenuItem>
                        ))}
                    </TextField>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    Uygula
                </Button>
            </DialogActions>
        </Dialog>
    );
}
