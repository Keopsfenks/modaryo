"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Typography from "@mui/material/Typography";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type ModuleInfo } from "@/lib/api/identity";
import { useSession } from "@/session/SessionProvider";

const waveTitles: Record<number, string> = { 1: "Temel", 2: "Sektör modülleri" };

/** Modül anahtarları: kapatılan modül onay ister, kayıttan sonra oturum yenilenir (menü hemen güncellenir). */
export default function ModulesForm() {
    const { refresh } = useSession();
    const [modules, setModules] = useState<ModuleInfo[] | null>(null);
    const [enabled, setEnabled] = useState<Set<string>>(new Set());
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [confirming, setConfirming] = useState(false);

    useEffect(() => {
        identity.modules.list().then(
            (list) => {
                setModules(list);
                setEnabled(new Set(list.filter((m) => m.enabled).map((m) => m.key)));
            },
            (e: unknown) => {
                setModules([]);
                setError(errorMessage(e));
            },
        );
    }, []);

    if (!modules) {
        return <CircularProgress aria-label="Yükleniyor" />;
    }

    const toggle = (key: string, on: boolean) => {
        setSaved(false);
        setEnabled((prev) => {
            const next = new Set(prev);
            if (on) next.add(key);
            else next.delete(key);
            return next;
        });
    };

    const persist = async () => {
        setConfirming(false);
        setSaving(true);
        setError(null);
        try {
            const list = await identity.modules.set(modules.filter((m) => enabled.has(m.key)).map((m) => m.key));
            setModules(list);
            setEnabled(new Set(list.filter((m) => m.enabled).map((m) => m.key)));
            await refresh();
            setSaved(true);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    const submit = async () => {
        setSaved(false);
        if (modules.some((m) => m.enabled && !enabled.has(m.key))) {
            setConfirming(true);
            return;
        }
        await persist();
    };

    const waves = [...new Set(modules.map((m) => m.wave))].sort();

    return (
        <form
            noValidate
            onSubmit={(e) => {
                e.preventDefault();
                void submit();
            }}
        >
            <Stack spacing={3}>
                {error && <Alert severity="error">{error}</Alert>}
                {saved && !error && <Alert severity="success">Modüller kaydedildi.</Alert>}
                {waves.map((wave) => (
                    <Stack key={wave} spacing={1.5}>
                        <Typography variant="h6" component="h2">
                            {waveTitles[wave] ?? "Diğer modüller"}
                        </Typography>
                        <Stack spacing={1.5}>
                            {modules
                                .filter((m) => m.wave === wave)
                                .map((m) => (
                                    <Card key={m.key} variant="outlined">
                                        <CardContent sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2 }}>
                                            <div>
                                                <Typography sx={{ fontWeight: 600 }}>{m.name}</Typography>
                                                <Typography variant="body2" color="text.secondary">
                                                    {m.description}
                                                </Typography>
                                            </div>
                                            <Switch
                                                checked={enabled.has(m.key)}
                                                onChange={(e) => toggle(m.key, e.target.checked)}
                                                slotProps={{ input: { "aria-label": m.name } }}
                                            />
                                        </CardContent>
                                    </Card>
                                ))}
                        </Stack>
                    </Stack>
                ))}
                <div>
                    <Button type="submit" variant="contained" disabled={saving}>
                        {saving ? "Kaydediliyor..." : "Kaydet"}
                    </Button>
                </div>
            </Stack>
            <Dialog open={confirming} onClose={() => setConfirming(false)}>
                <DialogTitle>Modül kapatılsın mı?</DialogTitle>
                <DialogContent>
                    <DialogContentText>Kapatılan modülün ekranları gizlenir; veriler silinmez.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirming(false)}>Vazgeç</Button>
                    <Button color="error" onClick={() => void persist()}>
                        Kapat ve kaydet
                    </Button>
                </DialogActions>
            </Dialog>
        </form>
    );
}
