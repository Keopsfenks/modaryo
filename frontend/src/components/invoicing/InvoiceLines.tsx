"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import LotSerialDialog, { type TrackingResult } from "@/components/inventory/LotSerialDialog";
import ItemPicker from "@/components/pricing/ItemPicker";
import { catalog, type SellableItem } from "@/lib/api/catalog";
import type { InvoiceCodes } from "@/lib/api/invoicing";
import { pricing } from "@/lib/api/pricing";
import { lineQty, measureQuantity, newInvoiceLine, VAT_RATES, type InvoiceLineDraft } from "./invoiceModel";

type InvoiceLinesProps = {
    lines: InvoiceLineDraft[];
    onChange(lines: InvoiceLineDraft[]): void;
    codes: InvoiceCodes | null;
    purchase: boolean;
    currentAccountId: string;
    date: string;
    currency: string;
    /** Stok etkili faturada lot takipli kalemler için lot seçimi gösterilir. */
    affectsStock: boolean;
    warehouseId: string;
    disabled?: boolean;
};

const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label } });

/** Fatura satır tablosu (P3 kalem seçici üzerine): fiyat otomatik dolar, ölçü kalemlerinde en/boy, iskonto, KDV, tevkifat ve istisna. */
export default function InvoiceLines({ lines, onChange, codes, purchase, currentAccountId, date, currency, affectsStock, warehouseId, disabled }: InvoiceLinesProps) {
    const [lotKey, setLotKey] = useState<string | null>(null);
    const lotLine = lines.find((l) => l.key === lotKey) ?? null;

    const patch = (key: string, change: Partial<InvoiceLineDraft>) => onChange(lines.map((l) => (l.key === key ? { ...l, ...change, error: null } : l)));

    async function add(item: SellableItem) {
        const line = newInvoiceLine(item);
        if (item.tracksSerialLot) {
            try {
                line.trackingMode = (await catalog.getProduct(item.productId)).trackingMode ?? "lot";
            } catch {
                line.trackingMode = "lot";
            }
        }
        // Fiyat: satışta satış, alışta alış önceliği (P2 teklifi). Fiyat yoksa ya da para birimi farklıysa boş kalır.
        try {
            const quote = await pricing.quote({
                kind: purchase ? "purchase" : "sale",
                currentAccountId: currentAccountId || undefined,
                productId: item.productId,
                variantId: item.variantId ?? undefined,
                unit: line.unit,
                quantity: 1,
                date: date || undefined,
            });
            if (quote.source !== "none" && quote.currency === currency) {
                line.unitPrice = String(quote.unitPrice).replace(".", ",");
                line.vatIncluded = quote.vatIncluded;
            }
        } catch {
            /* fiyat bulunamadı: kullanıcı girer */
        }
        onChange([...lines, line]);
    }

    const changeVat = (line: InvoiceLineDraft, vat: number) =>
        patch(line.key, vat === 0 ? { vatRate: vat } : { vatRate: vat, exemptionCode: "", exemptionReason: "" });

    return (
        <>
            {!disabled && <ItemPicker label="Kalem ekle" onPick={(item) => void add(item)} clearOnPick />}
            {lines.length === 0 ? (
                <Typography color="text.secondary" sx={{ py: 2 }}>
                    Henüz satır yok. Kalem arayıp ekleyin.
                </Typography>
            ) : (
                <TableContainer sx={{ mt: 2 }}>
                    <Table size="small" sx={{ minWidth: 1100 }}>
                        <TableHead>
                            <TableRow>
                                <TableCell>Kalem</TableCell>
                                <TableCell sx={{ width: 150 }}>Miktar</TableCell>
                                <TableCell sx={{ width: 100 }}>Birim</TableCell>
                                <TableCell sx={{ width: 150 }}>Birim fiyat</TableCell>
                                <TableCell sx={{ width: 170 }}>İskonto</TableCell>
                                <TableCell sx={{ width: 90 }}>KDV %</TableCell>
                                <TableCell sx={{ width: 170 }}>Tevkifat</TableCell>
                                <TableCell sx={{ width: 190 }}>İstisna</TableCell>
                                <TableCell />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {lines.map((line) => {
                                const label = line.code || line.name;
                                const measured = line.soldByMeasure;
                                const byLength = line.measureUnit?.toLowerCase() === "m";
                                const qty = measureQuantity(line);
                                const exemptionOptions = codes?.exemption ?? [];
                                const withholdingOptions = codes?.withholding ?? [];
                                return (
                                    <TableRow key={line.key} sx={line.error ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                        <TableCell>
                                            <Typography variant="body2">{line.code ? `${line.code} · ${line.name}` : line.name}</Typography>
                                            {line.error && (
                                                <Typography variant="caption" role="alert">
                                                    {line.error}
                                                </Typography>
                                            )}
                                        </TableCell>
                                        <TableCell>
                                            {measured ? (
                                                <>
                                                    {!byLength && (
                                                        <TextField
                                                            size="small"
                                                            placeholder="En"
                                                            value={line.width}
                                                            disabled={disabled}
                                                            onChange={(e) => patch(line.key, { width: e.target.value })}
                                                            slotProps={{ htmlInput: { "aria-label": `${label} en`, inputMode: "decimal" } }}
                                                            sx={{ mb: 0.5 }}
                                                        />
                                                    )}
                                                    <TextField
                                                        size="small"
                                                        placeholder="Boy"
                                                        value={line.length}
                                                        disabled={disabled}
                                                        onChange={(e) => patch(line.key, { length: e.target.value })}
                                                        slotProps={{ htmlInput: { "aria-label": `${label} boy`, inputMode: "decimal" } }}
                                                    />
                                                    <Typography variant="caption" color="text.secondary" component="div">
                                                        {qty !== null ? `${String(qty).replace(".", ",")} ${byLength ? "m" : "m²"}` : "Ölçü girin"}
                                                    </Typography>
                                                </>
                                            ) : (
                                                <TextField
                                                    size="small"
                                                    value={line.quantity}
                                                    disabled={disabled}
                                                    error={line.quantity.trim() !== "" && lineQty(line) === null}
                                                    onChange={(e) => patch(line.key, { quantity: e.target.value })}
                                                    slotProps={{ htmlInput: { "aria-label": `${label} miktar`, inputMode: "decimal" } }}
                                                />
                                            )}
                                            {affectsStock && line.tracksSerialLot && line.trackingMode === "lot" && (
                                                <Button size="small" sx={{ mt: 0.5 }} disabled={disabled || lineQty(line) === null || !warehouseId} onClick={() => setLotKey(line.key)}>
                                                    Lot ({line.lots.length})
                                                </Button>
                                            )}
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                select
                                                size="small"
                                                value={line.unit}
                                                disabled={disabled}
                                                onChange={(e) => patch(line.key, { unit: e.target.value, lots: [] })}
                                                slotProps={nativeSelect(`${label} birim`)}
                                                fullWidth
                                            >
                                                {[line.baseUnit, ...line.units.map((u) => u.unit).filter((u) => u !== line.baseUnit)].map((u) => (
                                                    <option key={u} value={u}>
                                                        {u}
                                                    </option>
                                                ))}
                                            </TextField>
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                size="small"
                                                value={line.unitPrice}
                                                disabled={disabled}
                                                onChange={(e) => patch(line.key, { unitPrice: e.target.value })}
                                                slotProps={{ htmlInput: { "aria-label": `${label} birim fiyat`, inputMode: "decimal" } }}
                                            />
                                            <FormControlLabel
                                                sx={{ display: "block", mr: 0 }}
                                                control={<Checkbox size="small" checked={line.vatIncluded} disabled={disabled} onChange={(e) => patch(line.key, { vatIncluded: e.target.checked })} />}
                                                label={<Typography variant="caption">KDV dahil</Typography>}
                                            />
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                select
                                                size="small"
                                                value={line.discountMode}
                                                disabled={disabled}
                                                onChange={(e) => patch(line.key, { discountMode: e.target.value as "percent" | "amount" })}
                                                slotProps={nativeSelect(`${label} iskonto türü`)}
                                                sx={{ width: 64, mr: 0.5 }}
                                            >
                                                <option value="percent">%</option>
                                                <option value="amount">Tutar</option>
                                            </TextField>
                                            <TextField
                                                size="small"
                                                value={line.discountValue}
                                                disabled={disabled}
                                                onChange={(e) => patch(line.key, { discountValue: e.target.value })}
                                                slotProps={{ htmlInput: { "aria-label": `${label} iskonto`, inputMode: "decimal" } }}
                                                sx={{ width: 90 }}
                                            />
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                select
                                                size="small"
                                                value={String(line.vatRate)}
                                                disabled={disabled}
                                                onChange={(e) => changeVat(line, Number(e.target.value))}
                                                slotProps={nativeSelect(`${label} KDV`)}
                                            >
                                                {VAT_RATES.map((r) => (
                                                    <option key={r} value={r}>
                                                        {r}
                                                    </option>
                                                ))}
                                            </TextField>
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                select
                                                size="small"
                                                value={line.withholdingCode}
                                                disabled={disabled || !!line.exemptionCode}
                                                onChange={(e) => patch(line.key, { withholdingCode: e.target.value })}
                                                slotProps={nativeSelect(`${label} tevkifat`)}
                                                fullWidth
                                            >
                                                <option value="">Yok</option>
                                                {withholdingOptions.map((c) => (
                                                    <option key={c.code} value={c.code}>
                                                        {c.code} ({c.tenths}/10) {c.name}
                                                    </option>
                                                ))}
                                            </TextField>
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                select
                                                size="small"
                                                value={line.exemptionCode}
                                                disabled={disabled || line.vatRate !== 0 || !!line.withholdingCode}
                                                onChange={(e) => patch(line.key, { exemptionCode: e.target.value, exemptionReason: e.target.value ? line.exemptionReason : "" })}
                                                slotProps={nativeSelect(`${label} istisna`)}
                                                helperText={line.vatRate !== 0 ? "Yalnızca KDV %0" : undefined}
                                                fullWidth
                                            >
                                                <option value="">Yok</option>
                                                {exemptionOptions.map((c) => (
                                                    <option key={c.code} value={c.code}>
                                                        {c.code} {c.name}
                                                    </option>
                                                ))}
                                            </TextField>
                                            {line.exemptionCode && (
                                                <TextField
                                                    size="small"
                                                    placeholder="İstisna gerekçesi"
                                                    value={line.exemptionReason}
                                                    disabled={disabled}
                                                    onChange={(e) => patch(line.key, { exemptionReason: e.target.value })}
                                                    slotProps={{ htmlInput: { "aria-label": `${label} istisna gerekçesi` } }}
                                                    sx={{ mt: 0.5 }}
                                                    fullWidth
                                                />
                                            )}
                                        </TableCell>
                                        <TableCell align="right">
                                            {!disabled && (
                                                <IconButton aria-label={`${label} satırını kaldır`} onClick={() => onChange(lines.filter((l) => l.key !== line.key))}>
                                                    <IconTrash size={18} />
                                                </IconButton>
                                            )}
                                        </TableCell>
                                    </TableRow>
                                );
                            })}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}
            {lotLine && lotLine.productId && lineQty(lotLine) !== null && (
                <LotSerialDialog
                    open
                    mode={purchase ? "in" : "out"}
                    trackingMode="lot"
                    productId={lotLine.productId}
                    variantId={lotLine.variantId}
                    warehouseId={warehouseId}
                    quantity={lineQty(lotLine) ?? 0}
                    title={`${lotLine.code} · Lot bilgisi`}
                    initialLots={lotLine.lots}
                    onClose={() => setLotKey(null)}
                    onSave={(result: TrackingResult) => patch(lotLine.key, { lots: result.lots ?? [] })}
                />
            )}
        </>
    );
}
