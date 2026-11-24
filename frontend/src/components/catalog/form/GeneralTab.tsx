"use client";

import { useState } from "react";
import Accordion from "@mui/material/Accordion";
import AccordionDetails from "@mui/material/AccordionDetails";
import AccordionSummary from "@mui/material/AccordionSummary";
import Autocomplete, { createFilterOptions } from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import FormControlLabel from "@mui/material/FormControlLabel";
import Grid from "@mui/material/Grid";
import MenuItem from "@mui/material/MenuItem";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconChevronDown } from "@tabler/icons-react";
import CurrencySelect from "@/components/cari/CurrencySelect";
import { catalog, flattenCategories, type BrandDto, type CategoryNode, type ProductKind } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import type { Errors, FormValues, MoneyValue } from "./model";

type BrandOption = BrandDto & { isNew?: boolean };
const filter = createFilterOptions<BrandOption>();

type GeneralTabProps = {
    v: FormValues;
    errors: Errors;
    creating: boolean;
    categories: CategoryNode[];
    brands: BrandDto[];
    unitOptions: string[];
    vatRates: number[];
    showSeriLot: boolean;
    canManageBrands: boolean;
    /** fiyat.alis izni yoksa alış fiyatı alanı hiç gösterilmez (sunucu da değeri zaten göndermez). */
    canSeePurchase: boolean;
    onChange: (patch: Partial<FormValues>) => void;
    onBrandCreated: (brand: BrandDto) => void;
    onGenerateBarcode: () => void;
    onKindChange: (kind: ProductKind) => void;
};

function MoneyFields({ label, value, onChange, error, idPrefix }: { label: string; value: MoneyValue; onChange: (m: MoneyValue) => void; error?: string; idPrefix: string }) {
    return (
        <>
            <Grid size={{ xs: 12, sm: 4 }}>
                <TextField
                    id={`${idPrefix}-amount`}
                    label={label}
                    fullWidth
                    value={value.amount}
                    onChange={(e) => onChange({ ...value, amount: e.target.value })}
                    error={Boolean(error)}
                    helperText={error}
                    slotProps={{ htmlInput: { inputMode: "decimal" } }}
                />
            </Grid>
            <Grid size={{ xs: 6, sm: 4 }}>
                <CurrencySelect id={`${idPrefix}-currency`} label={`${label} para birimi`} value={value.currency} onChange={(currency) => onChange({ ...value, currency })} />
            </Grid>
            <Grid size={{ xs: 6, sm: 4 }}>
                <TextField
                    select
                    id={`${idPrefix}-vat`}
                    label={`${label} KDV`}
                    fullWidth
                    value={value.vatIncluded ? "included" : "excluded"}
                    onChange={(e) => onChange({ ...value, vatIncluded: e.target.value === "included" })}
                >
                    <MenuItem value="included">KDV dahil</MenuItem>
                    <MenuItem value="excluded">KDV hariç</MenuItem>
                </TextField>
            </Grid>
        </>
    );
}

/** Genel sekmesi: kart kimliği, sınıflandırma, birim/KDV, fiyatlar, ölçüyle satış ve teknik alanlar. */
export default function GeneralTab(props: GeneralTabProps) {
    const { v, errors, creating, categories, brands, unitOptions, vatRates, showSeriLot, canManageBrands, canSeePurchase, onChange, onBrandCreated, onGenerateBarcode, onKindChange } = props;
    const service = v.kind === "service";
    const [brandError, setBrandError] = useState<string | null>(null);
    const categoryOptions = flattenCategories(categories);
    const selectedBrand = brands.find((b) => b.id === v.brandId) ?? null;

    const text = (key: keyof FormValues & string, label: string, extra: Partial<React.ComponentProps<typeof TextField>> = {}) => (
        <TextField id={key} label={label} fullWidth value={v[key] as string} onChange={(e) => onChange({ [key]: e.target.value })} error={Boolean(errors[key])} helperText={errors[key]} {...extra} />
    );

    async function pickBrand(option: BrandOption | null) {
        setBrandError(null);
        if (!option) return onChange({ brandId: "" });
        if (!option.isNew) return onChange({ brandId: option.id });
        try {
            const created = await catalog.createBrand(option.name);
            onBrandCreated(created);
            onChange({ brandId: created.id });
        } catch (e) {
            setBrandError(errorMessage(e));
        }
    }

    return (
        <Grid container spacing={2.5}>
            {creating && (
                <Grid size={12}>
                    <RadioGroup row aria-label="Kart türü" value={v.kind} onChange={(e) => onKindChange(e.target.value as ProductKind)}>
                        <FormControlLabel value="product" control={<Radio />} label="Ürün" />
                        <FormControlLabel value="service" control={<Radio />} label="Hizmet" />
                    </RadioGroup>
                </Grid>
            )}
            <Grid size={{ xs: 12, sm: 4 }}>{text("code", "Kod")}</Grid>
            <Grid size={{ xs: 12, sm: 8 }}>{text("name", "Ad")}</Grid>
            <Grid size={12}>{text("description", "Açıklama", { multiline: true, minRows: 2 })}</Grid>

            <Grid size={{ xs: 12, sm: 6 }}>
                <TextField select id="categoryId" label="Kategori" fullWidth value={v.categoryId} onChange={(e) => onChange({ categoryId: e.target.value })} slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
                    <option value="">Kategorisiz</option>
                    {categoryOptions.map(({ node, depth }) => (
                        <option key={node.id} value={node.id}>
                            {`${"— ".repeat(depth)}${node.name}`}
                        </option>
                    ))}
                </TextField>
            </Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
                <Autocomplete<BrandOption>
                    options={brands}
                    value={selectedBrand}
                    getOptionLabel={(o) => (o.isNew ? `+ Yeni marka: ${o.name}` : o.name)}
                    isOptionEqualToValue={(a, b) => a.id === b.id}
                    filterOptions={(options, state) => {
                        const filtered = filter(options, state);
                        const typed = state.inputValue.trim();
                        if (canManageBrands && typed && !options.some((o) => o.name.toLocaleLowerCase("tr") === typed.toLocaleLowerCase("tr"))) {
                            filtered.push({ id: "__new__", name: typed, productCount: 0, isNew: true });
                        }
                        return filtered;
                    }}
                    onChange={(_, option) => void pickBrand(option)}
                    noOptionsText="Marka bulunamadı"
                    renderInput={(params) => <TextField {...params} label="Marka" error={Boolean(brandError)} helperText={brandError} />}
                />
            </Grid>

            <Grid size={{ xs: 12, sm: 4 }}>
                <TextField select id="baseUnit" label="Temel birim" fullWidth value={v.soldByMeasure ? v.measureUnit : v.baseUnit} disabled={v.soldByMeasure} onChange={(e) => onChange({ baseUnit: e.target.value })}>
                    {[...new Set([...unitOptions, v.baseUnit, ...(v.soldByMeasure ? [v.measureUnit] : [])])].map((u) => (
                        <MenuItem key={u} value={u}>
                            {u}
                        </MenuItem>
                    ))}
                </TextField>
            </Grid>
            <Grid size={{ xs: 12, sm: 4 }}>
                <TextField select id="vatRate" label="KDV oranı" fullWidth value={v.vatRate} onChange={(e) => onChange({ vatRate: e.target.value })}>
                    {vatRates.map((r) => (
                        <MenuItem key={r} value={String(r)}>
                            %{r}
                        </MenuItem>
                    ))}
                </TextField>
            </Grid>
            <Grid size={{ xs: 12, sm: 4 }}>
                <Box sx={{ display: "flex", gap: 1 }}>
                    {text("barcode", "Barkod")}
                    {!creating && (
                        <Button variant="outlined" onClick={onGenerateBarcode} sx={{ whiteSpace: "nowrap", alignSelf: "flex-start", height: 56 }}>
                            Barkod üret
                        </Button>
                    )}
                </Box>
            </Grid>

            {canSeePurchase && <MoneyFields label="Alış fiyatı" idPrefix="purchase" value={v.purchase} error={errors.purchaseAmount} onChange={(purchase) => onChange({ purchase })} />}
            <MoneyFields label="Satış fiyatı" idPrefix="sale" value={v.sale} error={errors.saleAmount} onChange={(sale) => onChange({ sale })} />

            {!service && (
                <Grid size={12}>
                    <FormControlLabel
                        control={<Switch checked={v.soldByMeasure} onChange={(e) => onChange({ soldByMeasure: e.target.checked, baseUnit: e.target.checked ? v.measureUnit : v.baseUnit })} />}
                        label="Ölçüyle satılır"
                    />
                    {v.soldByMeasure && (
                        <Box sx={{ mt: 1, display: "flex", gap: 2, alignItems: "center", flexWrap: "wrap" }}>
                            <TextField select size="small" label="Ölçü birimi" value={v.measureUnit} onChange={(e) => onChange({ measureUnit: e.target.value as "m2" | "m", baseUnit: e.target.value })} sx={{ width: 160 }}>
                                <MenuItem value="m2">m²</MenuItem>
                                <MenuItem value="m">metre</MenuItem>
                            </TextField>
                            <Typography color="text.secondary">Satışta en × boy girilir. Temel birim ölçü birimine eşitlenir.</Typography>
                        </Box>
                    )}
                </Grid>
            )}
            {!service && showSeriLot && (
                <Grid size={12}>
                    <FormControlLabel control={<Switch checked={v.tracksSerialLot} onChange={(e) => onChange({ tracksSerialLot: e.target.checked })} />} label="Seri/lot takibi" />
                </Grid>
            )}

            <Grid size={12}>
                <Autocomplete
                    multiple
                    freeSolo
                    options={[] as string[]}
                    value={v.tags}
                    onChange={(_, tags) => onChange({ tags: [...new Set(tags.map((t) => t.trim()).filter(Boolean))] })}
                    renderValue={(values, getItemProps) =>
                        values.map((tag, index) => {
                            const { key, ...itemProps } = getItemProps({ index });
                            return <Chip key={key} size="small" label={tag} {...itemProps} />;
                        })
                    }
                    renderInput={(params) => <TextField {...params} label="Etiketler" placeholder="Etiket yazıp Enter'a basın" />}
                />
            </Grid>

            <Grid size={12}>
                <Accordion disableGutters variant="outlined">
                    <AccordionSummary expandIcon={<IconChevronDown size={18} />}>
                        <Typography>Teknik alanlar</Typography>
                    </AccordionSummary>
                    <AccordionDetails>
                        <Grid container spacing={2}>
                            <Grid size={{ xs: 6, sm: 3 }}>{text("weightKg", "Ağırlık (kg)")}</Grid>
                            <Grid size={{ xs: 6, sm: 3 }}>{text("widthCm", "En (cm)")}</Grid>
                            <Grid size={{ xs: 6, sm: 3 }}>{text("lengthCm", "Boy (cm)")}</Grid>
                            <Grid size={{ xs: 6, sm: 3 }}>{text("heightCm", "Yükseklik (cm)")}</Grid>
                            <Grid size={{ xs: 12, sm: 6 }}>{text("originCountry", "Menşei ülke")}</Grid>
                            <Grid size={{ xs: 12, sm: 6 }}>{text("gtip", "GTİP")}</Grid>
                        </Grid>
                    </AccordionDetails>
                </Accordion>
            </Grid>
        </Grid>
    );
}
