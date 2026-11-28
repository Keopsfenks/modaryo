"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { errorMessage } from "@/lib/api/errorMessages";
import { pos, type RegisterRow } from "@/lib/api/pos";

/** Kasa noktası seçimi: yalnızca aktif kasalar listelenir (sunucu oturumun şubelerine göre süzer). */
export default function RegisterPicker({ onPick }: { onPick: (register: RegisterRow) => void }) {
    const [rows, setRows] = useState<RegisterRow[] | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let active = true;
        pos.registers().then(
            (r) => active && setRows(r.filter((x) => x.isActive)),
            (e) => active && setError(errorMessage(e)),
        );
        return () => {
            active = false;
        };
    }, []);

    return (
        <Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center", p: 3 }}>
            <Stack spacing={2} sx={{ width: "100%", maxWidth: 520 }}>
                <Typography variant="h4">Kasa seçin</Typography>
                {error && <Alert severity="error">{error}</Alert>}
                {!rows && !error && <CircularProgress aria-label="Yükleniyor" />}
                {rows?.length === 0 && <Alert severity="info">Aktif kasa noktası yok. Ayarlar &gt; Kasa (POS) bölümünden ekleyin.</Alert>}
                {rows?.map((r) => (
                    <Button key={r.id} variant="outlined" size="large" onClick={() => onPick(r)} sx={{ minHeight: 64, justifyContent: "flex-start", fontSize: 20 }}>
                        {r.name} ({r.code})
                    </Button>
                ))}
            </Stack>
        </Box>
    );
}
