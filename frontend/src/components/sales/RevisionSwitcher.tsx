"use client";

import Chip from "@mui/material/Chip";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import type { QuoteRevisionRef } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";

type RevisionSwitcherProps = {
    revisions: QuoteRevisionRef[];
    current: number;
    selected: number;
    currency: string;
    onSelect(revision: number): void;
};

/** Teklif revizyonları: eski revizyonlar salt okunur görüntülenir. */
export default function RevisionSwitcher({ revisions, current, selected, currency, onSelect }: RevisionSwitcherProps) {
    if (revisions.length <= 1) return null;
    return (
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
            <Typography variant="body2" color="text.secondary">
                Revizyonlar:
            </Typography>
            {[...revisions]
                .sort((a, b) => a.revision - b.revision)
                .map((r) => (
                    <Chip
                        key={r.revision}
                        size="small"
                        color={r.revision === selected ? "primary" : "default"}
                        variant={r.revision === selected ? "filled" : "outlined"}
                        label={`Rev. ${r.revision}${r.revision === current ? " (güncel)" : ""} · ${formatDate(r.date)} · ${formatMoney(r.total, currency as Currency)}`}
                        onClick={() => onSelect(r.revision)}
                    />
                ))}
        </Stack>
    );
}
