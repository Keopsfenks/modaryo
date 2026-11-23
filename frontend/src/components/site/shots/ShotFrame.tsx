import type { ReactNode } from "react";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";

type ShotFrameProps = {
    title: string;
    children: ReactNode;
    /** Görselin altındaki not; varsayılan "Örnek veri". */
    caption?: string;
    /** Ekran okuyucu açıklaması; verilmezse başlıktan türetilir. */
    label?: string;
};

/**
 * Ürün ekranı çerçevesi. Süs amaçlıdır: iç içerik ekran okuyuculardan gizlenir,
 * çerçeve açıklayıcı bir aria-label taşır. Gerçek müşteri verisi değil, uydurma örnek veridir.
 */
export default function ShotFrame({ title, children, caption = "Örnek veri", label }: ShotFrameProps) {
    return (
        <Box
            role="img"
            aria-label={label ?? `${title} ekranı, örnek verilerle hazırlanmış görsel`}
            sx={{
                border: "1px solid",
                borderColor: "divider",
                borderRadius: "10px",
                bgcolor: "background.paper",
                overflow: "hidden",
                textAlign: "left",
                userSelect: "none",
            }}
        >
            <Box aria-hidden="true">
                <Box sx={{ display: "flex", alignItems: "center", gap: 1, px: 1.5, py: 1, borderBottom: "1px solid", borderColor: "divider", bgcolor: "grey.100" }}>
                    <Box sx={{ display: "flex", gap: 0.5 }}>
                        {[0, 1, 2].map((i) => (
                            <Box key={i} sx={{ width: 8, height: 8, borderRadius: "50%", bgcolor: "grey.300" }} />
                        ))}
                    </Box>
                    <Typography component="span" sx={{ fontSize: "0.75rem", fontWeight: 600, color: "text.secondary" }}>
                        {title}
                    </Typography>
                </Box>
                <Box sx={{ p: { xs: 1.5, sm: 2 }, fontSize: "0.75rem" }}>{children}</Box>
            </Box>
            <Typography
                component="div"
                sx={{ px: 1.5, py: 0.75, fontSize: "0.6875rem", color: "text.secondary", textAlign: "right", borderTop: "1px solid", borderColor: "divider" }}
            >
                {caption}
            </Typography>
        </Box>
    );
}

export type ShotColumn = { label: string; align?: "left" | "right"; width?: string };

/** Hafif tablo: gerçek ekranlardaki sütun adlarını taşır, MUI Table kadar ağır değildir. */
export function ShotTable({ columns, rows }: { columns: ShotColumn[]; rows: ReactNode[][] }) {
    const template = columns.map((c) => c.width ?? "1fr").join(" ");
    const row = { display: "grid", gridTemplateColumns: template, columnGap: 1.5, alignItems: "center", py: 0.9, borderBottom: "1px solid", borderColor: "divider" } as const;

    return (
        <Box sx={{ minWidth: 0 }}>
            <Box sx={{ ...row, fontWeight: 700, color: "text.primary" }}>
                {columns.map((c) => (
                    <Box key={c.label} sx={{ textAlign: c.align ?? "left", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {c.label}
                    </Box>
                ))}
            </Box>
            {rows.map((cells, r) => (
                <Box key={r} sx={{ ...row, color: "text.secondary", "&:last-of-type": { borderBottom: 0 } }}>
                    {cells.map((cell, i) => (
                        <Box key={i} sx={{ textAlign: columns[i].align ?? "left", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                            {cell}
                        </Box>
                    ))}
                </Box>
            ))}
        </Box>
    );
}

/** "1.234,50 ₺ (B)" çıktısını tutar + (B)/(A) etiketi olarak gösterir. */
export function BalanceCell({ text }: { text: string }) {
    const match = /^(.*) \((B|A)\)$/.exec(text);
    if (!match) {
        return <>{text}</>;
    }
    return (
        <Box component="span" sx={{ display: "inline-flex", alignItems: "center", gap: 0.5 }}>
            <Box component="span" sx={{ color: "text.primary", fontWeight: 600 }}>{match[1]}</Box>
            <Chip size="small" label={`(${match[2]})`} color={match[2] === "B" ? "primary" : "warning"} variant="outlined" sx={{ height: 18, fontSize: "0.65rem", "& .MuiChip-label": { px: 0.6 } }} />
        </Box>
    );
}
