"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import IconButton from "@mui/material/IconButton";
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
import { IconTrash } from "@tabler/icons-react";
import { catalog, type Money4, type ProductDto, type VariantDto } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { parseAmount } from "@/lib/money";

const MAX_ATTRIBUTES = 3;
const MAX_VALUES = 50;
const MAX_VARIANTS = 500;

type AttributeRow = { name: string; values: string[] };

type VariantsTabProps = {
    product: ProductDto;
    canManage: boolean;
    onChanged: (product: ProductDto) => void;
};

const amountText = (m: Money4 | null) => (m ? String(m.amount).replace(".", ",") : "");

/** Varyantlar sekmesi: özellik düzenleyici (ad + değer çipleri), canlı varyant sayısı ve varyant tablosu. */
export default function VariantsTab({ product, canManage, onChanged }: VariantsTabProps) {
    const [attrs, setAttrs] = useState<AttributeRow[]>(() => product.attributes.map((a) => ({ name: a.name, values: a.values })));
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [saved, setSaved] = useState(false);

    const used = attrs.filter((a) => a.name.trim() && a.values.length > 0);
    const count = used.length === 0 ? 0 : used.reduce((n, a) => n * a.values.length, 1);
    const tooMany = count > MAX_VARIANTS;

    const update = (index: number, patch: Partial<AttributeRow>) => {
        setSaved(false);
        setAttrs((current) => current.map((a, i) => (i === index ? { ...a, ...patch } : a)));
    };

    async function save() {
        setBusy(true);
        setError(null);
        setSaved(false);
        try {
            const next = await catalog.setAttributes(
                product.id,
                used.map((a) => ({ name: a.name.trim(), values: a.values })),
            );
            onChanged(next);
            setSaved(true);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const attributeNames = product.attributes.map((a) => a.name);

    return (
        <Stack spacing={3}>
            <Box>
                <Typography variant="h6" sx={{ mb: 1 }}>
                    Özellikler
                </Typography>
                <Stack spacing={2}>
                    {attrs.map((attr, i) => (
                        <Box key={i} sx={{ display: "flex", gap: 1.5, alignItems: "flex-start", flexWrap: "wrap" }}>
                            <TextField
                                size="small"
                                label={`${i + 1}. özellik adı`}
                                placeholder="Beden"
                                value={attr.name}
                                disabled={!canManage}
                                onChange={(e) => update(i, { name: e.target.value })}
                                sx={{ width: 200 }}
                            />
                            <Autocomplete
                                multiple
                                freeSolo
                                disabled={!canManage}
                                options={[] as string[]}
                                value={attr.values}
                                onChange={(_, values) => update(i, { values: [...new Set(values.map((v) => v.trim()).filter(Boolean))].slice(0, MAX_VALUES) })}
                                renderValue={(values, getItemProps) =>
                                    values.map((v, index) => {
                                        const { key, ...props } = getItemProps({ index });
                                        return <Chip key={key} size="small" label={v} {...props} />;
                                    })
                                }
                                renderInput={(params) => <TextField {...params} size="small" label={`${i + 1}. özellik değerleri`} placeholder="Değer yazıp Enter'a basın" />}
                                sx={{ flex: 1, minWidth: 260 }}
                            />
                            {canManage && (
                                <IconButton aria-label={`${i + 1}. özelliği kaldır`} onClick={() => setAttrs((c) => c.filter((_, j) => j !== i))}>
                                    <IconTrash size={18} />
                                </IconButton>
                            )}
                        </Box>
                    ))}
                    {canManage && (
                        <div>
                            <Button variant="outlined" disabled={attrs.length >= MAX_ATTRIBUTES} onClick={() => setAttrs((c) => [...c, { name: "", values: [] }])}>
                                Özellik ekle
                            </Button>
                        </div>
                    )}
                </Stack>
                <Typography sx={{ mt: 2 }} color={tooMany ? "error" : "text.secondary"} role="status">
                    {count === 0 ? "Varyant oluşmayacak" : `${count} varyant oluşacak`}
                    {tooMany && ` (en fazla ${MAX_VARIANTS})`}
                </Typography>
                {error && (
                    <Alert severity="error" sx={{ mt: 2 }}>
                        {error}
                    </Alert>
                )}
                {saved && !error && (
                    <Alert severity="success" sx={{ mt: 2 }}>
                        Özellikler kaydedildi.
                    </Alert>
                )}
                {canManage && (
                    <Button sx={{ mt: 2 }} variant="contained" disabled={busy || tooMany} onClick={() => void save()}>
                        {busy ? "Kaydediliyor..." : "Özellikleri kaydet"}
                    </Button>
                )}
            </Box>

            {product.variants.length > 0 && (
                <Box>
                    <Typography variant="h6" sx={{ mb: 1 }}>
                        Varyantlar ({product.variants.length})
                    </Typography>
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 900 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Varyant</TableCell>
                                    <TableCell>Kod</TableCell>
                                    <TableCell>Barkod</TableCell>
                                    <TableCell>Alış fiyatı</TableCell>
                                    <TableCell>Satış fiyatı</TableCell>
                                    <TableCell>Aktif</TableCell>
                                    <TableCell />
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {product.variants.map((variant) => (
                                    <VariantRow key={variant.id} product={product} variant={variant} attributeNames={attributeNames} canManage={canManage} onChanged={onChanged} />
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                </Box>
            )}
        </Stack>
    );
}

type VariantRowProps = {
    product: ProductDto;
    variant: VariantDto;
    attributeNames: string[];
    canManage: boolean;
    onChanged: (product: ProductDto) => void;
};

function VariantRow({ product, variant, attributeNames, canManage, onChanged }: VariantRowProps) {
    const [code, setCode] = useState(variant.code);
    const [barcode, setBarcode] = useState(variant.barcode ?? "");
    const [sale, setSale] = useState(amountText(variant.salePriceOverride));
    const [purchase, setPurchase] = useState(amountText(variant.purchasePriceOverride));
    const [errors, setErrors] = useState<{ code?: string; barcode?: string; sale?: string; purchase?: string; general?: string }>({});
    const [busy, setBusy] = useState(false);
    const label = attributeNames.map((n) => variant.values[n]).filter(Boolean).join(" / ");

    const money = (text: string, current: Money4 | null, fallback: Money4 | null): Money4 | null | "invalid" => {
        if (!text.trim()) return null;
        const amount = parseAmount(text);
        if (amount === null) return "invalid";
        const base = current ?? fallback;
        return { amount, currency: base?.currency ?? "TRY", vatIncluded: base?.vatIncluded ?? true };
    };

    async function save() {
        const next: typeof errors = {};
        if (code.trim().length < 1 || code.trim().length > 40) next.code = "Kod 1 ile 40 karakter arasında olmalı.";
        const salePrice = money(sale, variant.salePriceOverride, product.defaultSalePrice);
        const purchasePrice = money(purchase, variant.purchasePriceOverride, product.defaultPurchasePrice);
        if (salePrice === "invalid") next.sale = "Geçerli tutar";
        if (purchasePrice === "invalid") next.purchase = "Geçerli tutar";
        setErrors(next);
        if (Object.keys(next).length > 0 || salePrice === "invalid" || purchasePrice === "invalid") return;

        setBusy(true);
        try {
            onChanged(await catalog.updateVariant(product.id, variant.id, { code: code.trim(), barcode: barcode.trim() || null, salePriceOverride: salePrice, purchasePriceOverride: purchasePrice }));
        } catch (e) {
            const code2 = (e as { code?: string }).code;
            if (code2 === "code_taken") setErrors({ code: errorMessage(e) });
            else if (code2 === "barcode_taken") setErrors({ barcode: errorMessage(e) });
            else setErrors({ general: errorMessage(e) });
        } finally {
            setBusy(false);
        }
    }

    async function generate() {
        try {
            setBarcode((await catalog.generateBarcode(product.id)).barcode);
        } catch (e) {
            setErrors({ general: errorMessage(e) });
        }
    }

    async function toggle(active: boolean) {
        setBusy(true);
        try {
            await catalog.setVariantActive(product.id, variant.id, active);
            onChanged(await catalog.getProduct(product.id));
        } catch (e) {
            setErrors({ general: errorMessage(e) });
        } finally {
            setBusy(false);
        }
    }

    return (
        <TableRow sx={{ "& td": { verticalAlign: "top" } }}>
            <TableCell>
                {label || "—"}
                {errors.general && (
                    <Typography variant="caption" color="error" sx={{ display: "block" }}>
                        {errors.general}
                    </Typography>
                )}
            </TableCell>
            <TableCell>
                <TextField size="small" value={code} disabled={!canManage} onChange={(e) => setCode(e.target.value)} error={Boolean(errors.code)} helperText={errors.code} slotProps={{ htmlInput: { "aria-label": `${label} kodu` } }} sx={{ width: 150 }} />
            </TableCell>
            <TableCell>
                <Box sx={{ display: "flex", gap: 0.5 }}>
                    <TextField size="small" value={barcode} disabled={!canManage} onChange={(e) => setBarcode(e.target.value)} error={Boolean(errors.barcode)} helperText={errors.barcode} slotProps={{ htmlInput: { "aria-label": `${label} barkodu` } }} sx={{ width: 150 }} />
                    {canManage && (
                        <Button size="small" onClick={() => void generate()} aria-label={`${label} barkod üret`}>
                            Üret
                        </Button>
                    )}
                </Box>
            </TableCell>
            <TableCell>
                <TextField size="small" value={purchase} disabled={!canManage} placeholder="Ürün fiyatı" onChange={(e) => setPurchase(e.target.value)} error={Boolean(errors.purchase)} helperText={errors.purchase} slotProps={{ htmlInput: { inputMode: "decimal", "aria-label": `${label} alış fiyatı` } }} sx={{ width: 120 }} />
            </TableCell>
            <TableCell>
                <TextField size="small" value={sale} disabled={!canManage} placeholder="Ürün fiyatı" onChange={(e) => setSale(e.target.value)} error={Boolean(errors.sale)} helperText={errors.sale} slotProps={{ htmlInput: { inputMode: "decimal", "aria-label": `${label} satış fiyatı` } }} sx={{ width: 120 }} />
            </TableCell>
            <TableCell>
                <Switch checked={variant.isActive} disabled={!canManage || busy} onChange={(e) => void toggle(e.target.checked)} slotProps={{ input: { "aria-label": `${label} aktif` } }} />
            </TableCell>
            <TableCell>
                {canManage && (
                    <Button size="small" variant="outlined" disabled={busy} onClick={() => void save()} aria-label={`${label} kaydet`}>
                        Kaydet
                    </Button>
                )}
            </TableCell>
        </TableRow>
    );
}
