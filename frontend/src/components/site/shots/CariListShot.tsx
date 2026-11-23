import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import { formatBalance, formatMoney } from "@/lib/money";
import ShotFrame, { BalanceCell, ShotTable } from "./ShotFrame";

const roleChip = (label: string, color: "primary" | "secondary") => (
    <Chip size="small" variant="outlined" color={color} label={label} sx={{ height: 18, fontSize: "0.65rem", "& .MuiChip-label": { px: 0.6 } }} />
);

const accounts = [
    { code: "C-0001", name: "Örnek Gıda A.Ş.", customer: true, supplier: false, balance: 48250.5, overdue: 12000 },
    { code: "C-0002", name: "Deneme Tekstil Ltd.", customer: true, supplier: true, balance: -15400, overdue: 0 },
    { code: "C-0003", name: "Kurgu Ambalaj San.", customer: false, supplier: true, balance: -3280.75, overdue: 0 },
    { code: "C-0004", name: "Hayali Market", customer: true, supplier: false, balance: 7920, overdue: 2400 },
    { code: "C-0005", name: "Mavi Kırtasiye", customer: true, supplier: false, balance: 0, overdue: 0 },
];

/** Cari hesap listesi (Kod, Ad, Rol, Bakiye, Vadesi geçmiş). */
export default function CariListShot() {
    return (
        <ShotFrame title="Cari hesaplar" label="Cari hesap listesi: kod, ad, rol, bakiye ve vadesi geçmiş tutar sütunları">
            <ShotTable
                columns={[
                    { label: "Kod", width: "0.7fr" },
                    { label: "Ad", width: "1.6fr" },
                    { label: "Rol", width: "1.3fr" },
                    { label: "Bakiye", align: "right", width: "1.5fr" },
                    { label: "Vadesi geçmiş", align: "right", width: "1fr" },
                ]}
                rows={accounts.map((a) => [
                    a.code,
                    <Box key="n" component="span" sx={{ color: "text.primary", fontWeight: 600 }}>{a.name}</Box>,
                    <Box key="r" sx={{ display: "flex", gap: 0.5 }}>
                        {a.customer && roleChip("Müşteri", "primary")}
                        {a.supplier && roleChip("Tedarikçi", "secondary")}
                    </Box>,
                    <BalanceCell key="b" text={formatBalance(a.balance, "TRY")} />,
                    a.overdue > 0 ? formatMoney(a.overdue, "TRY") : "—",
                ])}
            />
        </ShotFrame>
    );
}
