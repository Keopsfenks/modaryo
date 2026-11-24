"use client";

import { useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import CurrencySelect from "@/components/cari/CurrencySelect";
import FormAlert from "@/components/auth/FormAlert";
import { erp, type CurrentAccountListItem } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type PriceListDto, type PriceListKind } from "@/lib/api/pricing";
import { parseAmount, type Currency } from "@/lib/money";

type PriceListDialogProps = {
    open: boolean;
    kind: PriceListKind;
    /** Verilirse düzenleme kipi. */
    list?: PriceListDto;
    /** Taban liste adayları (aynı türdeki listeler). */
    lists: PriceListDto[];
    onClose: () => void;
    onSaved: (list: PriceListDto) => void;
};

const percentText = (n: number | null | undefined) => (n == null ? "" : String(n).replace(".", ","));

/** Fiyat listesi oluşturma ve düzenleme penceresi; türetilmiş liste için taban liste ve yüzde alanları vardır. */
export default function PriceListDialog({ open, kind, list, lists, onClose, onSaved }: PriceListDialogProps) {
    const [name, setName] = useState("");
    const [currency, setCurrency] = useState<Currency>("TRY");
    const [vatIncluded, setVatIncluded] = useState(false);
    const [validFrom, setValidFrom] = useState("");
    const [validTo, setValidTo] = useState("");
    const [derived, setDerived] = useState(false);
    const [baseListId, setBaseListId] = useState("");
    const [percent, setPercent] = useState("");
    const [supplierId, setSupplierId] = useState("");
    const [isActive, setIsActive] = useState(true);
    const [suppliers, setSuppliers] = useState<CurrentAccountListItem[]>([]);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [alert, setAlert] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!open) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- pencere her açıldığında form başlangıç değerlerine döner
        setName(list?.name ?? "");
        setCurrency(list?.currency ?? "TRY");
        setVatIncluded(list?.vatIncluded ?? false);
        setValidFrom(list?.validFrom ?? "");
        setValidTo(list?.validTo ?? "");
        setDerived(Boolean(list?.baseListId));
        setBaseListId(list?.baseListId ?? "");
        setPercent(percentText(list?.adjustmentPercent));
        setSupplierId(list?.supplierId ?? "");
        setIsActive(list?.isActive ?? true);
        setErrors({});
        setAlert(null);
    }, [open, list]);

    useEffect(() => {
        if (!open || kind !== "purchase") return;
        erp.listAccounts({ role: "supplier", pageSize: 100 }).then(
            (r) => setSuppliers(r.items),
            () => setSuppliers([]),
        );
    }, [open, kind]);

    const candidates = lists.filter((l) => l.id !== list?.id && l.kind === kind);

    async function submit() {
        const found: Record<string, string> = {};
        const trimmed = name.trim();
        if (!trimmed) found.name = "Liste adı zorunludur.";
        else if (trimmed.length > 200) found.name = "Liste adı en fazla 200 karakter olabilir.";
        if (validFrom && validTo && validFrom > validTo) found.validTo = "Geçerlilik başlangıcı bitişten sonra olamaz.";
        let adjustment: number | null = null;
        if (derived) {
            if (!baseListId) found.baseListId = "Taban liste seçin.";
            const parsed = parseAmount(percent.replace(/^-/, ""), { maxDecimals: 4 });
            const negative = percent.trim().startsWith("-");
            const value = parsed === null ? null : negative ? -parsed : parsed;
            if (value === null || value < -100 || value > 1000) found.percent = "Yüzde -100 ile 1000 arasında bir sayı olmalı.";
            else adjustment = value;
        }
        setErrors(found);
        setAlert(null);
        if (Object.keys(found).length > 0) return;

        setBusy(true);
        try {
            const body = {
                name: trimmed,
                kind,
                currency,
                vatIncluded,
                validFrom: validFrom || null,
                validTo: validTo || null,
                supplierId: kind === "purchase" && supplierId ? supplierId : null,
                baseListId: derived ? baseListId : null,
                adjustmentPercent: derived ? adjustment : null,
                isActive,
            };
            const saved = list ? await pricing.updateList(list.id, { ...body, version: list.version }) : await pricing.createList(body);
            onSaved(saved);
        } catch (e) {
            setAlert(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{list ? "Listeyi düzenle" : "Yeni liste"}</DialogTitle>
            <DialogContent>
                <Stack spacing={2.5} sx={{ pt: 1 }}>
                    <FormAlert error={alert} />
                    <TextField label="Liste adı" value={name} onChange={(e) => setName(e.target.value)} error={Boolean(errors.name)} helperText={errors.name} autoFocus />
                    <CurrencySelect value={currency} onChange={setCurrency} id="list-currency" />
                    <FormControlLabel control={<Switch checked={vatIncluded} onChange={(e) => setVatIncluded(e.target.checked)} />} label="Fiyatlar KDV dahil" />
                    <Stack direction="row" spacing={2}>
                        <TextField
                            label="Geçerlilik başlangıcı"
                            type="date"
                            fullWidth
                            value={validFrom}
                            onChange={(e) => setValidFrom(e.target.value)}
                            slotProps={{ inputLabel: { shrink: true } }}
                        />
                        <TextField
                            label="Geçerlilik bitişi"
                            type="date"
                            fullWidth
                            value={validTo}
                            onChange={(e) => setValidTo(e.target.value)}
                            error={Boolean(errors.validTo)}
                            helperText={errors.validTo}
                            slotProps={{ inputLabel: { shrink: true } }}
                        />
                    </Stack>
                    {kind === "purchase" && (
                        <TextField select label="Tedarikçi" value={supplierId} onChange={(e) => setSupplierId(e.target.value)} helperText="Boşsa firma geneli alış listesi olur.">
                            <MenuItem value="">Tedarikçi seçilmedi</MenuItem>
                            {suppliers.map((s) => (
                                <MenuItem key={s.id} value={s.id}>
                                    {s.code} · {s.name}
                                </MenuItem>
                            ))}
                        </TextField>
                    )}
                    <FormControlLabel
                        control={<Switch checked={derived} onChange={(e) => setDerived(e.target.checked)} />}
                        label="Başka bir listeden türet"
                    />
                    {derived && (
                        <Stack direction="row" spacing={2}>
                            <TextField
                                select
                                label="Taban liste"
                                fullWidth
                                value={baseListId}
                                onChange={(e) => setBaseListId(e.target.value)}
                                error={Boolean(errors.baseListId)}
                                helperText={errors.baseListId}
                            >
                                {candidates.map((l) => (
                                    <MenuItem key={l.id} value={l.id}>
                                        {l.name}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField
                                label="Yüzde ayarı (%)"
                                value={percent}
                                onChange={(e) => setPercent(e.target.value)}
                                error={Boolean(errors.percent)}
                                helperText={errors.percent ?? "Örn. -10 indirim, 15 zam."}
                                slotProps={{ htmlInput: { inputMode: "decimal" } }}
                                sx={{ minWidth: 160 }}
                            />
                        </Stack>
                    )}
                    {list && <FormControlLabel control={<Switch checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />} label="Aktif" />}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    Kaydet
                </Button>
            </DialogActions>
        </Dialog>
    );
}
