"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import FormAlert from "@/components/auth/FormAlert";
import ProductForm from "@/components/catalog/form/ProductForm";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { catalog, type ProductDto } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { useSession } from "@/session/SessionProvider";

/** Ürün/hizmet kartı sayfası: başlık, pasifleştirme, silme ve sekmeli form. */
export default function ProductDetail({ id }: { id: string }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canRead = hasPermission("katalog.read");
    const canManage = hasPermission("katalog.manage");

    const [product, setProduct] = useState<ProductDto | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [actionError, setActionError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [confirmDelete, setConfirmDelete] = useState(false);

    useEffect(() => {
        if (!canRead) return;
        let ignore = false;
        catalog.getProduct(id).then(
            (dto) => {
                if (!ignore) setProduct(dto);
            },
            (e) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setLoadError(e);
            },
        );
        return () => {
            ignore = true;
        };
    }, [id, canRead]);

    if (!canRead) return <Alert severity="warning">Ürünleri görüntülemek için yetkiniz yok.</Alert>;
    if (loadError) return <FormAlert error={loadError} />;
    if (!product) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress />
            </Box>
        );
    }

    const run = async (action: () => Promise<void>) => {
        setBusy(true);
        setActionError(null);
        try {
            await action();
        } catch (e) {
            setActionError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    const toggleActive = () =>
        run(async () => {
            await catalog.setProductActive(product.id, !product.isActive);
            setProduct(await catalog.getProduct(product.id));
        });

    const remove = () => {
        setConfirmDelete(false);
        return run(async () => {
            await catalog.deleteProduct(product.id);
            router.push(product.kind === "service" ? "/stok/hizmetler" : "/stok/urunler");
        });
    };

    return (
        <>
            <PageHeader title={product.name} subtitle={`${product.code} · ${product.kind === "service" ? "Hizmet" : "Ürün"}`} />
            <Stack direction="row" spacing={1} sx={{ mb: 2, alignItems: "center" }}>
                {product.isActive ? <Chip size="small" color="success" variant="outlined" label="Aktif" /> : <Chip size="small" label="Pasif" />}
                {canManage && (
                    <>
                        <Button size="small" variant="outlined" disabled={busy} onClick={() => void toggleActive()}>
                            {product.isActive ? "Pasifleştir" : "Aktifleştir"}
                        </Button>
                        <Button size="small" color="error" variant="outlined" disabled={busy} onClick={() => setConfirmDelete(true)}>
                            Sil
                        </Button>
                    </>
                )}
            </Stack>
            <FormAlert error={actionError} />
            {!canManage && (
                <Alert severity="info" sx={{ mb: 2 }}>
                    Kartı düzenlemek için yetkiniz yok; alanlar salt okunur.
                </Alert>
            )}
            <DashboardCard>
                <ProductForm product={product} canManage={canManage} onSaved={setProduct} onProductChange={setProduct} />
            </DashboardCard>

            <Dialog open={confirmDelete} onClose={() => setConfirmDelete(false)}>
                <DialogTitle>Kart silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{product.name} kartı kalıcı olarak silinecek. Bu işlem geri alınamaz.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmDelete(false)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void remove()}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
