"use client";

import { useEffect, useState } from "react";
import { catalog } from "@/lib/api/catalog";
import type { LotInput } from "@/lib/api/inventory";

export type TrackingMode = "lot" | "serial" | null;
export type Tracking = { lots: LotInput[]; serials: string[] };

/** Kalem kartlarından takip türünü (lot / seri / yok) okur; kart okunamazsa takipsiz sayılır. */
export function useTrackingModes(productIds: string[]): Record<string, TrackingMode> {
    const [modes, setModes] = useState<Record<string, TrackingMode>>({});
    const key = [...new Set(productIds)].sort().join(",");

    useEffect(() => {
        let cancelled = false;
        for (const id of key ? key.split(",") : []) {
            catalog.getProduct(id).then(
                (p) => !cancelled && setModes((cur) => ({ ...cur, [id]: p.tracksSerialLot ? (p.trackingMode ?? "lot") : null })),
                () => undefined,
            );
        }
        return () => {
            cancelled = true;
        };
    }, [key]);

    return modes;
}

/** Lot / seri bilgisini sunucu gövdesine çevirir; lot miktarları satır biriminden temel birime çarpılır. Eksikse hata iletisi döner. */
export function trackingPayload(
    label: string,
    mode: TrackingMode,
    quantity: number,
    factor: number,
    t: Tracking | undefined,
): { lots?: LotInput[]; serials?: string[] } | string {
    const near = (a: number, b: number) => Math.abs(a - b) < 1e-6;
    if (mode === "serial") {
        if (!t || t.serials.length !== quantity) return `${label}: seri numaralarını girin (${quantity} adet).`;
        return { serials: t.serials };
    }
    if (mode === "lot") {
        if (!t || t.lots.length === 0 || !near(t.lots.reduce((sum, x) => sum + x.quantity, 0), quantity)) return `${label}: lot bilgisini girin.`;
        return { lots: t.lots.map((x) => ({ ...x, quantity: Math.round(x.quantity * factor * 1e6) / 1e6 })) };
    }
    return {};
}
