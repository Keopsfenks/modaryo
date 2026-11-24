"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import type { ProductDto } from "@/lib/api/catalog";
import { erp, type CurrentAccountListItem } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import { SOURCE_LABELS, pricing, type PriceItemDto, type PriceListDto, type PriceQuote } from "@/lib/api/pricing";
import { formatMoney, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import PriceHistory from "./PriceHistory";
import { summarizeTiers } from "./tiers";
import { useDebounced } from "./useDebounced";

type ListPrice = { list: PriceListDto; item: PriceItemDto };

/** Ürün kartının "Fiyatlar" sekmesi: kalemin tüm listelerdeki fiyatı, fiyat sorgulama aracı ve fiyat geçmişi. */
export default function ProductPricesTab({ product }: { product: ProductDto }) {
    const { hasPermission } = useSession();
    const canSeePurchase = hasPermission("fiyat.alis");

    const [lists, setLists] = useState<PriceListDto[] | null>(null);
    const [rows, setRows] = useState<ListPrice[]>([]);
    const [loadError, setLoadError] = useState<string | null>(null);

    useEffect(() => {
        let ignore = false;
        (async () => {
            try {
                const all = await pricing.lists();
                const found: ListPrice[] = [];
                // Kalem kodu benzersizdir; arama o koda daraltır, satırlar ürün kimliğiyle süzülür.
                for (const list of all) {
                    const page = await pricing.listItems(list.id, { search: product.code, pageSize: 50 });
                    for (const item of page.items) if (item.productId === product.id) found.push({ list, item });
                }
                if (!ignore) {
                    setLists(all);
                    setRows(found);
                }
            } catch (e) {
                if (!ignore) {
                    setLoadError(errorMessage(e));
                    setLists((current) => current ?? []);
                }
            }
        })();
        return () => {
            ignore = true;
        };
    }, [product.id, product.code]);

    const variantCode = (id: string | null) => (id ? (product.variants.find((v) => v.id === id)?.code ?? "—") : null);

    return (
        <Stack spacing={3}>
            <DashboardCard title="Fiyat listelerindeki fiyatlar" subtitle="Bu kalemin her listedeki fiyatı. Soluk satırlar taban listeden türetilmiştir.">
                <FormAlert error={loadError} />
                {lists === null ? (
                    <Skeleton variant="rounded" height={100} />
                ) : (
                    <TableContainer>
                        <Table size="small" aria-label="Listelerdeki fiyatlar">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Liste</TableCell>
                                    <TableCell>Varyant</TableCell>
                                    <TableCell>Birim</TableCell>
                                    <TableCell>Fiyat</TableCell>
                                    <TableCell>Para birimi</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {product.defaultSalePrice && (
                                    <TableRow>
                                        <TableCell>Ürün kartı (satış)</TableCell>
                                        <TableCell />
                                        <TableCell>{product.baseUnit}</TableCell>
                                        <TableCell>{formatMoney(product.defaultSalePrice.amount, product.defaultSalePrice.currency)}</TableCell>
                                        <TableCell>{product.defaultSalePrice.currency}</TableCell>
                                    </TableRow>
                                )}
                                {canSeePurchase && product.defaultPurchasePrice && (
                                    <TableRow>
                                        <TableCell>Ürün kartı (alış)</TableCell>
                                        <TableCell />
                                        <TableCell>{product.baseUnit}</TableCell>
                                        <TableCell>{formatMoney(product.defaultPurchasePrice.amount, product.defaultPurchasePrice.currency)}</TableCell>
                                        <TableCell>{product.defaultPurchasePrice.currency}</TableCell>
                                    </TableRow>
                                )}
                                {rows.map(({ list, item }) => (
                                    <TableRow key={`${list.id}|${item.variantId ?? ""}|${item.unit}`} sx={item.derived ? { color: "text.disabled" } : undefined}>
                                        <TableCell sx={{ color: "inherit" }}>
                                            {list.name} {list.kind === "purchase" && <Chip size="small" label="Alış" />} {item.derived && <Chip size="small" variant="outlined" label="Türetilmiş" />}
                                        </TableCell>
                                        <TableCell sx={{ color: "inherit" }}>{variantCode(item.variantId) ?? "—"}</TableCell>
                                        <TableCell sx={{ color: "inherit" }}>{item.unit}</TableCell>
                                        <TableCell sx={{ color: "inherit" }}>{summarizeTiers(item.tiers)}</TableCell>
                                        <TableCell sx={{ color: "inherit" }}>{list.currency}</TableCell>
                                    </TableRow>
                                ))}
                                {rows.length === 0 && !product.defaultSalePrice && (
                                    <TableRow>
                                        <TableCell colSpan={5}>
                                            <Typography color="text.secondary">Bu kalem için tanımlı fiyat yok.</Typography>
                                        </TableCell>
                                    </TableRow>
                                )}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            <QuoteTester product={product} lists={lists ?? []} />

            <DashboardCard title="Fiyat geçmişi">
                <PriceHistory productId={product.id} />
            </DashboardCard>
        </Stack>
    );
}

function QuoteTester({ product, lists }: { product: ProductDto; lists: PriceListDto[] }) {
    const units = [...new Set([product.baseUnit, product.measureUnit, ...product.units.map((u) => u.unit)].filter((u): u is string => Boolean(u)))];
    const [customerInput, setCustomerInput] = useState("");
    const customerSearch = useDebounced(customerInput);
    const [customers, setCustomers] = useState<CurrentAccountListItem[]>([]);
    const [customer, setCustomer] = useState<CurrentAccountListItem | null>(null);
    const [variantId, setVariantId] = useState("");
    const [unit, setUnit] = useState(product.soldByMeasure && product.measureUnit ? product.measureUnit : product.baseUnit);
    const [quantity, setQuantity] = useState("1");
    const [date, setDate] = useState("");
    const [quantityError, setQuantityError] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [quote, setQuote] = useState<PriceQuote | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (customerSearch.length < 1) return;
        let ignore = false;
        erp.listAccounts({ search: customerSearch, role: "customer", pageSize: 20 }).then(
            (r) => !ignore && setCustomers(r.items),
            () => !ignore && setCustomers([]),
        );
        return () => {
            ignore = true;
        };
    }, [customerSearch]);

    async function run() {
        const qty = parseAmount(quantity, { maxDecimals: 4 });
        if (qty === null || qty <= 0) {
            setQuantityError("Miktar sıfırdan büyük bir sayı olmalı.");
            return;
        }
        setQuantityError(null);
        setError(null);
        setQuote(null);
        setBusy(true);
        try {
            setQuote(
                await pricing.quote({
                    kind: "sale",
                    currentAccountId: customer?.id,
                    productId: product.id,
                    variantId: variantId || undefined,
                    unit,
                    quantity: qty,
                    date: date || undefined,
                }),
            );
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const sourceText = quote
        ? (() => {
              const label = SOURCE_LABELS[quote.source];
              const listName = quote.sourceId ? lists.find((l) => l.id === quote.sourceId)?.name : undefined;
              return listName ? `${label}: ${listName}` : label;
          })()
        : "";

    return (
        <DashboardCard title="Fiyat sorgulama" subtitle="Bir müşteri, miktar ve tarih için uygulanacak satış fiyatını ve kaynağını görün.">
            <Stack spacing={2}>
                <FormAlert error={error} />
                <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                    <Autocomplete
                        options={customers}
                        value={customer}
                        inputValue={customerInput}
                        filterOptions={(x) => x}
                        getOptionLabel={(c) => `${c.code} · ${c.name}`}
                        isOptionEqualToValue={(a, b) => a.id === b.id}
                        onInputChange={(_, text) => setCustomerInput(text)}
                        onChange={(_, c) => setCustomer(c)}
                        noOptionsText="Müşteri bulunamadı."
                        renderInput={(params) => <TextField {...params} label="Cari (müşteri)" helperText="Boşsa müşteriden bağımsız fiyat bulunur." />}
                        sx={{ minWidth: 260, flex: 1 }}
                    />
                    {product.variants.length > 0 && (
                        <TextField select label="Varyant" value={variantId} onChange={(e) => setVariantId(e.target.value)} sx={{ minWidth: 160 }}>
                            <MenuItem value="">Varyantsız</MenuItem>
                            {product.variants.map((v) => (
                                <MenuItem key={v.id} value={v.id}>
                                    {v.code}
                                </MenuItem>
                            ))}
                        </TextField>
                    )}
                    <TextField select label="Birim" value={unit} onChange={(e) => setUnit(e.target.value)} sx={{ minWidth: 110 }}>
                        {units.map((u) => (
                            <MenuItem key={u} value={u}>
                                {u}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField label="Miktar" value={quantity} onChange={(e) => setQuantity(e.target.value)} error={Boolean(quantityError)} helperText={quantityError} slotProps={{ htmlInput: { inputMode: "decimal" } }} sx={{ width: 120 }} />
                    <TextField label="Tarih" type="date" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} helperText="Boşsa bugün." />
                </Stack>
                <div>
                    <Button variant="contained" disabled={busy} onClick={() => void run()}>
                        Sorgula
                    </Button>
                </div>
                {quote && (
                    <Alert severity="success" icon={false} aria-label="Sorgu sonucu">
                        <Typography variant="h6">
                            {formatMoney(quote.unitPrice, quote.currency)} {quote.vatIncluded ? "(KDV dahil)" : "(KDV hariç)"}
                        </Typography>
                        <Typography variant="body2">Kaynak: {sourceText}</Typography>
                        {quote.tierMinQty > 1 && <Typography variant="body2">Uygulanan kademe: {quote.tierMinQty}+ {unit}</Typography>}
                    </Alert>
                )}
            </Stack>
        </DashboardCard>
    );
}
