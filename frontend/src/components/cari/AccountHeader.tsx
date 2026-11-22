import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";
import DashboardCard from "@/components/dashboard/DashboardCard";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { formatBalance, formatMoney } from "@/lib/money";

type AccountHeaderProps = {
    account: CurrentAccountDto;
    canManage: boolean;
    busy?: boolean;
    onToggleActive: () => void;
    onDelete: () => void;
};

function Stat({ label, value }: { label: string; value: string }) {
    return (
        <Box>
            <Typography variant="caption" color="text.secondary" component="div">
                {label}
            </Typography>
            <Typography variant="h6" component="div">
                {value}
            </Typography>
        </Box>
    );
}

/** Detay sayfası başlığı: kod, ad, rol ve durum rozetleri, bakiye, limit, vade günü ve yönetim butonları. */
export default function AccountHeader({ account, canManage, busy, onToggleActive, onDelete }: AccountHeaderProps) {
    return (
        <DashboardCard>
            <Box sx={{ display: "flex", flexWrap: "wrap", justifyContent: "space-between", gap: 2 }}>
                <Box>
                    <Typography variant="body2" color="text.secondary">
                        {account.code}
                    </Typography>
                    <Typography variant="h4" component="h1">
                        {account.name}
                    </Typography>
                    <Box sx={{ display: "flex", gap: 1, mt: 1 }}>
                        {account.isCustomer && <Chip size="small" color="primary" variant="outlined" label="Müşteri" />}
                        {account.isSupplier && <Chip size="small" color="secondary" variant="outlined" label="Tedarikçi" />}
                        {!account.isActive && <Chip size="small" label="Pasif" />}
                    </Box>
                </Box>
                {canManage && (
                    <Box className="no-print" sx={{ display: "flex", gap: 1, alignItems: "flex-start" }}>
                        <Button variant="outlined" disabled={busy} onClick={onToggleActive}>
                            {account.isActive ? "Pasifleştir" : "Etkinleştir"}
                        </Button>
                        {!account.hasMovements && (
                            <Button variant="outlined" color="error" disabled={busy} onClick={onDelete}>
                                Sil
                            </Button>
                        )}
                    </Box>
                )}
            </Box>
            <Box sx={{ display: "flex", flexWrap: "wrap", gap: 5, mt: 3 }}>
                <Stat label="Bakiye" value={formatBalance(account.balance, account.currency)} />
                <Stat label="Kredi limiti" value={account.creditLimit == null ? "Limit yok" : formatMoney(account.creditLimit, account.currency)} />
                <Stat label="Vade" value={`${account.defaultDueDays} gün`} />
            </Box>
        </DashboardCard>
    );
}
