import Box from "@mui/material/Box";
import LinearProgress from "@mui/material/LinearProgress";
import ShotFrame from "./ShotFrame";

const items = [
    { name: "Pamuklu havlu", sku: "HV-001", warehouse: "Merkez depo", quantity: 12, minimum: 60 },
    { name: "Yatak örtüsü", sku: "YO-014", warehouse: "Merkez depo", quantity: 38, minimum: 50 },
    { name: "Mutfak önlüğü", sku: "MO-203", warehouse: "Mağaza deposu", quantity: 9, minimum: 40 },
    { name: "Nevresim takımı", sku: "NV-090", warehouse: "Merkez depo", quantity: 44, minimum: 50 },
];

/** Kritik stok: minimum seviyenin altındaki ürünler. */
export default function StockShot() {
    return (
        <ShotFrame title="Kritik stok" label="Kritik stok listesi: minimum seviyenin altındaki ürünler ve stok doluluk çubukları">
            <Box sx={{ display: "grid", gap: 1.75 }}>
                {items.map((item) => {
                    const ratio = Math.round((item.quantity / item.minimum) * 100);
                    const color = ratio < 30 ? "error" : "warning";
                    return (
                        <Box key={item.sku}>
                            <Box sx={{ display: "flex", justifyContent: "space-between", gap: 2, mb: 0.5 }}>
                                <Box>
                                    <Box sx={{ color: "text.primary", fontWeight: 600 }}>{item.name}</Box>
                                    <Box sx={{ color: "text.secondary" }}>{item.sku} · {item.warehouse}</Box>
                                </Box>
                                <Box sx={{ color: `${color}.main`, fontWeight: 600, whiteSpace: "nowrap" }}>{item.quantity} / {item.minimum}</Box>
                            </Box>
                            <LinearProgress variant="determinate" value={ratio} color={color} sx={{ height: 6, borderRadius: 3, bgcolor: `${color}.light` }} />
                        </Box>
                    );
                })}
            </Box>
        </ShotFrame>
    );
}
