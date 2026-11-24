"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import Checkbox from "@mui/material/Checkbox";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { SetupStepProps } from "@/config/setupSteps";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity } from "@/lib/api/identity";
import { treasury } from "@/lib/api/treasury";
import { useSession } from "@/session/SessionProvider";

/** Kasa adımı: şubenin ilk TL kasasını oluşturur; istenirse bir banka hesabı da ekler. Kasa zaten varsa yeniden oluşturmaz. */
export default function CashStep({ onNext, onBack }: SetupStepProps) {
    const { hasPermission } = useSession();
    const canManage = hasPermission("finans.manage");
    const [name, setName] = useState("Merkez Kasa");
    const [withBank, setWithBank] = useState(false);
    const [bankName, setBankName] = useState("");
    const [iban, setIban] = useState("");
    const [hasCash, setHasCash] = useState(false);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!canManage) return;
        let cancelled = false;
        (async () => {
            try {
                const accounts = await treasury.accounts();
                if (!cancelled) setHasCash(accounts.some((a) => a.kind === "cash"));
            } catch (e) {
                if (!cancelled) setError(errorMessage(e));
            }
        })();
        return () => {
            cancelled = true;
        };
    }, [canManage]);

    const bankValid = !withBank || (bankName.trim() !== "" && iban.trim() !== "");

    const submit = async () => {
        setSaving(true);
        setError(null);
        try {
            const branches = await identity.branches.list();
            const branch = branches.find((b) => b.isDefault) ?? branches[0];
            if (!branch) throw new Error("no-branch");
            if (!hasCash) {
                await treasury.createAccount({ kind: "cash", name: name.trim(), currency: "TRY", branchId: branch.id });
                setHasCash(true);
            }
            if (withBank) {
                await treasury.createAccount({
                    kind: "bank",
                    name: `${bankName.trim()} TL`,
                    currency: "TRY",
                    branchId: branch.id,
                    iban: iban.trim(),
                    bankName: bankName.trim(),
                });
                setWithBank(false);
            }
            onNext();
        } catch (e) {
            setError(e instanceof Error && e.message === "no-branch" ? "Şube bulunamadı." : errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    if (!canManage) {
        return (
            <Stack spacing={2}>
                <Alert severity="info">Kasa tanımlamak için finans yönetimi yetkisi gerekir; bu adımı atlayabilirsiniz.</Alert>
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
                Tahsilat ve ödemeleriniz kasa ve banka hesaplarına işlenir. İlk TL kasanızı oluşturun; dilerseniz bir banka hesabı da ekleyebilirsiniz.
            </Typography>
            {hasCash ? (
                <Alert severity="success">Bir kasanız zaten var; yeniden oluşturulmayacak.</Alert>
            ) : (
                <TextField label="Kasa adı" value={name} onChange={(e) => setName(e.target.value)} sx={{ maxWidth: 360 }} />
            )}
            <FormControlLabel control={<Checkbox checked={withBank} onChange={(e) => setWithBank(e.target.checked)} />} label="Banka hesabı da ekle" />
            {withBank && (
                <Stack spacing={2} sx={{ maxWidth: 360 }}>
                    <TextField label="Banka adı" value={bankName} onChange={(e) => setBankName(e.target.value)} />
                    <TextField label="IBAN" value={iban} onChange={(e) => setIban(e.target.value)} />
                </Stack>
            )}
            <Stack direction="row" spacing={1}>
                <Button onClick={onBack}>Geri</Button>
                <Button variant="contained" disabled={(!hasCash && !name.trim()) || !bankValid || saving} onClick={() => void submit()}>
                    Devam
                </Button>
            </Stack>
        </Stack>
    );
}
