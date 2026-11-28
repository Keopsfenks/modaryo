"use client";

import { useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import Paper from "@mui/material/Paper";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import { errorMessage } from "@/lib/api/errorMessages";
import { newClientId, pos, type RegisterDto, type ReturnResult, type SaleDto, type SalePaymentInput } from "@/lib/api/pos";
import { lineEstimate, type CartLine } from "./Cart";
import { fmtQty, money, parseNumber } from "./format";
import PaymentPanel from "./PaymentPanel";
import { usePreview } from "./usePreview";

type ReturnScreenProps = { register: RegisterDto; onDone: (result: ReturnResult) => void; onCancel: () => void };

/**
 * İade / değişim (F9): fiş numarası (ya da okutulan fiş barkodu) ile satış yüklenir; iade miktarı satılan - daha önce iade edilenle sınırlıdır.
 * Değişimde yeni kalemler eklenir; iade değeri önce değişime mahsup edilir, fark ya müşteriden alınır ya nakit/karta iade edilir.
 * İade değeri, mahsup, tahsil ve iade tutarları sunucu önizlemesinden gelir (/returns/preview); istemci tutar hesaplamaz.
 */
export default function ReturnScreen({ register, onDone, onCancel }: ReturnScreenProps) {
    const [receipt, setReceipt] = useState("");
    const [sale, setSale] = useState<SaleDto | null>(null);
    const [qtys, setQtys] = useState<Record<string, string>>({});
    const [exchange, setExchange] = useState<CartLine[]>([]);
    const [exchangeScan, setExchangeScan] = useState("");
    const [method, setMethod] = useState<"cash" | "card">("cash");
    const [reason, setReason] = useState("");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    // Aynı iade denemesinin kimliği: yeniden gönderimde korunur.
    const attempt = useRef<{ id: string; signature: string } | null>(null);

    const load = async () => {
        const number = receipt.trim();
        if (!number) return;
        setError(null);
        try {
            const s = await pos.saleByNumber(number);
            if (s.status === "cancelled") {
                setSale(null);
                setError("Bu satış iptal edilmiş.");
                return;
            }
            setSale(s);
            setQtys({});
        } catch (e) {
            setSale(null);
            setError(errorMessage(e));
        }
    };

    const picked = (sale?.lines ?? []).map((l) => {
        // Ağırlıklı kalemlerde kalan miktar 3 ondalıktır (2'ye yuvarlamak "Tümü"yü bozar).
        const left = Math.round((l.quantity - l.returnedQuantity) * 1e6) / 1e6;
        const q = parseNumber(qtys[l.id] ?? "") ?? 0;
        return { line: l, left, q, valid: q > 0 && q <= left, over: q > left };
    });
    const chosen = picked.filter((p) => p.valid);
    const anyOver = picked.some((p) => p.over);

    const exchangeLines = exchange.map((l) => ({
        productId: l.info.item.productId,
        variantId: l.info.item.variantId,
        unit: l.info.unit,
        quantity: l.quantity,
        fixedTotal: l.info.fixedTotal ?? undefined,
    }));
    const returnLines = chosen.map((p) => ({ saleLineId: p.line.id, quantity: p.q }));
    const previewBody = sale ? { saleNumber: sale.number, registerId: register.id, lines: returnLines, exchangeLines: exchangeLines.length > 0 ? exchangeLines : undefined } : null;
    const preview = usePreview(previewBody && chosen.length > 0 && !anyOver ? JSON.stringify(previewBody) : "", () => pos.previewReturn(previewBody!));
    const quote = preview.data;
    const waiting = chosen.length > 0 && !anyOver && quote === null;
    const returnValue = quote?.returnValue ?? 0;
    const share = quote?.onAccountShare ?? 0;
    const payDue = quote?.payDue ?? 0;
    const refundDue = quote?.refundDue ?? 0;

    const addExchange = async () => {
        const code = exchangeScan.trim();
        if (!code) return;
        setExchangeScan("");
        setError(null);
        try {
            const i = await pos.lookup(code, register.id);
            setExchange((cur) => [...cur, { key: newClientId(), info: i, quantity: i.quantity, baseQuantity: i.quantity, discountPercent: 0 }]);
        } catch (e) {
            setError(errorMessage(e));
        }
    };

    const submit = async (payments?: SalePaymentInput[]) => {
        if (!sale || chosen.length === 0 || !quote) return;
        const body = {
            saleNumber: sale.number,
            registerId: register.id,
            lines: returnLines,
            refund: payDue <= 0 && refundDue > 0 ? [{ method, amount: refundDue }] : undefined,
            exchangeLines: exchangeLines.length > 0 ? exchangeLines : undefined,
            exchangePayments: payDue > 0 ? payments : undefined,
            reason: reason.trim() === "" ? null : reason.trim(),
        };
        const signature = JSON.stringify(body);
        if (attempt.current?.signature !== signature) attempt.current = { id: newClientId(), signature };
        setBusy(true);
        setError(null);
        try {
            const result = await pos.createReturn({ clientReturnId: attempt.current.id, ...body });
            attempt.current = null;
            onDone(result);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Box sx={{ maxWidth: 960, mx: "auto", p: 3 }}>
            <Paper sx={{ p: 3 }}>
                <Stack spacing={2}>
                    <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center" }}>
                        <Typography variant="h5">İade / değişim</Typography>
                        <Button onClick={onCancel}>Satışa dön</Button>
                    </Stack>
                    {error && <Alert severity="error">{error}</Alert>}
                    {preview.error && <Alert severity="error">{preview.error}</Alert>}
                    <TextField
                        autoFocus
                        label="Fiş numarası"
                        placeholder="Fiş barkodunu okutun ya da numarayı yazın"
                        value={receipt}
                        onChange={(e) => setReceipt(e.target.value)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter") {
                                e.preventDefault();
                                void load();
                            }
                        }}
                        slotProps={{ htmlInput: { style: { fontSize: 20 } } }}
                    />
                    {sale && (
                        <>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Satılan</TableCell>
                                        <TableCell align="right">İade edilen</TableCell>
                                        <TableCell align="right">Tutar</TableCell>
                                        <TableCell>İade miktarı</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {picked.map(({ line, left, over }) => (
                                        <TableRow key={line.id}>
                                            <TableCell>{line.description}</TableCell>
                                            <TableCell align="right">
                                                {fmtQty(line.quantity)} {line.unit}
                                            </TableCell>
                                            <TableCell align="right">{fmtQty(line.returnedQuantity)}</TableCell>
                                            <TableCell align="right">{money(line.total)}</TableCell>
                                            <TableCell>
                                                <Stack direction="row" spacing={1}>
                                                    <TextField
                                                        size="small"
                                                        value={qtys[line.id] ?? ""}
                                                        error={over}
                                                        helperText={over ? `En çok ${fmtQty(left)}` : undefined}
                                                        onChange={(e) => setQtys({ ...qtys, [line.id]: e.target.value })}
                                                        disabled={left <= 0}
                                                        slotProps={{ htmlInput: { "aria-label": `İade miktarı ${line.description}`, inputMode: "decimal" } }}
                                                        sx={{ width: 110 }}
                                                    />
                                                    <Button size="small" disabled={left <= 0} onClick={() => setQtys({ ...qtys, [line.id]: fmtQty(left) })}>
                                                        Tümü
                                                    </Button>
                                                </Stack>
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                            <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "baseline" }}>
                                <Typography variant="h6">İade değeri</Typography>
                                <Typography variant="h4" aria-label="İade değeri">
                                    {waiting ? "..." : money(returnValue)}
                                </Typography>
                            </Stack>
                            {share > 0 &&  <Typography color="text.secondary">Bunun {money(share)} tutarı veresiye olduğundan müşteri borcundan düşülür.</Typography>}
                            <Stack spacing={1}>
                                <Typography variant="subtitle1">Değişim ürünleri (isteğe bağlı)</Typography>
                                <TextField
                                    size="small"
                                    label="Değişim ürünü barkodu"
                                    value={exchangeScan}
                                    onChange={(e) => setExchangeScan(e.target.value)}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter") {
                                            e.preventDefault();
                                            void addExchange();
                                        }
                                    }}
                                />
                                {exchange.map((l) => (
                                    <Stack key={l.key} direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
                                        <Typography>
                                            {l.info.item.name} × {fmtQty(l.quantity)}
                                        </Typography>
                                        <Stack direction="row" sx={{ alignItems: "center" }} spacing={1}>
                                            <Typography color={quote?.exchange?.lines[exchange.indexOf(l)] ? "text.primary" : "text.disabled"}>{money(quote?.exchange?.lines[exchange.indexOf(l)]?.total ?? lineEstimate(l))}</Typography>
                                            <IconButton aria-label={`Değişim satırını sil ${l.info.item.name}`} onClick={() => setExchange((cur) => cur.filter((x) => x.key !== l.key))}>
                                                <IconTrash size={18} />
                                            </IconButton>
                                        </Stack>
                                    </Stack>
                                ))}
                            </Stack>
                            <TextField size="small" label="İade nedeni" value={reason} onChange={(e) => setReason(e.target.value)} />
                            {payDue > 0 ? (
                                <Paper variant="outlined" sx={{ p: 2 }}>
                                    <Typography sx={{ mb: 1 }}>Değişim farkı müşteriden alınır.</Typography>
                                    <PaymentPanel total={payDue} customer={null} allowOnAccount={false} busy={busy} offline={chosen.length === 0 || anyOver} pending={waiting} submitLabel="İadeyi tamamla" onSubmit={(p) => void submit(p)} />
                                </Paper>
                            ) : (
                                <Stack spacing={1}>
                                    {refundDue > 0 && (
                                        <>
                                            <Typography>
                                                Müşteriye iade edilecek: <strong>{money(refundDue)}</strong>
                                            </Typography>
                                            <RadioGroup row aria-label="İade yöntemi" value={method} onChange={(e) => setMethod(e.target.value as "cash" | "card")}>
                                                <FormControlLabel value="cash" control={<Radio />} label="Nakit" />
                                                <FormControlLabel value="card" control={<Radio />} label="Kart" />
                                            </RadioGroup>
                                        </>
                                    )}
                                    <div>
                                        <Button variant="contained" size="large" disabled={busy || chosen.length === 0 || anyOver || waiting} onClick={() => void submit()} sx={{ minHeight: 56 }}>
                                            {busy ? "İşleniyor..." : "İadeyi tamamla"}
                                        </Button>
                                    </div>
                                </Stack>
                            )}
                        </>
                    )}
                </Stack>
            </Paper>
        </Box>
    );
}
