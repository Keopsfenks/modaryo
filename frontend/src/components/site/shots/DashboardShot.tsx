import Box from "@mui/material/Box";
import { formatMoney } from "@/lib/money";
import ShotFrame from "./ShotFrame";

const kpis = [
    { label: "Aylık satış", value: formatMoney(486250, "TRY"), color: "primary.main" },
    { label: "Tahsilat", value: formatMoney(312400, "TRY"), color: "success.main" },
    { label: "Vadesi geçmiş", value: formatMoney(14400, "TRY"), color: "error.main" },
    { label: "Kritik stok", value: "4 ürün", color: "warning.main" },
];

const bars = [42, 58, 51, 66, 61, 74, 69, 82];
const months = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu"];

/** Panel özeti: dört gösterge ve aylık satış sütunları. */
export default function DashboardShot() {
    return (
        <ShotFrame title="Panel" label="Panel özeti: aylık satış, tahsilat, vadesi geçmiş tutar ve kritik stok göstergeleri ile satış grafiği">
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr 1fr", sm: "repeat(4, 1fr)" }, gap: 1, mb: 2 }}>
                {kpis.map((k) => (
                    <Box key={k.label} sx={{ p: 1.25, borderRadius: 1.5, bgcolor: "grey.100" }}>
                        <Box sx={{ color: "text.secondary" }}>{k.label}</Box>
                        <Box sx={{ color: k.color, fontWeight: 700, fontSize: "0.95rem", mt: 0.25 }}>{k.value}</Box>
                    </Box>
                ))}
            </Box>
            <Box sx={{ color: "text.primary", fontWeight: 700, mb: 1 }}>Aylık satış</Box>
            <svg viewBox="0 0 320 110" width="100%" role="presentation" focusable="false">
                {bars.map((h, i) => (
                    <g key={months[i]}>
                        <rect x={10 + i * 38} y={90 - h} width={24} height={h} rx={4} fill="var(--mui-palette-primary-main)" opacity={i === bars.length - 1 ? 1 : 0.45} />
                        <text x={22 + i * 38} y={106} textAnchor="middle" fontSize="9" fill="var(--mui-palette-text-secondary)">{months[i]}</text>
                    </g>
                ))}
            </svg>
        </ShotFrame>
    );
}
