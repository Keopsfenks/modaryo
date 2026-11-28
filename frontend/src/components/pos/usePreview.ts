import { useEffect, useRef, useState } from "react";
import { useDebounced } from "@/components/pricing/useDebounced";
import { errorMessage } from "@/lib/api/errorMessages";

export type PreviewState<T> = {
    /** Güncel istek içeriğine ait sunucu sonucu; içerik değiştiyse (bayat) null. */
    data: T | null;
    /** Bekleme (debounce) ya da istek sürüyor: ekrandaki tutar henüz güncel değil. */
    pending: boolean;
    error: string | null;
};

/**
 * Sunucu önizlemesi: `key` (isteğin JSON'u, boşsa istek yok) değişince gecikmeyle (debounce) `fetcher` çağrılır.
 * Sonuç yalnızca kendi anahtarı güncelken geçerlidir; eski yanıt yeni sepete uygulanmaz ve ödeme bu sürede kapalı kalır.
 */
export function usePreview<T>(key: string, fetcher: () => Promise<T>, delay = 300): PreviewState<T> {
    const settled = useDebounced(key, delay);
    const latest = useRef(fetcher);
    useEffect(() => {
        latest.current = fetcher;
    });
    const [result, setResult] = useState<{ key: string; data: T | null; error: string | null } | null>(null);

    useEffect(() => {
        if (settled === "") return;
        let cancelled = false;
        latest.current().then(
            (data) => !cancelled && setResult({ key: settled, data, error: null }),
            (e) => !cancelled && setResult({ key: settled, data: null, error: errorMessage(e) }),
        );
        return () => {
            cancelled = true;
        };
    }, [settled]);

    if (key === "") return { data: null, pending: false, error: null };
    const fresh = result?.key === key;
    return { data: fresh ? result.data : null, pending: !fresh, error: fresh ? result.error : null };
}
