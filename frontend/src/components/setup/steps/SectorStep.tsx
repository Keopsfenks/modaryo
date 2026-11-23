"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { sectorLabels, sectorModules, type Sector, type SetupStepProps } from "@/config/setupSteps";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type ModuleInfo } from "@/lib/api/identity";
import { useSession } from "@/session/SessionProvider";
import { patchSetup } from "../setupSettings";

/** Sektör adımı: seçilen sektörün modüllerini mevcut açık modüllere ekler ve sektörü kaydeder. */
export default function SectorStep({ onNext, onBack }: SetupStepProps) {
    const { refresh } = useSession();
    const [catalog, setCatalog] = useState<ModuleInfo[]>([]);
    const [sector, setSector] = useState<Sector | null>(null);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        identity.modules.list().then(setCatalog, () => setCatalog([]));
    }, []);

    const nameOf = (key: string) => catalog.find((m) => m.key === key)?.name ?? key;
    const picked = sector ? sectorModules[sector] : [];

    const submit = async () => {
        if (!sector) {
            return;
        }
        setSaving(true);
        setError(null);
        try {
            const current = (await identity.modules.list()).filter((m) => m.enabled).map((m) => m.key);
            await identity.modules.set([...new Set([...current, ...sectorModules[sector]])]);
            await patchSetup({ sector });
            await refresh();
            onNext();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    return (
        <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            <Typography variant="body2" color="text.secondary">
                Sektörünüze uygun modülleri sizin için açalım. Dilediğiniz zaman Ayarlar &gt; Modüller bölümünden değiştirebilirsiniz.
            </Typography>
            <RadioGroup value={sector ?? ""} onChange={(e) => setSector(e.target.value as Sector)}>
                {(Object.keys(sectorLabels) as Sector[]).map((key) => (
                    <FormControlLabel key={key} value={key} control={<Radio />} label={sectorLabels[key]} />
                ))}
            </RadioGroup>
            {sector && (
                <Stack spacing={0.5}>
                    <Typography variant="subtitle2">Açılacak modüller</Typography>
                    {picked.length === 0 ? (
                        <Typography variant="body2" color="text.secondary">
                            Ek modül açılmayacak.
                        </Typography>
                    ) : (
                        picked.map((key) => (
                            <Typography key={key} variant="body2">
                                {nameOf(key)}
                            </Typography>
                        ))
                    )}
                </Stack>
            )}
            <Stack direction="row" spacing={1}>
                <Button onClick={onBack}>Geri</Button>
                <Button variant="contained" disabled={!sector || saving} onClick={() => void submit()}>
                    Devam
                </Button>
            </Stack>
        </Stack>
    );
}
