"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import type { SetupStepProps } from "@/config/setupSteps";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp } from "@/lib/api/erp";
import { identity } from "@/lib/api/identity";
import { invoicing, type EBelgeSettings, type InvoiceMode, type SeriesGroup } from "@/lib/api/invoicing";
import { useSession } from "@/session/SessionProvider";

type DefaultSeries = { kindGroup: SeriesGroup; mode: InvoiceMode; prefix: string };

/** e-Belge açıkken: e-Fatura ve e-Arşiv serileri (3 karakterli, birbirinden farklı önekler); kapalıyken kâğıt serileri. */
export function defaultSeriesFor(enabled: boolean): DefaultSeries[] {
    if (!enabled) {
        return [
            { kindGroup: "sales", mode: "paper", prefix: "F" },
            { kindGroup: "purchase", mode: "paper", prefix: "AF" },
        ];
    }
    return [
        { kindGroup: "sales", mode: "eInvoice", prefix: "FAT" },
        { kindGroup: "sales", mode: "eArchive", prefix: "ARS" },
        { kindGroup: "purchase", mode: "eInvoice", prefix: "ALF" },
        { kindGroup: "purchase", mode: "eArchive", prefix: "ALA" },
    ];
}

/** e-Fatura adımı: "e-Fatura kullanıyor musunuz?" yanıtını kaydeder ve şubenin varsayılan belge serilerini oluşturur. */
export default function EBelgeStep({ onNext, onBack }: SetupStepProps) {
    const { hasPermission } = useSession();
    const canSettings = hasPermission("ayarlar.ebelge");
    const canSeries = hasPermission("ayarlar.belgeler");
    const [answer, setAnswer] = useState<"yes" | "no">("no");
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async () => {
        setSaving(true);
        setError(null);
        try {
            const enabled = answer === "yes";
            const current = await erp.settings.get<EBelgeSettings>("ebelge");
            if (current.value.enabled !== enabled) {
                await erp.settings.put<EBelgeSettings>("ebelge", current.version, { ...current.value, enabled });
            }
            if (canSeries) {
                const branches = await identity.branches.list();
                const branch = branches.find((b) => b.isDefault) ?? branches[0];
                if (branch) {
                    for (const series of defaultSeriesFor(enabled)) {
                        try {
                            await invoicing.series.create({ ...series, branchId: branch.id, isDefault: true });
                        } catch (e) {
                            // Seri zaten varsa (tekrar ziyaret) sessizce geç.
                            if (!(e instanceof ApiError && e.code === "series_exists")) throw e;
                        }
                    }
                }
            }
            onNext();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    if (!canSettings) {
        return (
            <Stack spacing={2}>
                <Alert severity="info">e-Belge ayarı için yetkiniz yok; bu adımı atlayabilirsiniz.</Alert>
                <Stack direction="row" spacing={1}>
                    <Button onClick={onBack}>Geri</Button>
                    <Button variant="contained" onClick={onNext}>
                        Devam
                    </Button>
                </Stack>
            </Stack>
        );
    }

    return (
        <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            <Typography variant="h6">e-Fatura kullanıyor musunuz?</Typography>
            <Typography variant="body2" color="text.secondary">
                Evet derseniz faturalarınız e-Fatura / e-Arşiv serileriyle numaralanır; hayır derseniz kâğıt fatura serileri açılır. Bunu daha sonra Ayarlar &gt; e-Belge
                bölümünden değiştirebilirsiniz.
            </Typography>
            <RadioGroup value={answer} onChange={(e) => setAnswer(e.target.value as "yes" | "no")}>
                <FormControlLabel value="yes" control={<Radio />} label="Evet, e-Fatura kullanıyorum" />
                <FormControlLabel value="no" control={<Radio />} label="Hayır, kâğıt fatura kullanıyorum" />
            </RadioGroup>
            <Stack direction="row" spacing={1}>
                <Button onClick={onBack}>Geri</Button>
                <Button variant="contained" disabled={saving} onClick={() => void submit()}>
                    Devam
                </Button>
            </Stack>
        </Stack>
    );
}
