import Box from "@mui/material/Box";
import { formatMoney } from "@/lib/money";
import ShotFrame, { ShotTable } from "./ShotFrame";

const items = [
    { dir: "Tahsil edilecek", date: "02.10.2026", due: "01.11.2026", remaining: 12000, late: 21 },
    { dir: "Tahsil edilecek", date: "15.10.2026", due: "14.11.2026", remaining: 2400, late: 8 },
    { dir: "Ödenecek", date: "20.10.2026", due: "19.12.2026", remaining: 8750, late: 0 },
    { dir: "Tahsil edilecek", date: "05.11.2026", due: "05.01.2027", remaining: 31500, late: 0 },
];

/** Vade takibi (Yön, Tarih, Vade, Kalan, Gecikme günü); vadesi geçenler kırmızı. */
export default function DueTrackingShot() {
    return (
        <ShotFrame title="Vade takibi" label="Vade takibi: açık kalemler, vade tarihi, kalan tutar ve gecikme günü">
            <ShotTable
                columns={[
                    { label: "Yön", width: "1.4fr" },
                    { label: "Tarih" },
                    { label: "Vade" },
                    { label: "Kalan", align: "right" },
                    { label: "Gecikme günü", align: "right", width: "1.1fr" },
                ]}
                rows={items.map((i) => [
                    i.dir,
                    i.date,
                    <Box key="d" component="span" sx={{ color: i.late ? "error.main" : undefined, fontWeight: i.late ? 700 : undefined }}>{i.due}</Box>,
                    formatMoney(i.remaining, "TRY"),
                    <Box key="l" component="span" sx={{ color: "error.main", fontWeight: 700 }}>{i.late || ""}</Box>,
                ])}
            />
        </ShotFrame>
    );
}
