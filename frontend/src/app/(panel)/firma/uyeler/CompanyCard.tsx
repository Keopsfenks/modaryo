"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { validateCompanyName } from "@/lib/validation";
import { useSession } from "@/session/SessionProvider";

/** Firma adı (company.update izniyle) ve firmadan ayrılma. */
export default function CompanyCard() {
    const { session, hasPermission, refresh } = useSession();
    const router = useRouter();
    const canEdit = hasPermission("company.update");
    const [name, setName] = useState(session.activeCompany?.name ?? "");
    const [fieldError, setFieldError] = useState<string>();
    const [error, setError] = useState<ApiError | null>(null);
    const [saved, setSaved] = useState(false);
    const [confirmLeave, setConfirmLeave] = useState(false);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        identity.getCurrentCompany().then((company) => setName(company.name), () => undefined);
    }, []);

    const save = async (event: React.FormEvent) => {
        event.preventDefault();
        const validation = validateCompanyName(name);
        setFieldError(validation);
        setError(null);
        setSaved(false);
        if (validation) return;

        setBusy(true);
        try {
            await identity.updateCompany(name.trim());
            await refresh();
            setSaved(true);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        } finally {
            setBusy(false);
        }
    };

    const leave = async () => {
        setBusy(true);
        setError(null);
        try {
            await identity.leaveCompany();
            // Ayrılınan firmadaki oturum firmasız kalır; başka firması varsa ona geç.
            const other = session.companies.find((c) => c.id !== session.activeCompany?.id);
            if (other) {
                await identity.switchTenant(other.id);
                router.replace("/");
            } else {
                router.replace("/baslangic");
            }
        } catch (e) {
            setBusy(false);
            setConfirmLeave(false);
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    return (
        <DashboardCard title="Firma">
            <FormAlert error={error} />
            {saved && <FormAlert severity="success" error="Firma adı güncellendi." />}
            <Box component="form" noValidate onSubmit={save}>
                <Stack spacing={2}>
                    <TextField
                        id="companyName"
                        label="Firma adı"
                        fullWidth
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        error={Boolean(fieldError)}
                        helperText={fieldError}
                        slotProps={{ htmlInput: { readOnly: !canEdit } }}
                    />
                    <Stack direction="row" spacing={1.5}>
                        {canEdit && (
                            <Button type="submit" variant="contained" disabled={busy}>
                                Kaydet
                            </Button>
                        )}
                        <Button color="error" onClick={() => setConfirmLeave(true)} disabled={busy}>
                            Firmadan ayrıl
                        </Button>
                    </Stack>
                </Stack>
            </Box>

            <Dialog open={confirmLeave} onClose={() => setConfirmLeave(false)}>
                <DialogTitle>Firmadan ayrılmak istiyor musun?</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        {session.activeCompany?.name} firmasının verilerine artık erişemeyeceksin. Geri dönmek için yeniden davet edilmen gerekir.
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmLeave(false)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void leave()} disabled={busy}>
                        Ayrıl
                    </Button>
                </DialogActions>
            </Dialog>
        </DashboardCard>
    );
}
