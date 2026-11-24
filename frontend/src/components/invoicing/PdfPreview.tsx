"use client";

import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import CircularProgress from "@mui/material/CircularProgress";
import Alert from "@mui/material/Alert";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";

type PdfPreviewProps = {
    /** PDF baytlarını getirir (yanıt "attachment" başlığı taşıdığından blob olarak okunup gömülür). */
    load: () => Promise<Blob>;
    /** Değişince PDF yeniden yüklenir. */
    refreshKey?: string | number;
    title: string;
    height?: number | string;
};

/** Gömülü PDF önizlemesi. */
export default function PdfPreview({ load, refreshKey, title, height = 640 }: PdfPreviewProps) {
    const [state, setState] = useState<{ key: string | number | undefined; url: string | null; error: string | null } | null>(null);

    useEffect(() => {
        let cancelled = false;
        let created: string | null = null;
        load().then(
            (blob) => {
                if (cancelled) return;
                const pdf = new Blob([blob], { type: "application/pdf" });
                created = typeof URL.createObjectURL === "function" ? URL.createObjectURL(pdf) : null;
                setState({ key: refreshKey, url: created, error: null });
            },
            (e: unknown) => {
                if (cancelled) return;
                setState({ key: refreshKey, url: null, error: e instanceof ApiError ? errorMessage(e) : "PDF yüklenemedi." });
            },
        );
        return () => {
            cancelled = true;
            if (created && typeof URL.revokeObjectURL === "function") URL.revokeObjectURL(created);
        };
        // load her render'da yeni olabilir; yenileme refreshKey ile istenir.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [refreshKey]);

    const loading = state === null || state.key !== refreshKey;
    const url = loading ? null : state.url;
    const error = loading ? null : state.error;

    return (
        <Box sx={{ position: "relative", height, border: 1, borderColor: "divider", borderRadius: 1, overflow: "hidden" }}>
            {loading && (
                <Box sx={{ position: "absolute", inset: 0, display: "grid", placeItems: "center" }}>
                    <CircularProgress aria-label="PDF yükleniyor" />
                </Box>
            )}
            {error && <Alert severity="error">{error}</Alert>}
            {url && <iframe title={title} src={url} style={{ width: "100%", height: "100%", border: 0 }} />}
        </Box>
    );
}
