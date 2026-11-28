"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { errorMessage } from "@/lib/api/errorMessages";
import { pos, type RegisterDto, type ShiftDto } from "@/lib/api/pos";
import { parseNumber } from "./format";

/** Vardiya açılışı: kasadaki açılış nakdi girilir. */
export default function ShiftOpen({ register, onOpened, onBack }: { register: RegisterDto; onOpened: (shift: ShiftDto) => void; onBack: () => void }) {
    const [cash, setCash] = useState("0");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const value = parseNumber(cash);

    const open = async () => {
        if (value === null) return;
        setBusy(true);
        setError(null);
        try {
            onOpened(await pos.openShift(register.id, value));
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center", p: 3 }}>
            <Stack
                component="form"
                spacing={2}
                sx={{ width: "100%", maxWidth: 420 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    void open();
                }}
            >
                <Typography variant="h4">
                    {register.name} ({register.code})
                </Typography>
                <Typography color="text.secondary">Bu kasada açık vardiyanız yok. Açılış nakdini girerek vardiyayı başlatın.</Typography>
                {error && <Alert severity="error">{error}</Alert>}
                <TextField autoFocus label="Açılış nakdi" value={cash} onChange={(e) => setCash(e.target.value)} onFocus={(e) => e.target.select()} slotProps={{ htmlInput: { inputMode: "decimal", style: { fontSize: 24 } } }} />
                <Button type="submit" variant="contained" size="large" disabled={busy || value === null} sx={{ minHeight: 56 }}>
                    {busy ? "Açılıyor..." : "Vardiyayı aç"}
                </Button>
                <Button onClick={onBack}>Kasa değiştir</Button>
            </Stack>
        </Box>
    );
}
