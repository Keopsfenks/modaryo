import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import { formatMoney } from "@/lib/money";
import ShotFrame from "./ShotFrame";

const basket = [
    { name: "Pamuklu havlu", qty: 2, price: 249.9 },
    { name: "Yatak örtüsü", qty: 1, price: 890 },
    { name: "Mutfak önlüğü", qty: 3, price: 119.5 },
];
const total = basket.reduce((s, l) => s + l.qty * l.price, 0);

/** Perakende satış ekranı: sepet, toplam ve ödeme türü. */
export default function PosShot() {
    return (
        <ShotFrame title="Perakende satış" label="Perakende kasa ekranı: sepet kalemleri, toplam ve ödeme türü seçenekleri">
            <Box sx={{ display: "grid", gap: 0.75 }}>
                {basket.map((l) => (
                    <Box key={l.name} sx={{ display: "flex", justifyContent: "space-between", py: 0.9, borderBottom: "1px solid", borderColor: "divider" }}>
                        <Box>
                            <Box sx={{ color: "text.primary", fontWeight: 600 }}>{l.name}</Box>
                            <Box sx={{ color: "text.secondary" }}>{l.qty} × {formatMoney(l.price, "TRY")}</Box>
                        </Box>
                        <Box sx={{ color: "text.primary", fontWeight: 600 }}>{formatMoney(l.qty * l.price, "TRY")}</Box>
                    </Box>
                ))}
            </Box>
            <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", mt: 1.5 }}>
                <Box sx={{ color: "text.secondary" }}>Toplam</Box>
                <Box sx={{ color: "text.primary", fontWeight: 800, fontSize: "1.25rem" }}>{formatMoney(total, "TRY")}</Box>
            </Box>
            <Box sx={{ display: "flex", gap: 1, mt: 1.5 }}>
                <Chip label="Nakit" color="primary" sx={{ flex: 1 }} />
                <Chip label="Kredi kartı" variant="outlined" sx={{ flex: 1 }} />
                <Chip label="Havale" variant="outlined" sx={{ flex: 1 }} />
            </Box>
        </ShotFrame>
    );
}
