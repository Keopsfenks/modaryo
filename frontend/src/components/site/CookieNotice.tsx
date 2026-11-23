"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Typography from "@mui/material/Typography";

const KEY = "modaryo_cerez_bilgi";

function wasDismissed(): boolean {
    try {
        return localStorage.getItem(KEY) === "1";
    } catch {
        return false;
    }
}

/** Yalnızca bilgilendirme: analiz çerezi yok, yalnızca zorunlu çerezler kullanılıyor. */
export default function CookieNotice() {
    const [visible, setVisible] = useState(false);

    useEffect(() => {
        // Depo yalnızca istemcide okunur; sunucu çıktısıyla uyumsuzluk olmaz.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setVisible(!wasDismissed());
    }, []);

    if (!visible) {
        return null;
    }

    const dismiss = () => {
        setVisible(false);
        try {
            localStorage.setItem(KEY, "1");
        } catch {
            // Depo kapalıysa bildirim yalnızca bu sayfa görüntülemesi için gizlenir.
        }
    };

    return (
        <Paper
            role="region"
            aria-label="Çerez bilgilendirmesi"
            elevation={6}
            sx={{ position: "fixed", left: 16, right: 16, bottom: 16, zIndex: 1500, maxWidth: 560, p: 2, display: "flex", gap: 2, alignItems: "center", flexWrap: "wrap" }}
        >
            <Typography sx={{ flex: "1 1 280px", fontSize: "0.9375rem" }}>
                Bu site yalnızca zorunlu çerez kullanır; analiz veya reklam çerezi yoktur.{" "}
                <Link href="/cerez-politikasi" style={{ textDecoration: "underline" }}>Çerez politikası</Link>
            </Typography>
            <Button variant="contained" onClick={dismiss}>Anladım</Button>
        </Paper>
    );
}
