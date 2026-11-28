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
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { newClientId, pos, type RegisterDto, type ReturnResult, type SaleDto, type SalePaymentInput } from "@/lib/api/pos";
import { parseAmount } from "@/lib/money";
import { lineTotal, type CartLine } from "./Cart";
import { fmtQty, money, parseNumber, round2 } from "./format";
import PaymentPanel from "./PaymentPanel";

type ReturnScreenProps = { register: RegisterDto; onDone: (result: ReturnResult) => void; onCancel: () => void };

/**
 * İade / değişim (F9): fiş numarası (ya da okutulan fiş barkodu) ile satış yüklenir; iade miktarı satılan - daha önce iade edilenle sınırlıdır.
 * Değişimde yeni kalemler eklenir; iade değeri önce değişime mahsup edilir, fark ya müşteriden alınır ya nakit/karta iade edilir.
 * Tutarlar tahmindir; sunucu kesin değeri hesaplar (uyuşmazlıkta sunucunun tutarı gösterilir).
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
    const [info, setInfo] = useState<string | null>(null);
    const [serverRefund, setServerRefund] = useState<number | null>(null);
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
            setServerRefund(null);
        } catch (e) {
            setSale(null);
            setError(errorMessage(e));
        }
    };

    const picked = (sale?.lines ?? []).map((l) => {
        const left = round2(l.quantity - l.returnedQuantity);
        const q = parseNumber(qtys[l.id] ?? "") ?? 0;
        const valid = q > 0 && q <= left;
        const value = valid ? (q === l.quantity ? l.total : round2((l.total * q) / l.quantity)) : 0;
        return { line: l, left, q, valid, over: q > left, value };
    });
    const chosen = picked.filter((p) => p.valid);
    const anyOver = picked.some((p) => p.over);
    const returnValue = round2(chosen.reduce((s, p) => s + p.value, 0));
    const onAccount = sale?.payments.filter((p) => p.method === "onAccount").reduce((s, p) => s + p.amount, 0) ?? 0;
    const share = sale && onAccount > 0 && sale.total > 0 ? Math.min(returnValue, round2((returnValue * onAccount) / sale.total)) : 0;
    const cashable = round2(returnValue - share);
    const exchangeTotal = round2(exchange.reduce((s, l) => s + lineTotal(l), 0));
    const credit = Math.min(cashable, exchangeTotal);
    const payDue = round2(exchangeTotal - credit);
    const refundDue = serverRefund ?? round2(cashable - credit);

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
        if (!sale || chosen.length === 0) return;
        const body = {
            saleNumber: sale.number,
            registerId: register.id,
            lines: chosen.map((p) => ({ saleLineId: p.line.id, quantity: p.q })),
            refund: payDue <= 0 && refundDue > 0 ? [{ method, amount: refundDue }] : undefined,
            exchangeLines:
                exchange.length > 0
                    ? exchange.map((l) => ({
                          productId: l.info.item.productId,
                          variantId: l.info.item.variantId,
                          unit: l.info.unit,
                          quantity: l.quantity,
                          unitPrice: l.info.unitPrice > 0 ? l.info.unitPrice : undefined,
                          fixedTotal: l.info.fixedTotal ?? undefined,
                      }))
                    : undefined,
            exchangePayments: payDue > 0 ? payments : undefined,
            reason: reason.trim() === "" ? null : reason.trim(),
        };
        const signature = JSON.stringify(body);
        if (attempt.current?.signature !== signature) attempt.current = { id: newClientId(), signature };
        setBusy(true);
        setError(null);
        setInfo(null);
        try {
            const result = await pos.createReturn({ clientReturnId: attempt.current.id, ...body });
            attempt.current = null;
            onDone(result);
        } catch (e) {
            if (e instanceof ApiError && e.code === "refund_mismatch") {
                // Sunucu kesin iade tutarını bildirir ("... toplamı 25,00 olmalıdır."); tutar güncellenir, kasiyer yeniden onaylar.
                const found = /(\d{1,3}(?:\.\d{3})*,\d{2})/.exec(e.message);
                const exact = found ? parseAmount(found[1]) : null;
                if (exact !== null) {
                    setServerRefund(exact);
                    setInfo(`İade tutarı sunucuya göre ${money(exact)} olarak güncellendi; tekrar onaylayın.`);
                    return;
                }
            }
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
                    {info && <Alert severity="info">{info}</Alert>}
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
                                    {money(returnValue)}
                                </Typography>
                            </Stack>
                            {share > 0 && <Typography color="text.secondary">Bunun {money(share)} tutarı veresiye olduğundan müşteri borcundan düşülür.</Typography>}
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
                                            <Typography>{money(lineTotal(l))}</Typography>
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
                                    <PaymentPanel total={payDue} customer={null} allowOnAccount={false} busy={busy} offline={chosen.length === 0 || anyOver} submitLabel="İadeyi tamamla" onSubmit={(p) => void submit(p)} />
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
                                        <Button variant="contained" size="large" disabled={busy || chosen.length === 0 || anyOver} onClick={() => void submit()} sx={{ minHeight: 56 }}>
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
