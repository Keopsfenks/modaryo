"use client";

import { useEffect, useState } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import { catalog, flattenCategories, type ProductListItem } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type CampaignDay, type CampaignDto, type CampaignKind } from "@/lib/api/pricing";
import { parseAmount } from "@/lib/money";
import ItemPicker, { itemLabel } from "./ItemPicker";
import { useDebounced } from "./useDebounced";

export const DAYS: { value: CampaignDay; short: string; label: string }[] = [
    { value: "monday", short: "Pzt", label: "Pazartesi" },
    { value: "tuesday", short: "Sal", label: "Salı" },
    { value: "wednesday", short: "Çar", label: "Çarşamba" },
    { value: "thursday", short: "Per", label: "Perşembe" },
    { value: "friday", short: "Cum", label: "Cuma" },
    { value: "saturday", short: "Cmt", label: "Cumartesi" },
    { value: "sunday", short: "Paz", label: "Pazar" },
];

type Ref = { id: string; label: string };

type CampaignDialogProps = {
    open: boolean;
    campaign?: CampaignDto;
    onClose: () => void;
    onSaved: (campaign: CampaignDto) => void;
};

const hhmm = (time: string | null) => (time ? time.slice(0, 5) : "");
const withSeconds = (time: string) => (time ? `${time}:00` : null);
const shortId = (id: string) => id.slice(0, 8);

/** Kampanya oluşturma/düzenleme: X al Y öde ya da yüzde indirim, geçerlilik, gün ve saat penceresi, ürün/varyant/kategori kapsamı. */
export default function CampaignDialog({ open, campaign, onClose, onSaved }: CampaignDialogProps) {
    const [name, setName] = useState("");
    const [kind, setKind] = useState<CampaignKind>("buyXPayY");
    const [buyQty, setBuyQty] = useState("");
    const [payQty, setPayQty] = useState("");
    const [percent, setPercent] = useState("");
    const [validFrom, setValidFrom] = useState("");
    const [validTo, setValidTo] = useState("");
    const [days, setDays] = useState<CampaignDay[]>([]);
    const [fromTime, setFromTime] = useState("");
    const [toTime, setToTime] = useState("");
    const [products, setProducts] = useState<Ref[]>([]);
    const [variants, setVariants] = useState<Ref[]>([]);
    const [categories, setCategories] = useState<Ref[]>([]);
    const [categoryOptions, setCategoryOptions] = useState<Ref[]>([]);
    const [isActive, setIsActive] = useState(true);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [alert, setAlert] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    const [productInput, setProductInput] = useState("");
    const productSearch = useDebounced(productInput);
    const [productOptions, setProductOptions] = useState<ProductListItem[]>([]);

    useEffect(() => {
        if (!open) return;
        let ignore = false;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- pencere her açıldığında form başlangıç değerlerine döner
        setName(campaign?.name ?? "");
        setKind(campaign?.kind ?? "buyXPayY");
        setBuyQty(campaign?.buyQty != null ? String(campaign.buyQty) : "");
        setPayQty(campaign?.payQty != null ? String(campaign.payQty) : "");
        setPercent(campaign?.percent != null ? String(campaign.percent).replace(".", ",") : "");
        setValidFrom(campaign?.validFrom ?? "");
        setValidTo(campaign?.validTo ?? "");
        setDays(campaign?.days ?? []);
        setFromTime(hhmm(campaign?.fromTime ?? null));
        setToTime(hhmm(campaign?.toTime ?? null));
        setIsActive(campaign?.isActive ?? true);
        setErrors({});
        setAlert(null);
        setProducts((campaign?.productIds ?? []).map((id) => ({ id, label: `Ürün (${shortId(id)})` })));
        setVariants((campaign?.variantIds ?? []).map((id) => ({ id, label: `Varyant (${shortId(id)})` })));
        setCategories([]);

        catalog.categories().then(
            (tree) => {
                if (ignore) return;
                const flat = flattenCategories(tree).map(({ node, depth }) => ({ id: node.id, label: `${"— ".repeat(depth)}${node.name}` }));
                setCategoryOptions(flat);
                setCategories(flat.filter((c) => campaign?.categoryIds.includes(c.id)).map((c) => ({ id: c.id, label: c.label.replace(/^(— )+/, "") })));
            },
            () => undefined,
        );
        // Düzenlemede kayıtlı ürün kimlikleri okunabilir ada çevrilir.
        for (const id of campaign?.productIds ?? []) {
            catalog.getProduct(id).then(
                (p) => !ignore && setProducts((current) => current.map((r) => (r.id === id ? { id, label: `${p.code} · ${p.name}` } : r))),
                () => undefined,
            );
        }
        return () => {
            ignore = true;
        };
    }, [open, campaign]);

    useEffect(() => {
        if (!open || productSearch.length < 1) return;
        let ignore = false;
        catalog.listProducts({ search: productSearch, pageSize: 20 }).then(
            (r) => !ignore && setProductOptions(r.items),
            () => !ignore && setProductOptions([]),
        );
        return () => {
            ignore = true;
        };
    }, [open, productSearch]);

    const toggleDay = (day: CampaignDay) => setDays((current) => (current.includes(day) ? current.filter((d) => d !== day) : [...current, day]));

    async function submit() {
        const found: Record<string, string> = {};
        const trimmed = name.trim();
        if (!trimmed) found.name = "Kampanya adı zorunludur.";
        else if (trimmed.length > 200) found.name = "Kampanya adı en fazla 200 karakter olabilir.";

        let buy: number | null = null;
        let pay: number | null = null;
        let pct: number | null = null;
        if (kind === "buyXPayY") {
            buy = /^\d+$/.test(buyQty.trim()) ? Number(buyQty.trim()) : null;
            pay = /^\d+$/.test(payQty.trim()) ? Number(payQty.trim()) : null;
            if (buy === null || pay === null || pay < 1 || buy <= pay) found.buyQty = "X al Y öde için X, Y'den büyük ve Y en az 1 olmalıdır.";
        } else {
            pct = parseAmount(percent, { maxDecimals: 4 });
            if (pct === null || pct <= 0 || pct > 100) found.percent = "İndirim yüzdesi 0'dan büyük, en çok 100 olmalıdır.";
        }
        if (validFrom && validTo && validFrom > validTo) found.validTo = "Geçerlilik başlangıcı bitişten sonra olamaz.";
        if (Boolean(fromTime) !== Boolean(toTime)) found.time = "Saat penceresi için başlangıç ve bitiş birlikte verilmelidir.";
        if (products.length + variants.length + categories.length === 0) found.scope = "Kampanya için en az bir ürün, varyant ya da kategori seçin.";

        setErrors(found);
        setAlert(null);
        if (Object.keys(found).length > 0) return;

        setBusy(true);
        try {
            const body = {
                name: trimmed,
                kind,
                buyQty: kind === "buyXPayY" ? buy : null,
                payQty: kind === "buyXPayY" ? pay : null,
                percent: kind === "percentOff" ? pct : null,
                validFrom: validFrom || null,
                validTo: validTo || null,
                days: days.length > 0 ? DAYS.filter((d) => days.includes(d.value)).map((d) => d.value) : null,
                fromTime: withSeconds(fromTime),
                toTime: withSeconds(toTime),
                productIds: products.map((p) => p.id),
                variantIds: variants.map((v) => v.id),
                categoryIds: categories.map((c) => c.id),
                isActive,
            };
            onSaved(campaign ? await pricing.updateCampaign(campaign.id, body) : await pricing.createCampaign(body));
        } catch (e) {
            setAlert(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
            <DialogTitle>{campaign ? "Kampanyayı düzenle" : "Yeni kampanya"}</DialogTitle>
            <DialogContent>
                <Stack spacing={2.5} sx={{ pt: 1 }}>
                    <FormAlert error={alert} />
                    <TextField label="Kampanya adı" value={name} onChange={(e) => setName(e.target.value)} error={Boolean(errors.name)} helperText={errors.name} autoFocus />

                    <ToggleButtonGroup exclusive color="primary" value={kind} onChange={(_, next: CampaignKind | null) => next && setKind(next)} aria-label="Kampanya türü">
                        <ToggleButton value="buyXPayY">X al Y öde</ToggleButton>
                        <ToggleButton value="percentOff">% indirim</ToggleButton>
                    </ToggleButtonGroup>

                    {kind === "buyXPayY" ? (
                        <Stack direction="row" spacing={2}>
                            <TextField label="Alınan adet (X)" value={buyQty} onChange={(e) => setBuyQty(e.target.value)} error={Boolean(errors.buyQty)} helperText={errors.buyQty} slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ flex: 1 }} />
                            <TextField label="Ödenen adet (Y)" value={payQty} onChange={(e) => setPayQty(e.target.value)} slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ flex: 1 }} />
                        </Stack>
                    ) : (
                        <TextField label="İndirim yüzdesi (%)" value={percent} onChange={(e) => setPercent(e.target.value)} error={Boolean(errors.percent)} helperText={errors.percent} slotProps={{ htmlInput: { inputMode: "decimal" } }} sx={{ maxWidth: 260 }} />
                    )}

                    <Stack direction="row" spacing={2}>
                        <TextField label="Geçerlilik başlangıcı" type="date" fullWidth value={validFrom} onChange={(e) => setValidFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField label="Geçerlilik bitişi" type="date" fullWidth value={validTo} onChange={(e) => setValidTo(e.target.value)} error={Boolean(errors.validTo)} helperText={errors.validTo} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>

                    <div>
                        <Typography variant="subtitle2" sx={{ mb: 1 }}>
                            Günler (boşsa her gün)
                        </Typography>
                        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1 }}>
                            {DAYS.map((d) => (
                                <Chip key={d.value} label={d.short} aria-label={d.label} aria-pressed={days.includes(d.value)} color={days.includes(d.value) ? "primary" : "default"} variant={days.includes(d.value) ? "filled" : "outlined"} onClick={() => toggleDay(d.value)} />
                            ))}
                        </Stack>
                    </div>
                    <Stack direction="row" spacing={2}>
                        <TextField label="Başlangıç saati" type="time" value={fromTime} onChange={(e) => setFromTime(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} error={Boolean(errors.time)} helperText={errors.time ?? "İstanbul saati"} />
                        <TextField label="Bitiş saati" type="time" value={toTime} onChange={(e) => setToTime(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>

                    <Typography variant="subtitle2">Kapsam</Typography>
                    <Autocomplete
                        multiple
                        options={productOptions.map((p) => ({ id: p.id, label: `${p.code} · ${p.name}` }))}
                        value={products}
                        filterOptions={(x) => x}
                        getOptionLabel={(o) => o.label}
                        isOptionEqualToValue={(a, b) => a.id === b.id}
                        inputValue={productInput}
                        onInputChange={(_, text, reason) => reason !== "reset" && setProductInput(text)}
                        onChange={(_, next) => setProducts(next)}
                        noOptionsText="Ürün bulunamadı."
                        renderInput={(params) => <TextField {...params} label="Ürünler" error={Boolean(errors.scope)} helperText={errors.scope} />}
                    />
                    <Stack spacing={1}>
                        <ItemPicker
                            label="Varyant ekle"
                            variantsOnly
                            clearOnPick
                            size="medium"
                            onPick={(item) => setVariants((current) => (current.some((v) => v.id === item.variantId) ? current : [...current, { id: item.variantId!, label: itemLabel(item) }]))}
                        />
                        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1 }}>
                            {variants.map((v) => (
                                <Chip key={v.id} label={v.label} onDelete={() => setVariants((current) => current.filter((x) => x.id !== v.id))} />
                            ))}
                        </Stack>
                    </Stack>
                    <Autocomplete
                        multiple
                        options={categoryOptions}
                        value={categories}
                        getOptionLabel={(o) => o.label}
                        isOptionEqualToValue={(a, b) => a.id === b.id}
                        onChange={(_, next) => setCategories(next.map((c) => ({ id: c.id, label: c.label.replace(/^(— )+/, "") })))}
                        noOptionsText="Kategori yok."
                        renderInput={(params) => <TextField {...params} label="Kategoriler" helperText="Alt kategoriler de kapsama girer." />}
                    />

                    <FormControlLabel control={<Switch checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />} label="Aktif" />
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
