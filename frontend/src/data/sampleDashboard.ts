// ÖRNEK VERİ — ERP servisine (services/erp) bağlanana kadar dashboard'u beslemek için.
// API entegrasyonu yapıldığında bu dosya kaldırılacak.

export type PaletteColor = "primary" | "secondary" | "success" | "warning" | "error" | "info";

export type Kpi = {
    key: "employees" | "customers" | "openOrders" | "stockItems" | "monthlyRevenue" | "pendingInvoices";
    label: string;
    value: number;
    kind: "number" | "currency";
    color: PaletteColor;
};

export const kpis: Kpi[] = [
    { key: "employees", label: "Personel", value: 48, kind: "number", color: "primary" },
    { key: "customers", label: "Müşteriler", value: 1250, kind: "number", color: "warning" },
    { key: "openOrders", label: "Açık Siparişler", value: 86, kind: "number", color: "secondary" },
    { key: "stockItems", label: "Stok Kalemleri", value: 3420, kind: "number", color: "error" },
    { key: "monthlyRevenue", label: "Bu Ay Ciro", value: 1_245_000, kind: "currency", color: "success" },
    { key: "pendingInvoices", label: "Bekleyen Fatura", value: 37, kind: "number", color: "info" },
];

export type RevenuePeriod = {
    id: string;
    label: string;
    months: string[];
    income: number[];
    expense: number[];
};

export const revenuePeriods: RevenuePeriod[] = [
    {
        id: "2026-h2",
        label: "Haz – Kas 2026",
        months: ["Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas"],
        income: [820_000, 910_000, 760_000, 1_050_000, 1_180_000, 1_245_000],
        expense: [540_000, 610_000, 580_000, 690_000, 720_000, 760_000],
    },
    {
        id: "2026-h1",
        label: "Ara 2025 – May 2026",
        months: ["Ara", "Oca", "Şub", "Mar", "Nis", "May"],
        income: [690_000, 610_000, 650_000, 780_000, 740_000, 860_000],
        expense: [480_000, 450_000, 470_000, 520_000, 500_000, 560_000],
    },
];

export const salesByChannel = {
    total: 9_340_000,
    changePercent: 12,
    labels: ["Toptan", "Perakende", "E-ticaret"],
    values: [4_650_000, 2_890_000, 1_800_000],
};

export const monthlyCollection = {
    total: 1_085_000,
    changePercent: -4,
    series: [120_000, 180_000, 140_000, 210_000, 160_000, 240_000, 190_000, 260_000],
};

export type OrderStatus = "hazirlaniyor" | "kargoda" | "teslim" | "iptal";

export type Order = {
    id: string;
    customer: string;
    city: string;
    date: string;
    status: OrderStatus;
    amount: number;
};

export const recentOrders: Order[] = [
    { id: "SP-2026-1184", customer: "Akdeniz Tekstil A.Ş.", city: "Antalya", date: "2026-11-20", status: "hazirlaniyor", amount: 184_500 },
    { id: "SP-2026-1183", customer: "Yıldız Moda Ltd.", city: "İstanbul", date: "2026-11-20", status: "kargoda", amount: 62_300 },
    { id: "SP-2026-1182", customer: "Ege Butik", city: "İzmir", date: "2026-11-19", status: "teslim", amount: 18_750 },
    { id: "SP-2026-1181", customer: "Karadeniz Giyim", city: "Trabzon", date: "2026-11-18", status: "iptal", amount: 41_200 },
    { id: "SP-2026-1180", customer: "Başkent Konfeksiyon", city: "Ankara", date: "2026-11-18", status: "teslim", amount: 96_900 },
];

export type StockAlert = {
    sku: string;
    name: string;
    warehouse: string;
    quantity: number;
    minimum: number;
};

export const criticalStock: StockAlert[] = [
    { sku: "KMS-0142", name: "Pamuk Gömlek – Beyaz / M", warehouse: "Merkez Depo", quantity: 12, minimum: 60 },
    { sku: "PNT-0387", name: "Kanvas Pantolon – Lacivert / 32", warehouse: "Merkez Depo", quantity: 28, minimum: 50 },
    { sku: "TRK-0921", name: "Triko Kazak – Gri / L", warehouse: "İzmir Depo", quantity: 9, minimum: 40 },
    { sku: "CKT-0215", name: "Şişme Mont – Siyah / XL", warehouse: "Ankara Depo", quantity: 33, minimum: 45 },
];
