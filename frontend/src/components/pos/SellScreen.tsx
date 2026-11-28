"use client";

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { IconArrowBackUp, IconCash, IconLock, IconSearch, IconUser } from "@tabler/icons-react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import List from "@mui/material/List";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemText from "@mui/material/ListItemText";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useDebounced } from "@/components/pricing/useDebounced";
import type { SellableItem } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { newClientId, pos, type PosLookup, type QuickButtonDto, type RegisterDto, type SaleInput, type SaleResult, type ShiftDto } from "@/lib/api/pos";
import { useSession } from "@/session/SessionProvider";
import Cart, { lineTotal, type CartLine } from "./Cart";
import CustomerPicker, { type PosCustomer } from "./CustomerPicker";
import { money, round2 } from "./format";
import PaymentPanel, { CASH_FIELD_ID } from "./PaymentPanel";
import QuickButtons from "./QuickButtons";
import { CashMoveDialog } from "./ShiftClose";
import Slip from "./Slip";

const AUTO_PRINT_KEY = "pos.autoPrint";

function subscribeOnline(callback: () => void) {
    window.addEventListener("online", callback);
    window.addEventListener("offline", callback);
    return () => {
        window.removeEventListener("online", callback);
        window.removeEventListener("offline", callback);
    };
}
const useOnline = () =>
    useSyncExternalStore(
        subscribeOnline,
        () => navigator.onLine,
        () => true,
    );

function readAutoPrint(): boolean {
    try {
        return localStorage.getItem(AUTO_PRINT_KEY) !== "0";
    } catch {
        return true;
    }
}

function SearchDialog({ open, onClose, onPick }: { open: boolean; onClose: () => void; onPick: (item: SellableItem) => void }) {
    const [text, setText] = useState("");
    const search = useDebounced(text);
    const [rows, setRows] = useState<SellableItem[]>([]);
    useEffect(() => {
        if (!open || search.length < 1) return;
        let cancelled = false;
        pos.search(search).then(
            (r) => !cancelled && setRows(r),
            () => !cancelled && setRows([]),
        );
        return () => {
            cancelled = true;
        };
    }, [open, search]);
    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Ürün ara</DialogTitle>
            <DialogContent>
                <TextField autoFocus fullWidth margin="dense" label="Kod, ad ya da barkod" value={text} onChange={(e) => setText(e.target.value)} />
                <List>
                    {(search.length < 1 ? [] : rows).map((i) => (
                        <ListItemButton key={`${i.productId}-${i.variantId}`} onClick={() => onPick(i)} sx={{ minHeight: 48 }}>
                            <ListItemText primary={i.name} secondary={i.code} />
                        </ListItemButton>
                    ))}
                </List>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Kapat</Button>
            </DialogActions>
        </Dialog>
    );
}

type SellScreenProps = {
    register: RegisterDto;
    shift: ShiftDto;
    onReturn: () => void;
    onCloseShift: () => void;
    onChangeRegister: () => void;
};

/** Kasiyer satış ekranı: sol sepet, sağ hızlı tuşlar ve ödeme; barkod alanı her zaman odakta, F2 ara, F4 müşteri, F8 ödeme, F9 iade, Esc iptal. */
export default function SellScreen({ register, shift, onReturn, onCloseShift, onChangeRegister }: SellScreenProps) {
    const { session, hasPermission } = useSession();
    const online = useOnline();
    const [lines, setLines] = useState<CartLine[]>([]);
    const [customer, setCustomer] = useState<PosCustomer | null>(null);
    const [scan, setScan] = useState("");
    const [message, setMessage] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [done, setDone] = useState<SaleResult | null>(null);
    const [searchOpen, setSearchOpen] = useState(false);
    const [pickerOpen, setPickerOpen] = useState(false);
    const [clearOpen, setClearOpen] = useState(false);
    const [cashMoveOpen, setCashMoveOpen] = useState(false);
    const [panelKey, setPanelKey] = useState(0);
    const [autoPrint, setAutoPrint] = useState(readAutoPrint);
    // Aynı satış denemesinin kimliği: içerik değişmedikçe yeniden gönderimde korunur (sunucuda tekrar yazılmaz).
    const attempt = useRef<{ id: string; signature: string } | null>(null);
    const scanRef = useRef<HTMLInputElement>(null);

    const anyDialog = searchOpen || pickerOpen || clearOpen || cashMoveOpen || done !== null;
    const total = round2(lines.reduce((s, l) => s + lineTotal(l), 0));
    const canSell = hasPermission("pos.sell");

    const addInfo = useCallback((info: PosLookup, quantity?: number) => {
        const qty = quantity ?? info.quantity;
        setLines((cur) => {
            const mergeable = info.fixedTotal === null && !info.item.soldByMeasure;
            const idx = mergeable
                ? cur.findIndex((l) => l.info.fixedTotal === null && l.info.item.productId === info.item.productId && l.info.item.variantId === info.item.variantId && l.info.unit === info.unit)
                : -1;
            if (idx >= 0) return cur.map((l, i) => (i === idx ? { ...l, quantity: Math.round((l.quantity + qty) * 1000) / 1000 } : l));
            return [...cur, { key: newClientId(), info, quantity: qty, baseQuantity: info.quantity, discountPercent: 0 }];
        });
    }, []);

    const lookupAndAdd = useCallback(
        async (code: string, quantity?: number) => {
            const text = code.trim();
            if (!text) return;
            setMessage(null);
            try {
                addInfo(await pos.lookup(text, register.id), quantity);
            } catch (e) {
                setMessage(errorMessage(e));
            }
        },
        [addInfo, register.id],
    );

    const submit = async (payments: SaleInput["payments"]) => {
        const body = {
            registerId: register.id,
            customerId: customer?.id ?? null,
            lines: lines.map((l) => ({
                productId: l.info.item.productId,
                variantId: l.info.item.variantId,
                unit: l.info.unit,
                quantity: l.quantity,
                unitPrice: l.info.unitPrice > 0 ? l.info.unitPrice : undefined,
                manualDiscountPercent: l.discountPercent > 0 ? l.discountPercent : undefined,
                fixedTotal: l.info.fixedTotal ?? undefined,
            })),
            payments,
        };
        const signature = JSON.stringify(body);
        if (attempt.current?.signature !== signature) attempt.current = { id: newClientId(), signature };
        setBusy(true);
        setMessage(null);
        try {
            const result = await pos.createSale({ clientSaleId: attempt.current.id, ...body });
            attempt.current = null;
            setLines([]);
            setCustomer(null);
            setPanelKey((k) => k + 1);
            setDone(result);
        } catch (e) {
            setMessage(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    const startNew = () => {
        setDone(null);
        setMessage(null);
    };

    const cancelSale = async () => {
        if (!done) return;
        try {
            await pos.cancelSale(done.id, "Kasada iptal edildi");
            startNew();
        } catch (e) {
            setMessage(errorMessage(e));
        }
    };

    // Fiş, satış sonrası otomatik yazdırılır (ayardan kapatılabilir).
    useEffect(() => {
        if (!done || !autoPrint) return;
        const handle = setTimeout(() => window.print(), 150);
        return () => clearTimeout(handle);
    }, [done, autoPrint]);

    // Barkod alanı her zaman odakta: klavye kamalı okuyucu yazıp Enter'a basar.
    useEffect(() => {
        if (!anyDialog) scanRef.current?.focus();
    }, [anyDialog, lines.length]);

    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (anyDialog) return;
            if (e.key === "F2") {
                e.preventDefault();
                setSearchOpen(true);
            } else if (e.key === "F4") {
                e.preventDefault();
                setPickerOpen(true);
            } else if (e.key === "F8") {
                e.preventDefault();
                document.getElementById(CASH_FIELD_ID)?.focus();
            } else if (e.key === "F9") {
                e.preventDefault();
                onReturn();
            } else if (e.key === "Escape") {
                if (scan) setScan("");
                else if (lines.length > 0) setClearOpen(true);
            }
        };
        window.addEventListener("keydown", onKey);
        return () => window.removeEventListener("keydown", onKey);
    }, [anyDialog, scan, lines.length, onReturn]);

    const pickButton = (b: QuickButtonDto) => void lookupAndAdd(b.code ?? "", b.presetQty ?? undefined);

    return (
        <Box
            sx={{ height: "100vh", display: "flex", flexDirection: "column", bgcolor: "background.default" }}
            onClick={(e) => {
                const tag = (e.target as HTMLElement).closest("input,textarea,button,select,a,[role=button],[role=tab]");
                if (!tag && !anyDialog) scanRef.current?.focus();
            }}
        >
            <Paper square sx={{ px: 2, py: 1, display: "flex", alignItems: "center", gap: 2, flexWrap: "wrap" }}>
                <Typography variant="h6">
                    {register.name} ({register.code})
                </Typography>
                <Typography variant="body2" color="text.secondary">
                    Kasiyer: {session.user.fullName} · Vardiya: {new Date(shift.openedAt).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}
                </Typography>
                <Chip icon={<IconUser size={16} />} label={customer ? customer.name : "Perakende Müşteri"} onClick={() => setPickerOpen(true)} color={customer ? "primary" : "default"} sx={{ height: 40, fontSize: 15 }} />
                <Box sx={{ flexGrow: 1 }} />
                <FormControlLabel
                    control={
                        <Switch
                            checked={autoPrint}
                            onChange={(e) => {
                                setAutoPrint(e.target.checked);
                                try {
                                    localStorage.setItem(AUTO_PRINT_KEY, e.target.checked ? "1" : "0");
                                } catch {
                                    // Tercih saklanamazsa yalnızca bu oturumda geçerlidir.
                                }
                            }}
                        />
                    }
                    label="Fişi otomatik yazdır"
                />
                <Button startIcon={<IconSearch size={18} />} onClick={() => setSearchOpen(true)} sx={{ minHeight: 48 }}>
                    Ara (F2)
                </Button>
                {hasPermission("pos.return") && (
                    <Button startIcon={<IconArrowBackUp size={18} />} onClick={onReturn} sx={{ minHeight: 48 }}>
                        İade (F9)
                    </Button>
                )}
                {hasPermission("pos.cashmove") && (
                    <Button startIcon={<IconCash size={18} />} onClick={() => setCashMoveOpen(true)} sx={{ minHeight: 48 }}>
                        Kasa giriş/çıkış
                    </Button>
                )}
                <Button startIcon={<IconLock size={18} />} color="warning" onClick={onCloseShift} sx={{ minHeight: 48 }}>
                    Vardiyayı kapat
                </Button>
                <Button onClick={onChangeRegister} sx={{ minHeight: 48 }}>
                    Kasa değiştir
                </Button>
            </Paper>
            {!online && (
                <Alert severity="error" sx={{ borderRadius: 0 }}>
                    Bağlantı yok. Çevrimdışı satış desteklenmez; bağlantı gelene kadar satış yapılamaz.
                </Alert>
            )}
            {message && (
                <Alert severity="error" onClose={() => setMessage(null)} sx={{ borderRadius: 0 }}>
                    {message}
                </Alert>
            )}
            <Box sx={{ flexGrow: 1, minHeight: 0, display: "grid", gridTemplateColumns: { xs: "1fr", md: "minmax(0, 3fr) minmax(360px, 2fr)" }, gap: 2, p: 2 }}>
                <Paper sx={{ p: 2, display: "flex", flexDirection: "column", gap: 2, minHeight: 0 }}>
                    <TextField
                        inputRef={scanRef}
                        value={scan}
                        onChange={(e) => setScan(e.target.value)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter") {
                                e.preventDefault();
                                const code = scan;
                                setScan("");
                                void lookupAndAdd(code);
                            }
                        }}
                        placeholder="Barkod okutun ya da kod yazıp Enter'a basın"
                        disabled={!canSell}
                        slotProps={{ htmlInput: { "aria-label": "Barkod", autoComplete: "off", style: { fontSize: 22 } } }}
                        fullWidth
                    />
                    <Cart
                        lines={lines}
                        maxDiscount={session.maxDiscountPercent}
                        onQuantity={(key, quantity) => setLines((cur) => cur.map((l) => (l.key === key ? { ...l, quantity } : l)))}
                        onDiscount={(key, discountPercent) => setLines((cur) => cur.map((l) => (l.key === key ? { ...l, discountPercent } : l)))}
                        onRemove={(key) => setLines((cur) => cur.filter((l) => l.key !== key))}
                    />
                    <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "baseline" }}>
                        <Typography variant="h5">Toplam</Typography>
                        <Typography variant="h3" aria-label="Sepet toplamı">
                            {money(total)}
                        </Typography>
                    </Stack>
                </Paper>
                <Stack spacing={2} sx={{ minHeight: 0, overflowY: "auto" }}>
                    <Paper sx={{ p: 2 }}>
                        <QuickButtons pages={register.pages} onPick={pickButton} disabled={!online} />
                    </Paper>
                    <Paper sx={{ p: 2 }}>
                        <PaymentPanel key={panelKey} total={total} customer={customer} busy={busy} offline={!online || !canSell} onSubmit={(p) => void submit(p)} />
                    </Paper>
                </Stack>
            </Box>
            <SearchDialog
                open={searchOpen}
                onClose={() => setSearchOpen(false)}
                onPick={(i) => {
                    setSearchOpen(false);
                    void lookupAndAdd(i.code);
                }}
            />
            <CustomerPicker
                open={pickerOpen}
                onClose={() => setPickerOpen(false)}
                onPick={(c) => {
                    setCustomer(c);
                    setPickerOpen(false);
                }}
            />
            <Dialog open={clearOpen} onClose={() => setClearOpen(false)}>
                <DialogTitle>Satış iptal edilsin mi?</DialogTitle>
                <DialogContent>Sepetteki tüm kalemler silinecek.</DialogContent>
                <DialogActions>
                    <Button onClick={() => setClearOpen(false)}>Vazgeç</Button>
                    <Button
                        color="error"
                        variant="contained"
                        onClick={() => {
                            setLines([]);
                            setClearOpen(false);
                        }}
                    >
                        Sepeti boşalt
                    </Button>
                </DialogActions>
            </Dialog>
            {cashMoveOpen && <CashMoveDialog shift={shift} onClose={() => setCashMoveOpen(false)} />}
            <Dialog open={done !== null} onClose={startNew} fullWidth maxWidth="xs">
                <DialogTitle>Satış tamamlandı</DialogTitle>
                <DialogContent>
                    {done && (
                        <Stack spacing={1} sx={{ textAlign: "center" }}>
                            <Typography color="text.secondary">Fiş {done.number}</Typography>
                            <Typography variant="h4">{money(done.total)}</Typography>
                            {done.change > 0 && (
                                <>
                                    <Typography color="text.secondary">Para üstü</Typography>
                                    <Typography sx={{ fontSize: 56, fontWeight: 700 }} color="success.main">
                                        {money(done.change)}
                                    </Typography>
                                </>
                            )}
                            {done.invoiceNumber && <Typography>e-Arşiv fatura: {done.invoiceNumber}</Typography>}
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions sx={{ justifyContent: "space-between" }}>
                    <Box>
                        <Button onClick={() => window.print()}>Fişi yazdır</Button>
                        {hasPermission("pos.return") && (
                            <Button color="error" onClick={() => void cancelSale()}>
                                Satışı iptal et
                            </Button>
                        )}
                    </Box>
                    <Button variant="contained" autoFocus onClick={startNew} sx={{ minHeight: 48 }}>
                        Yeni satış
                    </Button>
                </DialogActions>
            </Dialog>
            {done && <Slip slip={done.slip} />}
        </Box>
    );
}
