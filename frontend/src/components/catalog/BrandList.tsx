"use client";

import { useCallback, useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconCheck, IconPencil, IconTrash, IconX } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { catalog, type BrandDto } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { useSession } from "@/session/SessionProvider";

/** Marka listesi: ekleme, satır içi yeniden adlandırma ve silme. */
export default function BrandList() {
    const { hasPermission } = useSession();
    const canManage = hasPermission("katalog.manage");

    const [brands, setBrands] = useState<BrandDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [newName, setNewName] = useState("");
    const [renamingId, setRenamingId] = useState<string | null>(null);
    const [renameValue, setRenameValue] = useState("");
    const [deleting, setDeleting] = useState<BrandDto | null>(null);

    const load = useCallback(async () => {
        try {
            setBrands(await catalog.brands());
        } catch (e) {
            setError(errorMessage(e));
            setBrands((current) => current ?? []);
        }
    }, []);

    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [load]);

    async function run(action: () => Promise<unknown>): Promise<boolean> {
        setBusy(true);
        setError(null);
        let ok = true;
        try {
            await action();
        } catch (e) {
            setError(errorMessage(e));
            ok = false;
        }
        await load();
        setBusy(false);
        return ok;
    }

    async function add() {
        const name = newName.trim();
        if (name && (await run(() => catalog.createBrand(name)))) setNewName("");
    }

    async function rename(brand: BrandDto) {
        const name = renameValue.trim();
        if (!name) return;
        if (name === brand.name || (await run(() => catalog.updateBrand(brand.id, name)))) setRenamingId(null);
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        await run(() => catalog.deleteBrand(target.id));
    }

    return (
        <DashboardCard title="Markalar">
            <FormAlert error={error} />
            {canManage && (
                <Stack direction="row" spacing={1} sx={{ mb: 2 }}>
                    <TextField
                        size="small"
                        label="Yeni marka"
                        value={newName}
                        onChange={(e) => setNewName(e.target.value)}
                        onKeyDown={(e) => e.key === "Enter" && void add()}
                        sx={{ minWidth: 220 }}
                    />
                    <Button variant="contained" disabled={busy || !newName.trim()} onClick={() => void add()}>
                        Marka ekle
                    </Button>
                </Stack>
            )}
            {brands === null ? (
                <Skeleton variant="rounded" height={120} />
            ) : brands.length === 0 ? (
                <Typography color="text.secondary" sx={{ py: 3 }}>
                    Henüz marka yok.
                </Typography>
            ) : (
                brands.map((brand) => (
                    <Stack key={brand.id} direction="row" spacing={0.5} sx={{ alignItems: "center", py: 0.5, borderBottom: 1, borderColor: "divider" }}>
                        {renamingId === brand.id ? (
                            <>
                                <TextField
                                    size="small"
                                    label="Marka adı"
                                    value={renameValue}
                                    autoFocus
                                    onChange={(e) => setRenameValue(e.target.value)}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter") void rename(brand);
                                        if (e.key === "Escape") setRenamingId(null);
                                    }}
                                    sx={{ flexGrow: 1 }}
                                />
                                <Button size="small" variant="contained" startIcon={<IconCheck size={16} />} disabled={busy} onClick={() => void rename(brand)}>
                                    Kaydet
                                </Button>
                                <IconButton size="small" aria-label="Vazgeç" onClick={() => setRenamingId(null)}>
                                    <IconX size={18} />
                                </IconButton>
                            </>
                        ) : (
                            <>
                                <Typography sx={{ flexGrow: 1, fontWeight: 600 }}>{brand.name}</Typography>
                                <Chip size="small" variant="outlined" label={`${brand.productCount} ürün`} />
                                {canManage && (
                                    <>
                                        <IconButton
                                            size="small"
                                            aria-label={`${brand.name} markasının adını değiştir`}
                                            onClick={() => {
                                                setRenamingId(brand.id);
                                                setRenameValue(brand.name);
                                            }}
                                        >
                                            <IconPencil size={18} />
                                        </IconButton>
                                        <IconButton size="small" color="error" aria-label={`${brand.name} markasını sil`} onClick={() => setDeleting(brand)}>
                                            <IconTrash size={18} />
                                        </IconButton>
                                    </>
                                )}
                            </>
                        )}
                    </Stack>
                ))
            )}
            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Markayı sil</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting ? `"${deleting.name}" markası silinecek.` : ""}</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
                        Sil
                    </Button>
                </DialogActions>
            </Dialog>
        </DashboardCard>
    );
}
