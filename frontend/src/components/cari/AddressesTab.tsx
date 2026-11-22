import { useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Chip from "@mui/material/Chip";
import Grid from "@mui/material/Grid";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { erp, type AddressDto, type CurrentAccountDto } from "@/lib/api/erp";
import AddressDialog from "./AddressDialog";

type AddressesTabProps = {
    account: CurrentAccountDto;
    canManage: boolean;
    /** Adres değişiminden sonra sunucudan yeniden okunan kart. */
    onChanged: (account: CurrentAccountDto) => void;
};

/** Adres kartları. Her değişiklikten sonra kart yeniden okunur; varsayılan rozetleri sunucudan gelir. */
export default function AddressesTab({ account, canManage, onChanged }: AddressesTabProps) {
    const [editing, setEditing] = useState<AddressDto | "new" | null>(null);
    const [deleting, setDeleting] = useState<AddressDto | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    const reload = async () => {
        onChanged(await erp.getAccount(account.id));
    };

    const remove = async (address: AddressDto) => {
        setError(null);
        try {
            await erp.deleteAddress(account.id, address.id);
            await reload();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    return (
        <DashboardCard
            title="Adresler"
            action={
                canManage && (
                    <Button variant="contained" onClick={() => setEditing("new")}>
                        Adres ekle
                    </Button>
                )
            }
        >
            <FormAlert error={error} />
            {account.addresses.length === 0 ? (
                <Typography color="text.secondary">Henüz adres yok</Typography>
            ) : (
                <Grid container spacing={2}>
                    {account.addresses.map((a) => (
                        <Grid key={a.id} size={{ xs: 12, md: 6 }}>
                            <Box sx={{ border: 1, borderColor: "divider", borderRadius: 1, p: 2 }}>
                                <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                    <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                                        {a.title}
                                    </Typography>
                                    {canManage && (
                                        <Box>
                                            <Button size="small" aria-label={`${a.title} adresini düzenle`} onClick={() => setEditing(a)}>
                                                Düzenle
                                            </Button>
                                            <Button size="small" color="error" aria-label={`${a.title} adresini sil`} onClick={() => setDeleting(a)}>
                                                Sil
                                            </Button>
                                        </Box>
                                    )}
                                </Box>
                                <Typography>{a.line}</Typography>
                                <Typography color="text.secondary">{[a.district, a.city, a.postalCode, a.country].filter(Boolean).join(", ")}</Typography>
                                <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.5, mt: 1 }}>
                                    {a.isBilling && <Chip size="small" label="Fatura" />}
                                    {a.isShipping && <Chip size="small" label="Teslimat" />}
                                    {a.isDefaultBilling && <Chip size="small" color="primary" label="Varsayılan fatura" />}
                                    {a.isDefaultShipping && <Chip size="small" color="primary" label="Varsayılan teslimat" />}
                                </Box>
                            </Box>
                        </Grid>
                    ))}
                </Grid>
            )}
            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Adres silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.title} adresi silinecek.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>İptal</Button>
                    <Button
                        color="error"
                        variant="contained"
                        onClick={() => {
                            const target = deleting;
                            setDeleting(null);
                            if (target) void remove(target);
                        }}
                    >
                        Adresi sil
                    </Button>
                </DialogActions>
            </Dialog>
            {editing && (
                <AddressDialog
                    accountId={account.id}
                    address={editing === "new" ? undefined : editing}
                    onClose={() => setEditing(null)}
                    onSaved={async () => {
                        setEditing(null);
                        await reload();
                    }}
                />
            )}
        </DashboardCard>
    );
}
