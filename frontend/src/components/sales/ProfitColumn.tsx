"use client";

import TableCell from "@mui/material/TableCell";
import Typography from "@mui/material/Typography";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

/** Kâr sütunu yalnızca maliyet.read izniyle görünür (sunucu da tahmini maliyeti bu izin olmadan döndürmez). */
export function useCanSeeProfit(): boolean {
    return useSession().hasPermission("maliyet.read");
}

export function ProfitHeaderCell() {
    const allowed = useCanSeeProfit();
    if (!allowed) return null;
    return <TableCell sx={{ width: 130 }}>Marj</TableCell>;
}

type ProfitCellProps = { cost: number | null; margin: number | null; currency?: string };

export function ProfitCell({ cost, margin, currency = "TRY" }: ProfitCellProps) {
    const allowed = useCanSeeProfit();
    if (!allowed) return null;
    return (
        <TableCell>
            {margin === null ? (
                "—"
            ) : (
                <>
                    <Typography variant="body2" color={margin < 0 ? "error" : "text.primary"}>
                        % {margin.toLocaleString("tr-TR", { maximumFractionDigits: 1 })}
                    </Typography>
                    {cost !== null && (
                        <Typography variant="caption" color="text.secondary">
                            Maliyet {formatMoney(cost, currency as Currency)}
                        </Typography>
                    )}
                </>
            )}
        </TableCell>
    );
}
