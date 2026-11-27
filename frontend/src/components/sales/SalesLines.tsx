"use client";

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
import ItemPicker from "@/components/pricing/ItemPicker";
import type { SellableItem } from "@/lib/api/catalog";
import { pricing } from "@/lib/api/pricing";
import type { SupplyMode } from "@/lib/api/sales";
import { useSession } from "@/session/SessionProvider";
import { ProfitCell, ProfitHeaderCell } from "./ProfitColumn";
import { lineQty, measureQty, newSalesLine, VAT_RATES, type SalesLineDraft } from "./salesModel";

export const BACK_TO_BACK_MODULE = "siparise-gore-tedarik";

type SalesLinesProps = {
    lines: SalesLineDraft[];
    onChange(lines: SalesLineDraft[]): void;
    currentAccountId: string;
    date: string;
    currency: string;
    /** Kâr sütunu (yalnızca kayıtlı siparişte anlamlıdır; maliyet.read ile görünür). */
    showProfit?: boolean;
    disabled?: boolean;
};

const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label } });

/**
 * Teklif ve sipariş satır tablosu: fiyat otomatik dolar (müşteri/liste/kart önceliği), ölçü kalemlerinde en x boy = m²,
 * iskonto, KDV, tedarik şekli (yalnızca "siparişe göre tedarik" modülü açıkken) ve kâr sütunu (maliyet.read).
 */
export default function SalesLines({ lines, onChange, currentAccountId, date, currency, showProfit, disabled }: SalesLinesProps) {
    const { hasModule } = useSession();
    const moduleOn = hasModule(BACK_TO_BACK_MODULE);

    const patch = (key: string, change: Partial<SalesLineDraft>) => onChange(lines.map((l) => (l.key === key ? { ...l, ...change, error: null } : l)));

    async function add(item: SellableItem) {
        const line = newSalesLine(item);
        try {
            const quote = await pricing.quote({
                kind: "sale",
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

    return (
        <>
            {!disabled && <ItemPicker label="Kalem ekle" onPick={(item) => void add(item)} clearOnPick />}
            {lines.length === 0 ? (
                <Typography color="text.secondary" sx={{ py: 2 }}>
                    Henüz satır yok. Kalem arayıp ekleyin.
                </Typography>
            ) : (
                <TableContainer sx={{ mt: 2 }}>
                    <Table size="small" sx={{ minWidth: 980 }}>
                        <TableHead>
                            <TableRow>
                                <TableCell>Kalem</TableCell>
                                <TableCell sx={{ width: 150 }}>Miktar</TableCell>
                                <TableCell sx={{ width: 100 }}>Birim</TableCell>
                                <TableCell sx={{ width: 150 }}>Birim fiyat</TableCell>
                                <TableCell sx={{ width: 100 }}>İskonto %</TableCell>
                                <TableCell sx={{ width: 90 }}>KDV %</TableCell>
                                {moduleOn && <TableCell sx={{ width: 170 }}>Tedarik</TableCell>}
                                {showProfit && <ProfitHeaderCell />}
                                <TableCell />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {lines.map((l) => {
                                const label = l.code || l.name;
                                const byLength = l.measureUnit?.toLowerCase() === "m";
                                const qty = measureQty(l);
                                return (
                                    <TableRow key={l.key} sx={l.error ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                        <TableCell>
                                            <Typography variant="body2">{l.code ? `${l.code} · ${l.name}` : l.name}</Typography>
                                            {l.error && (
                                                <Typography variant="caption" role="alert">
                                                    {l.error}
                                                </Typography>
                                            )}
                                        </TableCell>
                                        <TableCell>
                                            {l.soldByMeasure ? (
                                                <>
                                                    {!byLength && (
                                                        <TextField
                                                            size="small"
                                                            placeholder="En (m)"
                                                            value={l.width}
                                                            disabled={disabled}
                                                            onChange={(e) => patch(l.key, { width: e.target.value })}
                                                            slotProps={{ htmlInput: { "aria-label": `${label} en`, inputMode: "decimal" } }}
                                                            sx={{ mb: 0.5 }}
                                                        />
                                                    )}
                                                    <TextField
                                                        size="small"
                                                        placeholder="Boy (m)"
                                                        value={l.length}
                                                        disabled={disabled}
                                                        onChange={(e) => patch(l.key, { length: e.target.value })}
                                                        slotProps={{ htmlInput: { "aria-label": `${label} boy`, inputMode: "decimal" } }}
                                                    />
                                                    <Typography variant="caption" color="text.secondary" component="div">
                                                        {qty !== null ? `${qty.toLocaleString("tr-TR", { maximumFractionDigits: 4 })} ${byLength ? "m" : "m²"}` : "Ölçü girin"}
                                                    </Typography>
                                                </>
                                            ) : (
                                                <TextField
                                                    size="small"
                                                    value={l.quantity}
                                                    disabled={disabled}
                                                    error={l.quantity.trim() !== "" && lineQty(l) === null}
                                                    onChange={(e) => patch(l.key, { quantity: e.target.value })}
                                                    slotProps={{ htmlInput: { "aria-label": `${label} miktar`, inputMode: "decimal" } }}
                                                />
                                            )}
                                        </TableCell>
                                        <TableCell>
                                            <TextField select size="small" value={l.unit} disabled={disabled} onChange={(e) => patch(l.key, { unit: e.target.value })} slotProps={nativeSelect(`${label} birim`)} fullWidth>
                                                {l.units.map((u) => (
                                                    <option key={u} value={u}>
                                                        {u}
                                                    </option>
                                                ))}
                                            </TextField>
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                size="small"
                                                value={l.unitPrice}
                                                disabled={disabled}
                                                onChange={(e) => patch(l.key, { unitPrice: e.target.value })}
                                                slotProps={{ htmlInput: { "aria-label": `${label} birim fiyat`, inputMode: "decimal" } }}
                                            />
                                            <FormControlLabel
                                                sx={{ display: "block", mr: 0 }}
                                                control={<Checkbox size="small" checked={l.vatIncluded} disabled={disabled} onChange={(e) => patch(l.key, { vatIncluded: e.target.checked })} />}
                                                label={<Typography variant="caption">KDV dahil</Typography>}
                                            />
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                size="small"
                                                value={l.discountPercent}
                                                disabled={disabled}
                                                onChange={(e) => patch(l.key, { discountPercent: e.target.value })}
                                                slotProps={{ htmlInput: { "aria-label": `${label} iskonto`, inputMode: "decimal" } }}
                                            />
                                        </TableCell>
                                        <TableCell>
                                            <TextField select size="small" value={String(l.vatRate)} disabled={disabled} onChange={(e) => patch(l.key, { vatRate: Number(e.target.value) })} slotProps={nativeSelect(`${label} KDV`)}>
                                                {VAT_RATES.map((r) => (
                                                    <option key={r} value={r}>
                                                        {r}
                                                    </option>
                                                ))}
                                            </TextField>
                                        </TableCell>
                                        {moduleOn && (
                                            <TableCell>
                                                <TextField
                                                    select
                                                    size="small"
                                                    value={l.supplyMode}
                                                    disabled={disabled}
                                                    onChange={(e) => patch(l.key, { supplyMode: e.target.value as SupplyMode })}
                                                    slotProps={nativeSelect(`${label} tedarik şekli`)}
                                                    fullWidth
                                                >
                                                    <option value="stock">Stoktan</option>
                                                    <option value="backToBack">Siparişe göre tedarik</option>
                                                </TextField>
                                            </TableCell>
                                        )}
                                        {showProfit && <ProfitCell cost={l.estimatedUnitCost} margin={l.marginPercent} currency={currency} />}
                                        <TableCell align="right">
                                            {!disabled && (
                                                <IconButton aria-label={`${label} satırını kaldır`} onClick={() => onChange(lines.filter((x) => x.key !== l.key))}>
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
        </>
    );
}
