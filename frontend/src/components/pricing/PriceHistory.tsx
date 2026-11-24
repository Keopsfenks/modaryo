"use client";

import { useEffect, useState } from "react";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import type { Money4 } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type PriceHistoryDto, type PriceHistoryScope, type PriceTier } from "@/lib/api/pricing";
import { formatMoney } from "@/lib/money";
import { summarizeTiers } from "./tiers";

const SCOPE_LABELS: Record<PriceHistoryScope, string> = { list: "Fiyat listesi", customer: "Müşteriye özel", card: "Ürün kartı" };

/** Geçmiş kaydının önceki/sonraki değeri: kademe dizisi, kart fiyatı (Money4) ya da yok. */
export function formatHistoryValue(value: PriceHistoryDto["before"]): string {
    if (value === null || value === undefined) return "—";
    if (Array.isArray(value)) return summarizeTiers(value as PriceTier[]);
    const money = value as Money4;
    return formatMoney(money.amount, money.currency);
}

const dateTime = new Intl.DateTimeFormat("tr-TR", { dateStyle: "short", timeStyle: "short" });

/** Bir kalemin fiyat değişiklik geçmişi (en yeni önce). */
export default function PriceHistory({ productId, variantId }: { productId: string; variantId?: string }) {
    const [entries, setEntries] = useState<PriceHistoryDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let ignore = false;
        pricing.history({ productId, variantId, pageSize: 50 }).then(
            (r) => !ignore && setEntries(r.items),
            (e) => {
                if (!ignore) {
                    setError(errorMessage(e));
                    setEntries([]);
                }
            },
        );
        return () => {
            ignore = true;
        };
    }, [productId, variantId]);

    return (
        <>
            <FormAlert error={error} />
            {entries === null ? (
                <Skeleton variant="rounded" height={100} />
            ) : entries.length === 0 ? (
                <Typography color="text.secondary">Henüz fiyat değişikliği kaydı yok.</Typography>
            ) : (
                <TableContainer>
                    <Table size="small" aria-label="Fiyat geçmişi">
                        <TableHead>
                            <TableRow>
                                <TableCell>Tarih</TableCell>
                                <TableCell>Kullanıcı</TableCell>
                                <TableCell>Kaynak</TableCell>
                                <TableCell>Birim</TableCell>
                                <TableCell>Önce</TableCell>
                                <TableCell>Sonra</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {entries.map((h) => (
                                <TableRow key={h.id}>
                                    <TableCell>{dateTime.format(new Date(h.at))}</TableCell>
                                    <TableCell>{h.actorLabel}</TableCell>
                                    <TableCell>{SCOPE_LABELS[h.scope]}</TableCell>
                                    <TableCell>{h.unit}</TableCell>
                                    <TableCell>{formatHistoryValue(h.before)}</TableCell>
                                    <TableCell>{formatHistoryValue(h.after)}</TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}
        </>
    );
}
