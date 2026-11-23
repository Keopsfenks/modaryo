import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import { formatMoney } from "@/lib/money";
import ShotFrame, { ShotTable } from "./ShotFrame";

const lines = [
    { name: "Zeytinyağı 5 lt", qty: 24, price: 1180 },
    { name: "Ayçiçek yağı 2 lt", qty: 60, price: 245.5 },
    { name: "Makarna 500 g (koli)", qty: 15, price: 310 },
];

const subtotal = lines.reduce((sum, l) => sum + l.qty * l.price, 0);
const vat = subtotal * 0.2;

/** Satış faturası taslağı: cari, kalemler ve KDV içeren toplam. */
export default function InvoiceShot() {
    return (
        <ShotFrame title="Satış faturası" label="Satış faturası taslağı: müşteri, kalemler, KDV ve genel toplam">
            <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Box>
                    <Box sx={{ color: "text.secondary" }}>Müşteri</Box>
                    <Box sx={{ color: "text.primary", fontWeight: 700, fontSize: "0.875rem" }}>Örnek Gıda A.Ş.</Box>
                </Box>
                <Chip size="small" label="Taslak" color="warning" variant="outlined" />
            </Box>
            <ShotTable
                columns={[
                    { label: "Ürün", width: "2fr" },
                    { label: "Miktar", align: "right", width: "0.7fr" },
                    { label: "Birim fiyat", align: "right", width: "1fr" },
                    { label: "Tutar", align: "right", width: "1fr" },
                ]}
                rows={lines.map((l) => [l.name, l.qty, formatMoney(l.price, "TRY"), formatMoney(l.qty * l.price, "TRY")])}
            />
            <Box sx={{ mt: 1.5, ml: "auto", maxWidth: 220, display: "grid", gap: 0.5 }}>
                <Box sx={{ display: "flex", justifyContent: "space-between" }}><span>Ara toplam</span><span>{formatMoney(subtotal, "TRY")}</span></Box>
                <Box sx={{ display: "flex", justifyContent: "space-between" }}><span>KDV %20</span><span>{formatMoney(vat, "TRY")}</span></Box>
                <Box sx={{ display: "flex", justifyContent: "space-between", color: "text.primary", fontWeight: 700, fontSize: "0.875rem", pt: 0.5, borderTop: "1px solid", borderColor: "divider" }}>
                    <span>Genel toplam</span><span>{formatMoney(subtotal + vat, "TRY")}</span>
                </Box>
            </Box>
        </ShotFrame>
    );
}
