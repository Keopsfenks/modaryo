"use client";

import { useCallback, useEffect, useState } from "react";
import Box from "@mui/material/Box";
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
import { IconArrowDown, IconArrowUp, IconCheck, IconGripVertical, IconPencil, IconPlus, IconTrash, IconX } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { catalog, type CategoryNode } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { useSession } from "@/session/SessionProvider";

/** Kategori ağacı: satır içi yeniden adlandırma, alt kategori ekleme, silme ve sıralama (sürükle veya ↑/↓). */
export default function CategoryTree() {
    const { hasPermission } = useSession();
    const canManage = hasPermission("katalog.manage");

    const [tree, setTree] = useState<CategoryNode[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [renamingId, setRenamingId] = useState<string | null>(null);
    const [renameValue, setRenameValue] = useState("");
    const [addingTo, setAddingTo] = useState<string | null>(null);
    const [addValue, setAddValue] = useState("");
    const [rootValue, setRootValue] = useState("");
    const [deleting, setDeleting] = useState<CategoryNode | null>(null);
    const [dragId, setDragId] = useState<string | null>(null);

    const load = useCallback(async () => {
        try {
            setTree(await catalog.categories());
        } catch (e) {
            setError(errorMessage(e));
            setTree((current) => current ?? []);
        }
    }, []);

    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [load]);

    /** Bir işlemi çalıştırır, hata ileti olarak gösterilir; ardından ağaç yenilenir. */
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

    async function add(name: string, parentId: string | null) {
        const trimmed = name.trim();
        if (!trimmed) return;
        if (await run(() => catalog.createCategory(trimmed, parentId))) {
            if (parentId === null) setRootValue("");
            else {
                setAddingTo(null);
                setAddValue("");
            }
        }
    }

    async function rename(node: CategoryNode) {
        const trimmed = renameValue.trim();
        if (!trimmed) return;
        if (trimmed === node.name) {
            setRenamingId(null);
            return;
        }
        if (await run(() => catalog.updateCategory(node.id, { name: trimmed, parentId: node.parentId, sort: node.sort }))) {
            setRenamingId(null);
        }
    }

    /** Kardeşleri yeni sıraya göre 0..n-1 olarak yeniden numaralar; yalnızca değişenler kaydedilir. */
    async function reorder(siblings: CategoryNode[], orderedIds: string[]) {
        const byId = new Map(siblings.map((s) => [s.id, s]));
        await run(async () => {
            for (const [index, id] of orderedIds.entries()) {
                const node = byId.get(id)!;
                if (node.sort !== index) {
                    await catalog.updateCategory(node.id, { name: node.name, parentId: node.parentId, sort: index });
                }
            }
        });
    }

    function move(siblings: CategoryNode[], index: number, delta: -1 | 1) {
        const ids = siblings.map((s) => s.id);
        [ids[index], ids[index + delta]] = [ids[index + delta], ids[index]];
        void reorder(siblings, ids);
    }

    function drop(siblings: CategoryNode[], targetId: string) {
        const source = dragId;
        setDragId(null);
        if (!source || source === targetId || !siblings.some((s) => s.id === source)) return;
        const ids = siblings.map((s) => s.id).filter((id) => id !== source);
        ids.splice(ids.indexOf(targetId), 0, source);
        void reorder(siblings, ids);
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        await run(() => catalog.deleteCategory(target.id));
    }

    function renderLevel(siblings: CategoryNode[], depth: number): React.ReactNode {
        return siblings.map((node, index) => (
            <Box key={node.id}>
                <Stack
                    direction="row"
                    spacing={0.5}
                    draggable={canManage && renamingId !== node.id}
                    onDragStart={() => setDragId(node.id)}
                    onDragOver={(e) => dragId && e.preventDefault()}
                    onDrop={() => drop(siblings, node.id)}
                    onDragEnd={() => setDragId(null)}
                    sx={{
                        alignItems: "center",
                        pl: depth * 3,
                        py: 0.5,
                        borderBottom: 1,
                        borderColor: "divider",
                        opacity: dragId === node.id ? 0.5 : 1,
                    }}
                >
                    {canManage && <IconGripVertical size={18} aria-hidden style={{ cursor: "grab", flexShrink: 0 }} />}
                    {renamingId === node.id ? (
                        <>
                            <TextField
                                size="small"
                                label="Kategori adı"
                                value={renameValue}
                                autoFocus
                                onChange={(e) => setRenameValue(e.target.value)}
                                onKeyDown={(e) => {
                                    if (e.key === "Enter") void rename(node);
                                    if (e.key === "Escape") setRenamingId(null);
                                }}
                                sx={{ flexGrow: 1 }}
                            />
                            <Button size="small" variant="contained" startIcon={<IconCheck size={16} />} disabled={busy} onClick={() => void rename(node)}>
                                Kaydet
                            </Button>
                            <IconButton size="small" aria-label="Vazgeç" onClick={() => setRenamingId(null)}>
                                <IconX size={18} />
                            </IconButton>
                        </>
                    ) : (
                        <>
                            <Typography sx={{ flexGrow: 1, fontWeight: 600 }}>{node.name}</Typography>
                            <Chip size="small" variant="outlined" label={`${node.productCount} ürün`} />
                            {canManage && (
                                <>
                                    <IconButton size="small" aria-label={`${node.name} yukarı taşı`} disabled={busy || index === 0} onClick={() => move(siblings, index, -1)}>
                                        <IconArrowUp size={18} />
                                    </IconButton>
                                    <IconButton
                                        size="small"
                                        aria-label={`${node.name} aşağı taşı`}
                                        disabled={busy || index === siblings.length - 1}
                                        onClick={() => move(siblings, index, 1)}
                                    >
                                        <IconArrowDown size={18} />
                                    </IconButton>
                                    <IconButton
                                        size="small"
                                        aria-label={`${node.name} altına ekle`}
                                        onClick={() => {
                                            setAddingTo(node.id);
                                            setAddValue("");
                                        }}
                                    >
                                        <IconPlus size={18} />
                                    </IconButton>
                                    <IconButton
                                        size="small"
                                        aria-label={`${node.name} adını değiştir`}
                                        onClick={() => {
                                            setRenamingId(node.id);
                                            setRenameValue(node.name);
                                        }}
                                    >
                                        <IconPencil size={18} />
                                    </IconButton>
                                    <IconButton size="small" color="error" aria-label={`${node.name} kategorisini sil`} onClick={() => setDeleting(node)}>
                                        <IconTrash size={18} />
                                    </IconButton>
                                </>
                            )}
                        </>
                    )}
                </Stack>
                {addingTo === node.id && (
                    <Stack direction="row" spacing={1} sx={{ pl: (depth + 1) * 3, py: 1 }}>
                        <TextField
                            size="small"
                            label="Alt kategori adı"
                            value={addValue}
                            autoFocus
                            onChange={(e) => setAddValue(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter") void add(addValue, node.id);
                                if (e.key === "Escape") setAddingTo(null);
                            }}
                        />
                        <Button variant="contained" size="small" disabled={busy} onClick={() => void add(addValue, node.id)}>
                            Ekle
                        </Button>
                        <Button size="small" onClick={() => setAddingTo(null)}>
                            Vazgeç
                        </Button>
                    </Stack>
                )}
                {renderLevel(node.children, depth + 1)}
            </Box>
        ));
    }

    return (
        <DashboardCard title="Kategoriler" subtitle="En fazla 5 seviye. Sürükleyerek veya okları kullanarak sıralayın.">
            <FormAlert error={error} />
            {canManage && (
                <Stack direction="row" spacing={1} sx={{ mb: 2 }}>
                    <TextField
                        size="small"
                        label="Yeni kategori"
                        value={rootValue}
                        onChange={(e) => setRootValue(e.target.value)}
                        onKeyDown={(e) => e.key === "Enter" && void add(rootValue, null)}
                        sx={{ minWidth: 240 }}
                    />
                    <Button variant="contained" disabled={busy || !rootValue.trim()} onClick={() => void add(rootValue, null)}>
                        Kategori ekle
                    </Button>
                </Stack>
            )}
            {tree === null ? (
                <Skeleton variant="rounded" height={160} />
            ) : tree.length === 0 ? (
                <Typography color="text.secondary" sx={{ py: 3 }}>
                    Henüz kategori yok.
                </Typography>
            ) : (
                renderLevel(tree, 0)
            )}
            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Kategoriyi sil</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting ? `"${deleting.name}" kategorisi silinecek.` : ""}</DialogContentText>
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
