"use client";

import { useCallback, useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconPencil, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import CurrencySelect from "@/components/cari/CurrencySelect";
import DashboardCard from "@/components/dashboard/DashboardCard";
import type { SellableItem } from "@/lib/api/catalog";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type CustomerPriceDto } from "@/lib/api/pricing";
import type { Currency } from "@/lib/money";
import ItemPicker, { itemLabel } from "./ItemPicker";
import { validityText } from "./PriceLists";
import TierEditor from "./TierEditor";
import { emptyTiers, parseTiers, summarizeTiers, toTierText, validateTiers, type TierText } from "./tiers";

type CustomerPricesTabProps = {
    account: CurrentAccountDto;
    canManage: boolean;
};

type Draft = {
    price?: CustomerPriceDto;
    item: SellableItem | null;
    unit: string;
    currency: Currency;
    vatIncluded: boolean;
    tiers: TierText[];
    validFrom: string;
    validTo: string;
};

const unitsOf = (item: SellableItem) => [...new Set([item.baseUnit, item.measureUnit, ...item.units.map((u) => u.unit)].filter((u): u is string => Boolean(u)))];

/** Cari detayındaki "Özel fiyatlar" sekmesi: müşteriye özel kademeli fiyatlar. Çakışan tarihli fiyat reddedilir. */
export default function CustomerPricesTab({ account, canManage }: CustomerPricesTabProps) {
    const [prices, setPrices] = useState<CustomerPriceDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [dialogError, setDialogError] = useState<string | null>(null);
    const [fieldError, setFieldError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [deleting, setDeleting] = useState<CustomerPriceDto | null>(null);

    const load = useCallback(async () => {
        try {
            setPrices(await pricing.customerPrices(account.id));
        } catch (e) {
            setError(errorMessage(e));
            setPrices((current) => current ?? []);
        }
    }, [account.id]);

    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [load]);

    const openNew = () => {
        setDraft({ item: null, unit: "", currency: account.currency, vatIncluded: false, tiers: emptyTiers(), validFrom: "", validTo: "" });
        setDialogError(null);
        setFieldError(null);
    };

    const openEdit = (price: CustomerPriceDto) => {
        setDraft({
            price,
            item: null,
            unit: price.unit,
            currency: price.currency,
            vatIncluded: price.vatIncluded,
            tiers: toTierText(price.tiers),
            validFrom: price.validFrom ?? "",
            validTo: price.validTo ?? "",
        });
        setDialogError(null);
        setFieldError(null);
    };

    async function save() {
        if (!draft) return;
        const target = draft.price ?? (draft.item ? { productId: draft.item.productId, variantId: draft.item.variantId } : null);
        if (!target) {
            setFieldError("Önce bir kalem seçin.");
            return;
        }
        const message = validateTiers(draft.tiers);
        if (message) {
            setFieldError(message);
            return;
        }
        if (draft.validFrom && draft.validTo && draft.validFrom > draft.validTo) {
            setFieldError("Geçerlilik başlangıcı bitişten sonra olamaz.");
            return;
        }
        setFieldError(null);
        setDialogError(null);
        setBusy(true);
        try {
            const body = {
                productId: target.productId,
                variantId: target.variantId,
                unit: draft.unit,
                currency: draft.currency,
                vatIncluded: draft.vatIncluded,
                tiers: parseTiers(draft.tiers)!,
                validFrom: draft.validFrom || null,
                validTo: draft.validTo || null,
            };
            if (draft.price) await pricing.updateCustomerPrice(account.id, draft.price.id, body);
            else await pricing.createCustomerPrice(account.id, body);
            setDraft(null);
            await load();
        } catch (e) {
            setDialogError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setError(null);
        try {
            await pricing.deleteCustomerPrice(account.id, target.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    return (
        <DashboardCard
            title="Özel fiyatlar"
            subtitle="Bu müşteriye liste fiyatlarından bağımsız tanımlanan fiyatlar; satışta her zaman önceliklidir."
            action={
                canManage && (
                    <Button variant="contained" onClick={openNew}>
                        Özel fiyat ekle
                    </Button>
                )
            }
        >
            <FormAlert error={error} />
            {prices === null ? (
                <Skeleton variant="rounded" height={100} />
            ) : prices.length === 0 ? (
                <Typography color="text.secondary">Bu müşteri için özel fiyat tanımlanmamış.</Typography>
            ) : (
                <TableContainer>
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Kalem</TableCell>
                                <TableCell>Birim</TableCell>
                                <TableCell>Fiyat</TableCell>
                                <TableCell>Para birimi</TableCell>
                                <TableCell>Geçerlilik</TableCell>
                                <TableCell align="right">İşlem</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {prices.map((p) => (
                                <TableRow key={p.id} hover>
                                    <TableCell>
                                        {p.productCode} · {p.productName}
                                        {p.variantCode && <Typography variant="caption" color="text.secondary" sx={{ display: "block" }}>Varyant: {p.variantCode}</Typography>}
                                    </TableCell>
                                    <TableCell>{p.unit}</TableCell>
                                    <TableCell>
                                        {summarizeTiers(p.tiers)} {p.vatIncluded ? "(KDV dahil)" : "(KDV hariç)"}
                                    </TableCell>
                                    <TableCell>{p.currency}</TableCell>
                                    <TableCell>{validityText(p.validFrom, p.validTo)}</TableCell>
                                    <TableCell align="right">
                                        {canManage && (
                                            <>
                                                <IconButton aria-label={`${p.productCode} özel fiyatını düzenle`} size="small" onClick={() => openEdit(p)}>
                                                    <IconPencil size={18} />
                                                </IconButton>
                                                <IconButton aria-label={`${p.productCode} özel fiyatını sil`} size="small" onClick={() => setDeleting(p)}>
                                                    <IconTrash size={18} />
                                                </IconButton>
                                            </>
                                        )}
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}

            <Dialog open={draft !== null} onClose={() => setDraft(null)} fullWidth maxWidth="sm">
                <DialogTitle>{draft?.price ? "Özel fiyatı düzenle" : "Özel fiyat ekle"}</DialogTitle>
                <DialogContent>
                    {draft && (
                        <Stack spacing={2.5} sx={{ pt: 1 }}>
                            <FormAlert error={dialogError} />
                            {draft.price ? (
                                <Typography>
                                    {draft.price.productCode} · {draft.price.productName} ({draft.price.unit})
                                </Typography>
                            ) : (
                                <>
                                    <ItemPicker
                                        label="Kalem"
                                        size="medium"
                                        value={draft.item}
                                        onPick={(item) => setDraft({ ...draft, item, unit: item.soldByMeasure && item.measureUnit ? item.measureUnit : item.baseUnit })}
                                    />
                                    {draft.item && (
                                        <TextField select label="Birim" value={draft.unit} onChange={(e) => setDraft({ ...draft, unit: e.target.value })}>
                                            {unitsOf(draft.item).map((u) => (
                                                <MenuItem key={u} value={u}>
                                                    {u}
                                                </MenuItem>
                                            ))}
                                        </TextField>
                                    )}
                                    {draft.item && <Typography variant="caption" color="text.secondary">Seçilen: {itemLabel(draft.item)}</Typography>}
                                </>
                            )}
                            <Stack direction="row" spacing={2}>
                                <CurrencySelect value={draft.currency} onChange={(currency) => setDraft({ ...draft, currency })} id="customer-price-currency" />
                                <FormControlLabel control={<Switch checked={draft.vatIncluded} onChange={(e) => setDraft({ ...draft, vatIncluded: e.target.checked })} />} label="KDV dahil" sx={{ whiteSpace: "nowrap" }} />
                            </Stack>
                            <TierEditor tiers={draft.tiers} unit={draft.unit} onChange={(tiers) => setDraft({ ...draft, tiers })} />
                            <Stack direction="row" spacing={2}>
                                <TextField label="Geçerlilik başlangıcı" type="date" fullWidth value={draft.validFrom} onChange={(e) => setDraft({ ...draft, validFrom: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
                                <TextField label="Geçerlilik bitişi" type="date" fullWidth value={draft.validTo} onChange={(e) => setDraft({ ...draft, validTo: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
                            </Stack>
                            {fieldError && (
                                <Typography color="error" variant="body2">
                                    {fieldError}
                                </Typography>
                            )}
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDraft(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy} onClick={() => void save()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Özel fiyat silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        {deleting?.productCode} · {deleting?.productName} için bu müşteriye tanımlı fiyat silinecek.
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>
        </DashboardCard>
    );
}
