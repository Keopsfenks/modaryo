"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import OpeningImport from "@/components/inventory/OpeningImport";
import type { SetupStepProps } from "@/config/setupSteps";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity } from "@/lib/api/identity";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";

/** İlk depo adımı: şubenin varsayılan deposunu (yoksa oluşturur) adlandırır ve istenirse açılış stoklarını Excel'den yükletir. */
export default function WarehouseStep({ onNext, onBack }: SetupStepProps) {
    const { hasPermission } = useSession();
    const canManage = hasPermission("stok.manage");
    const [warehouse, setWarehouse] = useState<WarehouseDto | null>(null);
    const [name, setName] = useState("");
    const [showImport, setShowImport] = useState(false);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!canManage) return;
        let cancelled = false;
        (async () => {
            try {
                const branches = await identity.branches.list();
                const branch = branches.find((b) => b.isDefault) ?? branches[0];
                if (!branch) return;
                const dto = await inventory.defaultWarehouse(branch.id);
                if (cancelled) return;
                setWarehouse(dto);
                setName(dto.name);
            } catch (e) {
                if (!cancelled) setError(errorMessage(e));
            }
        })();
        return () => {
            cancelled = true;
        };
    }, [canManage]);

    const submit = async () => {
        if (!warehouse) return;
        setSaving(true);
        setError(null);
        try {
            const trimmed = name.trim();
            if (trimmed !== warehouse.name) {
                await inventory.updateWarehouse(warehouse.id, { code: warehouse.code, name: trimmed, kind: warehouse.kind });
            }
            onNext();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    if (!canManage) {
        return (
            <Stack spacing={2}>
                <Alert severity="info">Depo tanımlamak için stok yönetimi yetkisi gerekir; bu adımı atlayabilirsiniz.</Alert>
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
            <Typography variant="body2" color="text.secondary">
                Stoklarınız şubenizin varsayılan deposunda tutulur. Adını dilediğiniz gibi değiştirebilir, dilerseniz açılış stoklarını Excel ile yükleyebilirsiniz.
            </Typography>
            <TextField label="Depo adı" value={name} onChange={(e) => setName(e.target.value)} disabled={!warehouse} sx={{ maxWidth: 360 }} />
            <div>
                <Button variant="outlined" onClick={() => setShowImport((v) => !v)}>
                    Açılış stoklarını Excel&apos;den yükle
                </Button>
            </div>
            {showImport && <OpeningImport />}
            <Stack direction="row" spacing={1}>
                <Button onClick={onBack}>Geri</Button>
                <Button variant="contained" disabled={!warehouse || !name.trim() || saving} onClick={() => void submit()}>
                    Devam
                </Button>
            </Stack>
        </Stack>
    );
}
