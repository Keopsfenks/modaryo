"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { pos, type RegisterDto, type ReturnResult, type ShiftDto } from "@/lib/api/pos";
import { useSession } from "@/session/SessionProvider";
import { money } from "./format";
import RegisterPicker from "./RegisterPicker";
import ReturnScreen from "./ReturnScreen";
import SellScreen from "./SellScreen";
import ShiftClose from "./ShiftClose";
import ShiftOpen from "./ShiftOpen";
import Slip from "./Slip";

const REGISTER_KEY = "pos.register";

function readRegister(): string | null {
    try {
        return localStorage.getItem(REGISTER_KEY);
    } catch {
        return null;
    }
}

type Loaded = { registerId: string; register: RegisterDto | null; shift: ShiftDto | null; error: string | null };

/** Kasa akışı: kasa seçimi, vardiya açılışı, satış; iade ve vardiya kapanışı ekranları arasında geçiş. */
export default function PosApp() {
    const { hasPermission, hasModule } = useSession();
    const [registerId, setRegisterId] = useState<string | null>(readRegister);
    const [loaded, setLoaded] = useState<Loaded | null>(null);
    const [view, setView] = useState<"sell" | "return" | "close">("sell");
    const [returnResult, setReturnResult] = useState<ReturnResult | null>(null);

    const load = useCallback(async (id: string): Promise<Loaded> => {
        try {
            const [register, shift] = await Promise.all([
                pos.register(id),
                pos.currentShift(id).catch((e: unknown) => {
                    if (e instanceof ApiError && e.status === 404) return null;
                    throw e;
                }),
            ]);
            return { registerId: id, register, shift, error: null };
        } catch (e) {
            return { registerId: id, register: null, shift: null, error: errorMessage(e) };
        }
    }, []);

    useEffect(() => {
        if (!registerId) return;
        let active = true;
        void load(registerId).then((l) => active && setLoaded(l));
        return () => {
            active = false;
        };
    }, [registerId, load]);

    const choose = (id: string | null) => {
        try {
            if (id) localStorage.setItem(REGISTER_KEY, id);
            else localStorage.removeItem(REGISTER_KEY);
        } catch {
            // Seçim saklanamazsa yalnızca bu oturumda geçerlidir.
        }
        setLoaded(null);
        setView("sell");
        setRegisterId(id);
    };

    if (!hasModule("pos") || !(hasPermission("pos.sell") || hasPermission("pos.shift"))) {
        return (
            <Box sx={{ p: 4 }}>
                <Alert severity="warning">Kasa modülü kapalı ya da kasa kullanma yetkiniz yok.</Alert>
            </Box>
        );
    }

    if (!registerId) return <RegisterPicker onPick={(r) => choose(r.id)} />;

    if (!loaded || loaded.registerId !== registerId) {
        return (
            <Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center" }}>
                <CircularProgress aria-label="Yükleniyor" />
            </Box>
        );
    }

    if (loaded.error || !loaded.register) {
        return (
            <Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center", p: 3 }}>
                <Stack spacing={2} sx={{ maxWidth: 480 }}>
                    <Alert severity="error">{loaded.error ?? "Kasa bulunamadı."}</Alert>
                    <Button variant="contained" onClick={() => choose(null)}>
                        Kasa seç
                    </Button>
                </Stack>
            </Box>
        );
    }

    const { register, shift } = loaded;
    const setShift = (next: ShiftDto | null) => setLoaded({ ...loaded, shift: next });

    if (!register.isActive) {
        return (
            <Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center", p: 3 }}>
                <Stack spacing={2} sx={{ maxWidth: 480 }}>
                    <Alert severity="warning">Bu kasa noktası pasif.</Alert>
                    <Button variant="contained" onClick={() => choose(null)}>
                        Kasa seç
                    </Button>
                </Stack>
            </Box>
        );
    }

    if (!shift) {
        return <ShiftOpen register={register} onOpened={setShift} onBack={() => choose(null)} />;
    }

    return (
        <>
            {view === "sell" && <SellScreen register={register} shift={shift} onReturn={() => setView("return")} onCloseShift={() => setView("close")} onChangeRegister={() => choose(null)} />}
            {view === "return" && (
                <ReturnScreen
                    register={register}
                    onCancel={() => setView("sell")}
                    onDone={(result) => {
                        setReturnResult(result);
                        setView("sell");
                    }}
                />
            )}
            {view === "close" && <ShiftClose shift={shift} onCancel={() => setView("sell")} onClosed={() => {
                setShift(null);
                setView("sell");
            }} />}
            <Dialog open={returnResult !== null} onClose={() => setReturnResult(null)} fullWidth maxWidth="xs">
                <DialogTitle>İade tamamlandı</DialogTitle>
                <DialogContent>
                    {returnResult && (
                        <Stack spacing={1}>
                            <Typography>İade {returnResult.number}</Typography>
                            <Typography variant="h5">İade değeri: {money(returnResult.returnValue)}</Typography>
                            {returnResult.cashRefund > 0 && <Typography>Nakit iade: {money(returnResult.cashRefund)}</Typography>}
                            {returnResult.cardRefund > 0 && <Typography>Kart iadesi: {money(returnResult.cardRefund)}</Typography>}
                            {returnResult.onAccountCredit > 0 && <Typography>Müşteri borcundan düşülen: {money(returnResult.onAccountCredit)}</Typography>}
                            {returnResult.exchangeSale && <Typography>Değişim fişi: {returnResult.exchangeSale.number}</Typography>}
                            {returnResult.warnings.map((w) => (
                                <Alert key={w} severity="warning">
                                    {w}
                                </Alert>
                            ))}
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    {returnResult?.exchangeSale && <Button onClick={() => window.print()}>Fişi yazdır</Button>}
                    <Button variant="contained" onClick={() => setReturnResult(null)}>
                        Tamam
                    </Button>
                </DialogActions>
            </Dialog>
            {returnResult?.exchangeSale && <Slip slip={returnResult.exchangeSale.slip} />}
        </>
    );
}
