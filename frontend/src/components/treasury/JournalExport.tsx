"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { exportJournal } from "@/lib/api/treasury";
import { addDaysIso, todayIso } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";

const MAX_DAYS = 366;
const dayNumber = (iso: string) => Date.UTC(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)) - 1, Number(iso.slice(8, 10))) / 86_400_000;

/** Muhasebe aktarımı: seçilen tarih aralığının yevmiye satırlarını Excel olarak indirir (en çok 366 gün). */
export default function JournalExport() {
    const { hasPermission } = useSession();
    const [from, setFrom] = useState(addDaysIso(todayIso(), -30));
    const [to, setTo] = useState(todayIso());
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    if (!hasPermission("muhasebe.export")) return <Alert severity="warning">Muhasebe aktarımı için yetkiniz yok.</Alert>;

    const span = from && to ? dayNumber(to) - dayNumber(from) : -1;
    const rangeError = !from || !to ? "Tarih aralığı seçin." : span < 0 ? "Başlangıç tarihi bitişten sonra olamaz." : span > MAX_DAYS ? `Aralık en çok ${MAX_DAYS} gün olabilir.` : null;

    async function run() {
        setBusy(true);
        setError(null);
        try {
            await exportJournal(from, to);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title="Muhasebe aktarımı" subtitle="Fatura, tahsilat, ödeme, masraf, çek/senet ve mahsup fişleri hesap planı kodlarıyla yevmiye satırlarına dökülür." />
            <DashboardCard>
                <Stack spacing={2}>
                    <FormAlert error={error} />
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                        <TextField size="small" type="date" label="Başlangıç" value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField size="small" type="date" label="Bitiş" value={to} onChange={(e) => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>
                    {rangeError && <Typography color="error">{rangeError}</Typography>}
                    <Typography variant="body2" color="text.secondary">
                        Sütunlar: Fiş no, Tarih, Hesap kodu, Açıklama, Borç, Alacak, Belge türü, Belge no. İptal edilen belgeler ters fişle birlikte gelir. Hesap eşlemesi Ayarlar &gt; Finans bölümünden düzenlenir.
                    </Typography>
                    <div>
                        <Button variant="contained" disabled={busy || !!rangeError} onClick={() => void run()}>
                            {busy ? "Hazırlanıyor..." : "Excel olarak indir"}
                        </Button>
                    </div>
                </Stack>
            </DashboardCard>
        </>
    );
}
